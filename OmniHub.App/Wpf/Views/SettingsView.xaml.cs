using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniHub.App.Wpf;
using UserControl = System.Windows.Controls.UserControl;
using RadioButton = System.Windows.Controls.RadioButton;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace OmniHub.App.Wpf.Views;

public partial class SettingsView : UserControl
{
    private readonly AppSettings _settings;
    private bool _suppressEvents;

    public SettingsView(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        _suppressEvents = true;
        if (_settings.CloseBehavior == CloseBehavior.Exit) ExitCloseBtn.IsChecked = true;
        else TrayCloseBtn.IsChecked = true;

        // Snap the saved lead to whichever preset it matches; anything else (a hand-edited
        // settings file) falls back to Off rather than silently showing the wrong pill.
        var lead = _settings.PredictiveLeadSeconds;
        if (lead >= 20) Lead20Btn.IsChecked = true;
        else if (lead >= 10) Lead10Btn.IsChecked = true;
        else if (lead >= 5) Lead5Btn.IsChecked = true;
        else LeadOffBtn.IsChecked = true;

        LoggingToggle.IsChecked = _settings.ThermalLogging;
        _thermalLoggingAtLoad = _settings.ThermalLogging;
        InitialiseOverlayControls();
        _suppressEvents = false;

        RefreshLoggingChip();

        BuildThemeSwatches();

        // schtasks /Query is a subprocess call -- keep it off the UI thread so opening
        // this tab doesn't stall the window the way earlier synchronous BIOS calls did.
        Task.Run(() => StartupManager.IsEnabled()).ContinueWith(t =>
        {
            Dispatcher.Invoke(() =>
            {
                _suppressEvents = true;
                StartupToggle.IsChecked = t.Result;
                _suppressEvents = false;

                // Reflects the actual scheduled task, queried from Windows, not the setting
                // we would like to be true.
                SetChip(StartupChip, StartupChipText, t.Result, "ENABLED");
            });
        }, TaskScheduler.Default);
    }

    // Swatches are built in code rather than declared in XAML because each one previews its
    // own palette, which means loading that palette's dictionary and reading colours out of
    // it. A XAML DataTemplate can only bind to the *active* theme's brushes, so every swatch
    // would have looked identical -- the one thing a theme picker must not do.
    private void BuildThemeSwatches()
    {
        foreach (var theme in ThemeManager.All)
        {
            var dict = new ResourceDictionary { Source = new Uri(theme.Source, UriKind.Relative) };
            Color Pick(string key, Color fallback) => dict[key] is Color c ? c : fallback;

            var bg = Pick("BackgroundColor", Colors.Black);
            var panel = Pick("PanelColor", Colors.DimGray);
            var accent = Pick("AccentColor", Colors.SkyBlue);
            var text = Pick("TextPrimaryColor", Colors.White);
            var border = Pick("BorderColor", Colors.Gray);

            var preview = new Border
            {
                Width = 132,
                Height = 56,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(bg),
                BorderBrush = new SolidColorBrush(border),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        Swatch(panel), Swatch(text), Swatch(accent),
                    },
                },
            };

            var radio = new RadioButton
            {
                GroupName = "Theme",
                Tag = theme.Id,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 10, 10),
                IsChecked = string.Equals(theme.Id, _settings.ThemeName, StringComparison.OrdinalIgnoreCase),
                Content = new StackPanel
                {
                    Children =
                    {
                        preview,
                        new TextBlock
                        {
                            Text = theme.DisplayName,
                            Margin = new Thickness(2, 6, 0, 0),
                            FontSize = 12,
                            Foreground = (Brush)FindResource("TextPrimaryBrush"),
                        },
                        new TextBlock
                        {
                            Text = theme.Description,
                            Margin = new Thickness(2, 1, 0, 0),
                            FontSize = 10.5,
                            MaxWidth = 132,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Brush)FindResource("TextFaintBrush"),
                        },
                    },
                },
                Template = (ControlTemplate)FindResource("ThemeSwatchTemplate"),
            };
            radio.Checked += Theme_Checked;
            ThemeList.Items.Add(radio);
        }
    }

    private static Border Swatch(Color c) => new()
    {
        Width = 20,
        Height = 20,
        Margin = new Thickness(3, 0, 3, 0),
        CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush(c),
    };

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is RadioButton rb && rb.Tag is string id)
        {
            _settings.ThemeName = id;
            _settings.Save();
            ThemeManager.Apply(id);
        }
    }

    /// <summary>
    /// Fills the corner picker and syncs both overlay controls to the saved settings.
    /// Called from the constructor inside the _suppressEvents window, so populating the
    /// combo does not immediately fire its own SelectionChanged and re-save.
    /// </summary>
    private void InitialiseOverlayControls()
    {
        OverlayToggle.IsChecked = _settings.OverlayEnabled;

        OverlayCornerPicker.Items.Clear();
        foreach (var corner in Enum.GetValues<OverlayCorner>())
            OverlayCornerPicker.Items.Add(Describe(corner));
        OverlayCornerPicker.SelectedIndex = (int)_settings.OverlayCorner;

        _suppressOverlayEvents = true;
        OverlayOpacitySlider.Value = Math.Clamp(_settings.OverlayOpacity, 0.2, 1.0);
        OverlayOpacityLabel.Text = $"{OverlayOpacitySlider.Value * 100:0}%";
        _suppressOverlayEvents = false;

        // One checkbox per available metric, ticked if the user has it. Overlay order follows
        // the saved list, so re-ticking a metric appends it rather than restoring its old
        // position -- predictable enough not to need drag ordering.
        foreach (var (key, label) in Wpf.OverlayWindow.AvailableMetrics)
        {
            var box = new System.Windows.Controls.CheckBox
            {
                Style = (Style)FindResource("OmniCheckBoxStyle"),
                Content = label,
                IsChecked = _settings.OverlayMetrics.Contains(key),
                Margin = new Thickness(0, 0, 18, 8),
                Tag = key,
            };
            box.Checked += OverlayMetric_Changed;
            box.Unchecked += OverlayMetric_Changed;
            OverlayMetricList.Children.Add(box);
        }
    }

    private bool _suppressOverlayEvents;

    private void OverlayOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Slider.ValueChanged fires during InitializeComponent, before the label field exists.
        if (_suppressOverlayEvents || OverlayOpacityLabel is null) return;

        _settings.OverlayOpacity = OverlayOpacitySlider.Value;
        OverlayOpacityLabel.Text = $"{OverlayOpacitySlider.Value * 100:0}%";
        _settings.Save();
        Owner()?.RefreshOverlayAppearance();
    }

    private void OverlayMetric_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { Tag: string key } box) return;

        if (box.IsChecked == true)
        {
            if (!_settings.OverlayMetrics.Contains(key)) _settings.OverlayMetrics.Add(key);
        }
        else _settings.OverlayMetrics.Remove(key);

        _settings.Save();
        Owner()?.RefreshOverlayAppearance();
    }

    /// <summary>The MainWindow hosting this view, or null before it is up.</summary>
    private MainWindow? Owner() => Window.GetWindow(this) as MainWindow;

    private static string Describe(OverlayCorner corner) => corner switch
    {
        OverlayCorner.TopLeft => "Top left",
        OverlayCorner.TopRight => "Top right",
        OverlayCorner.BottomLeft => "Bottom left",
        _ => "Bottom right",
    };

    private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        _settings.OverlayEnabled = OverlayToggle.IsChecked == true;
        _settings.Save();
        (System.Windows.Application.Current.MainWindow as MainWindow)?.SetOverlayVisible(_settings.OverlayEnabled);
    }

    private void OverlayCorner_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || OverlayCornerPicker.SelectedIndex < 0) return;

        _settings.OverlayCorner = (OverlayCorner)OverlayCornerPicker.SelectedIndex;
        _settings.Save();
        (System.Windows.Application.Current.MainWindow as MainWindow)?.RefreshOverlayPosition();
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        bool enabled = StartupToggle.IsChecked == true;

        Task.Run(() =>
        {
            bool ok = false;
            try { ok = StartupManager.SetEnabled(enabled); } catch { }

            // Re-query rather than assume the write took: schtasks can report success while
            // policy blocks the task, and the chip must show the machine's state.
            bool actual = false;
            try { actual = StartupManager.IsEnabled(); } catch { }
            Dispatcher.Invoke(() => SetChip(StartupChip, StartupChipText, actual, "ENABLED"));

            if (!ok)
            {
                Dispatcher.Invoke(() =>
                {
                    _suppressEvents = true;
                    StartupToggle.IsChecked = !enabled;
                    _suppressEvents = false;
                    System.Windows.MessageBox.Show(
                        "Could not update the startup task.", "Startup",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
        });
    }

    /// <summary>
    /// StartMinimizedToTray existed in the settings file with no UI and no reader -- saved,
    /// serialised, and completely inert. It is wired to both ends now.
    /// </summary>
    private void StartMinimized_Changed(object sender, RoutedEventArgs e)
    {
        _settings.StartMinimizedToTray = StartMinimizedToggle.IsChecked == true;
        _settings.Save();
    }

    private void CloseBehavior_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            _settings.CloseBehavior = tag == "Exit" ? CloseBehavior.Exit : CloseBehavior.MinimizeToTray;
            _settings.Save();
        }
    }

    // Persisted only. Applying it live would mean changing the cooling behaviour of a loop
    // that is already running against a curve with its own hysteresis state, mid-flight;
    // taking effect on restart keeps the running session predictable.
    private void Lead_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is RadioButton rb && rb.Tag is string tag && double.TryParse(tag, out var seconds))
        {
            _settings.PredictiveLeadSeconds = seconds;
            _settings.Save();
        }
    }

    private void LoggingToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _settings.ThermalLogging = LoggingToggle.IsChecked == true;
        _settings.Save();
        RefreshLoggingChip();
    }

    // Whether logging was already running when this window opened. A box ticked just now is
    // saved but not yet writing, and the chip has to distinguish those two states.
    private bool _thermalLoggingAtLoad;

    private void RefreshLoggingChip()
    {
        bool wanted = _settings.ThermalLogging;

        // Three states, not two. "ON NEXT START" is the one that matters: ThermalLog is
        // constructed once in MainWindow's constructor, so a freshly enabled setting is not
        // writing anything yet. Showing ACTIVE would send someone looking for a file that
        // does not exist, and turning it back off returns to plain OFF, not to pending.
        string label = !wanted ? "OFF"
            : _thermalLoggingAtLoad ? "LOGGING"
            : "ON NEXT START";

        bool live = wanted && _thermalLoggingAtLoad;
        SetChip(LoggingChip, LoggingChipText, live, label);

        // Pending reads amber: enabled, but not doing anything yet.
        if (wanted && !_thermalLoggingAtLoad)
        {
            LoggingChipText.Foreground = (Brush)FindResource("WarnBrush");
            LoggingChip.BorderBrush = (Brush)FindResource("WarnBrush");
        }
    }

    /// <summary>
    /// Paints an active-state chip. State is passed in rather than read from a control, so the
    /// indicator reflects what is actually true rather than what a switch is showing.
    /// </summary>
    private void SetChip(Border chip, TextBlock text, bool active, string activeLabel = "ACTIVE")
    {
        text.Text = active ? activeLabel : (activeLabel == "ACTIVE" ? "OFF" : activeLabel);
        text.Foreground = (Brush)FindResource(active ? "AccentBrush" : "TextFaintBrush");
        chip.BorderBrush = (Brush)FindResource(active ? "AccentBrush" : "BorderBrush");
        chip.Background = (Brush)FindResource(active ? "AccentSoftBrush" : "PanelAltBrush");
    }

    private void OpenLogsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Created here rather than assumed to exist: the folder only appears once
            // logging has actually run, and opening a missing path just fails silently.
            System.IO.Directory.CreateDirectory(OmniHub.Core.Fan.ThermalLog.LogDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = OmniHub.Core.Fan.ThermalLog.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Could not open the log folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
