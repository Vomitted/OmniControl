using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OmniHub.App.Wpf;

namespace OmniHub.App;

public partial class App : Application
{
    // Held for the process lifetime -- a local variable would be eligible for GC
    // (and release the mutex) as soon as OnStartup returns. Guards every launch
    // path (GUI and all three CLI modes), since two instances -- whether two GUI
    // windows or a GUI plus a headless run -- would double up BIOS polling and can
    // actively fight each other for fan control via competing SetFanLevel calls.
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "Local\\OmniHub_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "OmniHub is already running -- check your system tray icon, or Task Manager's " +
                "Details tab (not just the Processes search) if you don't see it there.",
                "OmniHub already running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].Equals("-Probe", StringComparison.OrdinalIgnoreCase))
        {
            Program.RunProbeCli();
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].Equals("-Calibrate", StringComparison.OrdinalIgnoreCase))
        {
            Program.RunCalibrateCli();
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0].Equals("-RunHeadless", StringComparison.OrdinalIgnoreCase))
        {
            Program.RunHeadlessCli();
            Shutdown();
            return;
        }

        try
        {
            // Applied before the window is constructed so it opens already in the saved
            // theme, rather than painting the default palette and re-tinting a frame after.
            ThemeManager.Apply(AppSettings.Load().ThemeName);

            var window = new MainWindow();
            window.Show();
        }
        catch (Exception ex)
        {
            // Full ex.ToString() (not just ex.Message) deliberately -- a XAML load
            // failure's real cause is almost always in the InnerException, and the
            // outer message alone ("TypeConverterMarkupExtension threw an exception")
            // is too generic to act on.
            MessageBox.Show(ex.ToString(), "OmniHub failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
