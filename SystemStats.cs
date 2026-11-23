using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MetroKafeDVD2ISO;

/// <param name="CpuTempC">Windows'un bildirdiği ACPI (anakart) sıcaklığı; gerçek işlemci çekirdek sıcaklığı değildir.</param>
/// <param name="GpuTempC">Yalnız NVIDIA kartlarda (nvidia-smi ile).</param>
/// <param name="GpuVideo">Video motorlarının en yoğunu. Intel kartlar kodlamayı da çözme motorunda (VideoDecode/
/// VideoProcessing) yapar, ayrı VideoEncode motoru yoktur; bu yüzden ekranda bu birleşik değer gösterilir.</param>
public sealed record SystemSnapshot(double CpuPercent, double? CpuTempC, long RamUsed, long RamTotal,
    double? Gpu3D, double? GpuEncode, double? GpuDecode, double? GpuTempC, double? GpuVideo = null);

/// <summary>
/// Yönetici izni ve ek sürücü gerektirmeden okunabilen sistem bilgileri. İşlemci çekirdek sıcaklığı Windows
/// tarafından doğrudan verilmez (çekirdek sürücüsü gerekir); onun yerine ACPI ısıl bölge sıcaklığı gösterilir.
/// </summary>
public sealed class SystemStats : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PDH_FMT_COUNTERVALUE_ITEM { public IntPtr Name; public uint CStatus; public uint Pad; public double Value; }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000, PDH_MORE_DATA = 0x800007D2;

    readonly IntPtr query, gpuCounter, thermalCounter;
    ulong lastIdle, lastTotal;
    string? nvidiaSmi;
    double? lastNvTemp;
    long lastNvTicks;

    public SystemStats()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) == 0)
        {
            PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out gpuCounter);
            PdhAddEnglishCounter(query, @"\Thermal Zone Information(*)\Temperature", IntPtr.Zero, out thermalCounter);
            PdhCollectQueryData(query);
        }
        GetSystemTimes(out var idle, out var kernel, out var user);
        lastIdle = idle.Value;
        lastTotal = kernel.Value + user.Value;

        string smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (File.Exists(smi)) nvidiaSmi = smi;
    }

    /// <summary>Bilgisayardaki ekran kartlarının adları (tanılama için).</summary>
    public static List<string> GpuNames()
    {
        var names = new List<string>();
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            if (!string.IsNullOrWhiteSpace(dd.DeviceString) && !names.Contains(dd.DeviceString)) names.Add(dd.DeviceString.Trim());
            dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return names;
    }

    /// <summary>Son çağrıdan bu yana geçen süredeki değerler (yaklaşık 1 sn arayla çağrılmalı).</summary>
    public SystemSnapshot Sample()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        ulong total = kernel.Value + user.Value, idleNow = idle.Value;
        ulong dTotal = total - lastTotal, dIdle = idleNow - lastIdle;
        double cpu = dTotal > 0 ? Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100) : 0;
        lastTotal = total;
        lastIdle = idleNow;

        var mem = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);

        double? g3d = null, genc = null, gdec = null, gvideo = null, temp = null;
        if (query != IntPtr.Zero && PdhCollectQueryData(query) == 0)
        {
            var gpu = ReadArray(gpuCounter);
            if (gpu.Count > 0)
            {
                // Örnek ad: pid_1234_luid_0x0_0x1234_phys_0_eng_3_engtype_VideoEncode. Görev Yöneticisi gibi: aynı motordaki
                // süreçler toplanır, her tür için en yoğun motor gösterilir.
                var byEngine = gpu
                    .Select(x => (Key: x.Name[(x.Name.IndexOf("_luid_", StringComparison.Ordinal) + 1)..], x.Value))
                    .GroupBy(x => x.Key)
                    .Select(g => (Key: g.Key, Sum: g.Sum(v => v.Value)))
                    .ToList();
                double Max(string type) => byEngine.Where(e => e.Key.EndsWith("engtype_" + type, StringComparison.OrdinalIgnoreCase))
                                                   .Select(e => e.Sum).DefaultIfEmpty(0).Max();
                g3d = Math.Min(100, Max("3D"));
                genc = Math.Min(100, Max("VideoEncode"));
                gdec = Math.Min(100, Max("VideoDecode"));
                gvideo = Math.Min(100, new[] { genc, gdec, Max("VideoProcessing"), Max("VideoCodec") }.Max() ?? 0);
            }
            var thermal = ReadArray(thermalCounter);
            if (thermal.Count > 0)
            {
                // Kelvin; bazı bilgisayarlar anlamsız sabit değer (ör. 273 K = 0 °C) bildirir, gerçekçi değilse gösterme
                double c = thermal.Max(t => t.Value) - 273.15;
                if (c is >= 20 and <= 110) temp = c;
            }
        }

        return new SystemSnapshot(cpu, temp, (long)(mem.TotalPhys - mem.AvailPhys), (long)mem.TotalPhys, g3d, genc, gdec, NvidiaTemperature(), gvideo);
    }

    List<(string Name, double Value)> ReadArray(IntPtr counter)
    {
        var result = new List<(string, double)>();
        if (counter == IntPtr.Zero) return result;
        uint size = 0;
        if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA || size == 0)
            return result;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, buf) != 0) return result;
            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buf + i * itemSize);
                if (item.CStatus == 0) result.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        return result;
    }

    // nvidia-smi her çağrıda yeni süreç başlattığı için en çok 3 sn'de bir sorulur
    double? NvidiaTemperature()
    {
        if (nvidiaSmi == null) return null;
        if (Environment.TickCount64 - lastNvTicks < 3000) return lastNvTemp;
        lastNvTicks = Environment.TickCount64;
        try
        {
            var psi = new ProcessStartInfo(nvidiaSmi, "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string text = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000)) { p.Kill(); return lastNvTemp; }
            var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            lastNvTemp = double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : null;
        }
        catch
        {
            nvidiaSmi = null; // bir daha deneme
            lastNvTemp = null;
        }
        return lastNvTemp;
    }

    public static string Format(SystemSnapshot s)
    {
        static string Gb(long b) => (b / (1024.0 * 1024 * 1024)).ToString("0.0");
        string cpu = $"İşlemci %{s.CpuPercent:0}" + (s.CpuTempC is { } ct ? $" · {ct:0} °C (anakart)" : "");
        string ram = $"RAM {Gb(s.RamUsed)} / {Gb(s.RamTotal)} GB";
        string gpu = s.Gpu3D == null
            ? "Ekran kartı: —"
            : $"Ekran kartı: video kodlama/çözme %{s.GpuVideo:0} · 3D %{s.Gpu3D:0}" + (s.GpuTempC is { } gt ? $" · {gt:0} °C" : "");
        return $"{cpu}     {ram}     {gpu}";
    }

    public void Dispose()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
    }
}
