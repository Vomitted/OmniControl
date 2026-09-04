using System.Windows;
using Window = System.Windows.Window;
using OmniHub.Core.Fan;
using OmniHub.Core.Hardware;

namespace OmniHub.App.Wpf;

public partial class TrayFlyout : Window
{
    private readonly HardwareContext _ctx;
    private readonly FanService _service;
    private readonly AppSettings _settings;
    private readonly MainWindow _owner;

    public TrayFlyout(HardwareContext ctx, FanService service, AppSettings settings, MainWindow owner)
    {
        InitializeComponent();
        _ctx = ctx; _service = service; _settings = settings; _owner = owner;

        ModelLabel.Text = $"{ctx.Model.Manufacturer} {ctx.Model.Product}".Trim().ToUpperInvariant();
        RefreshModeText();

        // Paired, for the reason given in DashboardView: this window is hidden and re-shown
        // rather than recreated, and an unsubscribe with no matching re-subscribe leaves the
        // flyout showing whatever it last saw.
        Loaded += (_, _) =>
        {
            ctx.OnReading -= OnReading;
            ctx.OnReading += OnReading;
        };
        Unloaded += (_, _) => ctx.OnReading -= OnReading;
    }

    public void ShowNearTray()
    {
        var area = System.Windows.SystemParameters.WorkArea;
        Left = area.Right - Width - 12;
        Top = area.Bottom - Height - 12;
        Show();
        Activate();
    }

    private void RefreshModeText()
    {
        ModeText.Text = _settings.FanControlMode switch
        {
            FanControlMode.Auto => "Auto (Curve)",
            FanControlMode.BiosDefault => "BIOS Default",
            FanControlMode.Max => "Max Fan",
            _ => "--",
        };
        LevelText.Text = _service.IsRunning
            ? $"Commanded {_service.LastCommandedLevelPercent}%"
            : "Not actively controlling";
    }

    private void OnReading(Reading r)
    {
        // BeginInvoke: see DashboardView.OnReading. Raised on the poll thread, and this window
        // is usually hidden, so blocking the poll on it would be doubly wasteful.
        Dispatcher.BeginInvoke(() =>
        {
            // ° rather than a literal degree sign: the escape survives the file being
            // re-saved in another encoding, which is exactly how the reference app ended up
            // rendering "59AC" instead of "59°C" throughout its UI.
            TempText.Text = $"{r.TemperatureC}°C";
            ThrottleText.Text = r.Throttling == ThrottlingState.On ? "Throttling now" : "Normal";
            RefreshModeText();
        });
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _owner.RestoreFromTrayPublic();
    }

    private void Window_Deactivated(object sender, EventArgs e) => Hide();
}
