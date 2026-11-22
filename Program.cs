namespace MetroKafeDVD2ISO;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Diske erişen işler ayrı, penceresiz bir işlemde çalışır (bkz. Worker)
        if (Worker.IsCommand(args)) return Worker.Run(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
