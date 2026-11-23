# MetroKafe DVD2ISO

DVD ve CD'lerden ISO kalıbı oluşturan, sade ve modern bir Windows uygulaması.

*English: see [below](#english).*

## Özellikler
- DVD/CD sürücülerini otomatik bulur; disk takılıp çıkarılınca listeyi yeniler
- Disk boyutunu dosya sisteminden (ISO9660/UDF) okur; yazılabilir disklerin sonundaki boş sektörleri hata saymaz
- Diske erişen işler ayrı işlemde çalışır; sürücü takılsa da pencere donmaz, çarpı her zaman çalışır
- Kayıt konumu (varsayılan Masaüstü) ve dosya adı ayrı seçilir
- Hazır dosya adları: Disk adı, Disk adı + tarih, Disk adı + tarih + saat, Tarih + saat
- Anlık kopyalama hızı (MB/s ve DVD/CD hız katı), kalan süre, hatalı sektör sayısı
- Çizik/hasarlı disk uyarısı: okuma takıldığında veya belirgin şekilde yavaşladığında bilgi verir
- Okunamayan sektörleri tekrar dener; okunamazsa sıfırla doldurup devam eder (isteğe bağlı)
- Sürücü takılı kaldığında bile İptal çalışır
- Boş alan ve FAT32 4 GB sınırı denetimi
- Takılan bölgeyi belirlenen süre sonra atlama; önce sağlam kısımları kopyalayıp hasarlı bölgelere en sonda dönme
- Hasarlı bölgede sürücü hızını düşürme
- Bitince doğrulama (diskle karşılaştırma + SHA-256) ve diski çıkarma
- Video CD / Super Video CD: ISO diskteki dosyalardan oluşturulur (görüntü sektörleri ISO'ya sektör sektör sığmaz)
- Kopyalama yöntemi: Otomatik / Sektör sektör / Dosya tabanlı
- Ayarlar sekmesi (seçenekler kalıcı)
- Ayarlar sayfasının en üstünde güncelleme bölümü, açılışta otomatik denetim ve tek tıkla güncelleme (GitHub Releases)
- Arayüz Türkçe ve İngilizce kullanılabilir; ilk açılışta Windows görüntüleme diline uyar, sağ üstteki bayrak menüsünden değiştirilebilir

## MP4'e dönüştürme
"MP4'e Dönüştür" sekmesi ISO'ları [HandBrake](https://handbrake.fr) komut satırı aracıyla MP4'e çevirir: çözünürlük ve kare hızı kaynakla aynı kalır, yalnız ortalama bit hızı seçilir (yanında tahmini boyut görünür). Toplu dönüştürme, ekran kartıyla kodlama ve taramalı görüntü düzeltme desteklenir. DVD (VIDEO_TS) ve Video CD (MPEGAV) ISO'ları dönüştürülebilir; her ISO'nun bit hızı MP4 yaklaşık ISO boyutunda çıkacak şekilde otomatik hesaplanır ve tabloda ayrı ayrı değiştirilebilir; hazır bit hızları Ayarlar'dan değiştirilir. HandBrakeCLI (GPL) programa gömülü değildir; ilk kullanımda izin istenip resmi GitHub sürümlerinden indirilir.

## Kullanım
1. Diski takın, uygulamayı açın.
2. Sürücüyü, kayıt konumunu ve dosya adını seçin.
3. **ISO Oluştur**'a basın.

İndirme: [Releases](../../releases) sayfasındaki `MetroKafeDVD2ISO.exe` (kurulum gerekmez, .NET gerektirmez).

> Not: Kopya korumalı (CSS) film DVD'lerinin korumasını kaldırmaz.

## Derleme
.NET 10 SDK gerekir.

```bash
dotnet publish -c Release -o publish
```

Çıktı: `publish/MetroKafeDVD2ISO.exe` (tek dosya, Windows x64).

## Yeni sürüm yayınlama
1. `MetroKafeDVD2ISO.csproj` içindeki `<Version>` değerini artırın.
2. `CHANGELOG.md` dosyasına sürüm notlarını ekleyin.
3. Derleyip `vX.Y.Z` etiketiyle bir GitHub Release oluşturun ve exe'yi ekleyin.

---

## English

MetroKafe DVD2ISO is a simple, modern Windows app that creates ISO images from DVDs and CDs, with recovery features for scratched or damaged discs, and converts ISOs to MP4.

### Features
- Finds DVD/CD drives automatically and refreshes the list when a disc is inserted or removed
- Reads the disc size from the file system (ISO9660/UDF); empty sectors at the end of recordable discs are not counted as errors
- All disc access runs in a separate process, so the window never freezes even if the drive hangs, and the close button always works
- Save location (Desktop by default) and file name are chosen separately, with file name presets (disc label, date, time)
- Live copy speed (MB/s and DVD/CD speed multiplier), remaining time and bad sector count
- Scratched/damaged disc warning when reading stalls or slows down noticeably
- Retries unreadable sectors; if they still can't be read, fills them with zeros and continues (optional)
- Skips a stuck area after a set time; copies the healthy parts first and returns to damaged areas at the end; lowers the drive speed in damaged areas
- Verify when done (compare with the disc + SHA-256) and eject the disc
- Video CD / Super Video CD: the ISO is built from the files on the disc (the video sectors don't fit into ISO sectors one-to-one)
- Copy method: Auto / Sector by sector / File-based
- Free space and FAT32 4 GB limit checks
- Settings tab (options are saved), updates page with automatic check at startup and one-click update (GitHub Releases)
- The UI is available in Turkish and English; it follows the Windows display language on first start and can be switched from the flag menu at the top right

### Converting to MP4
The "Convert to MP4" tab converts ISOs to MP4 with the [HandBrake](https://handbrake.fr) command-line tool: resolution and frame rate stay the same as the source, and you only choose the average bitrate (the estimated size is shown next to it). Batch conversion, GPU encoding and deinterlacing are supported. DVD (VIDEO_TS) and Video CD (MPEGAV) ISOs can be converted; each ISO's bitrate is calculated automatically so the MP4 comes out at about the size of the ISO, and can be changed per ISO in the table; the preset bitrates can be changed in Settings. HandBrakeCLI (GPL) is not bundled with the program; on first use it is downloaded, with your permission, from the official GitHub releases.

### Usage
1. Insert the disc and open the app.
2. Choose the drive, save location and file name.
3. Click **Create ISO**.

Download: `MetroKafeDVD2ISO.exe` from the [Releases](../../releases) page (no installation, no .NET required).

> Note: It does not remove copy protection (CSS) from movie DVDs.

### Building
Requires the .NET 10 SDK.

```bash
dotnet publish -c Release -o publish
```

Output: `publish/MetroKafeDVD2ISO.exe` (single file, Windows x64).
