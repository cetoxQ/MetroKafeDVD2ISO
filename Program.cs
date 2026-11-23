namespace MetroKafeDVD2ISO;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Diske erişen işler ayrı, penceresiz bir işlemde çalışır (bkz. Worker)
        if (Worker.IsCommand(args))
        {
            L.Init(Environment.GetEnvironmentVariable(L.EnvVar));
            return Worker.Run(args);
        }

        ApplicationConfiguration.Initialize();
        // Dil değişince pencere yeni dilde, aynı yerde yeniden kurulur
        Point? location = null;
        while (true)
        {
            L.Init(AppSettings.Load().Language);
            var form = new MainForm();
            if (location is { } p) { form.StartPosition = FormStartPosition.Manual; form.Location = p; }
            Application.Run(form);
            if (!form.RestartRequested) return 0;
            location = form.Location;
        }
    }
}
