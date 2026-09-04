using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OmniHub.Core.Fan;
using OmniHub.Core.Hardware;
using OmniHub.Core.Optimize;

namespace OmniHub.App.Wpf;

/// <summary>
/// A small always-on-top readout for while you are doing something else.
///
/// Click-through is the whole point and is done with window styles rather than by trying to
/// dodge the mouse: WS_EX_TRANSPARENT makes hit-testing pass straight through to whatever is
/// underneath, WS_EX_NOACTIVATE stops it stealing focus from a game, and WS_EX_TOOLWINDOW
/// keeps it out of Alt-Tab. Without these it would be a window that sits on top of your game
/// and swallows clicks, which is worse than no overlay at all.
///
/// KNOWN LIMIT, because it is the first thing anyone hits: this draws as a normal desktop
/// window, so it appears over borderless and windowed games but NOT over true fullscreen
/// exclusive ones. Those composite through the display driver and the only way in is to hook
/// the graphics API, which is what RTSS exists to do. Being honest about that is better than
/// shipping something that mysteriously vanishes in half your games.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly HardwareContext _ctx;
    private readonly AppSettings _settings;
    private AmdTuning? _tuning;
    private DispatcherTimer? _powerTimer;
    private string _powerLabel = "--";

    public OverlayWindow(HardwareContext ctx, AppSettings settings)
    {
        InitializeComponent();
        _ctx = ctx;
        _settings = settings;

        if (ctx.Smu is { } smu)
        {
            var tuning = new AmdTuning(smu);
            if (tuning.IsSupported) _tuning = tuning;
        }

        // No SMU means no package power to show. Hiding the row is better than a permanent
        // "--" that looks like a fault.
        if (_tuning is null) PowerRow.Visibility = Visibility.Collapsed;
        else StartPowerUpdates();

        // Re-place on resize: SizeToContent means the size is not known until the window has
        // laid out, and a corner-anchored window placed before that lands in the wrong spot.
        SizeChanged += (_, _) => MoveToCorner();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);

        MoveToCorner();
    }

    /// <summary>Places the window in the configured screen corner, clear of the taskbar.</summary>
    public void MoveToCorner()
    {
        var area = SystemParameters.WorkArea;
        const double margin = 14;

        Left = _settings.OverlayCorner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft
            ? area.Left + margin
            : area.Right - ActualWidth - margin;

        Top = _settings.OverlayCorner is OverlayCorner.TopLeft or OverlayCorner.TopRight
            ? area.Top + margin
            : area.Bottom - ActualHeight - margin;
    }

    /// <summary>
    /// Refreshes from a poll the app already performs, so the overlay costs nothing extra for
    /// temperature and fan speed.
    /// </summary>
    public void Update(Reading r, FanService service)
    {
        double tempC = double.IsNaN(r.PreciseTemperatureC) ? r.TemperatureC : r.PreciseTemperatureC;
        bool fromDie = r.TemperatureSource == TemperatureSource.SmuDieTctl;

        // Filtered, matching the dashboard. Two readouts of the same sensor disagreeing by ten
        // degrees because one caught a die excursion and the other did not is worse than either
        // number on its own.
        // The context's CpuTrend, for the same two reasons the dashboard uses it: FanService's
        // Trend is the control temperature (max of CPU and GPU) while this readout is labelled
        // DIE, and FanService stops outside Auto fan mode whereas the poll loop never does.
        double displayC = _ctx.CpuTrend.HasEnoughData ? _ctx.CpuTrend.FilteredTempC : tempC;
        TempText.Text = fromDie ? $"{displayC:0.0}°" : $"{Math.Round(displayC):0}°";
        SourceLabel.Text = fromDie ? "OMNIHUB · DIE" : "OMNIHUB · ACPI";

        FanText.Text = $"{FanService.RawToRpm(r.FanLevel1)}";
        PowerText.Text = _powerLabel;

        StateText.Text = r.Throttling == ThrottlingState.On ? "THROTTLING"
            : r.MaxFanActive ? "MAX FAN"
            : service.IsRunning ? $"CURVE {service.LastCommandedLevelPercent}%"
            : "BIOS AUTO";
    }

    /// <summary>
    /// Package power on its own slow timer.
    ///
    /// Separate from the reading tick because it is the only value here that costs a driver
    /// round trip of its own -- the PM table has to be refreshed and re-read -- and five
    /// seconds is plenty for a number that is an average over a window anyway.
    /// </summary>
    private void StartPowerUpdates()
    {
        _powerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _powerTimer.Tick += (_, _) =>
        {
            var tuning = _tuning;
            if (tuning is null) return;

            Task.Run(() => tuning.ReadPower()).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                _powerLabel = t.Result is { } p ? $"{p.StapmWatts:0.0}W" : "--";
            });
        };

        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _powerTimer.Start();
            else _powerTimer.Stop();
        };
    }
}
