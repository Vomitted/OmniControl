using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using UserControl = System.Windows.Controls.UserControl;
namespace OmniHub.App.Wpf.Controls;

public partial class StatTile : UserControl
{
    private string _lastValue = "";

    public StatTile()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value.ToUpperInvariant();
    }

    public Brush TitleAccent
    {
        get => TitleText.Foreground;
        set => TitleText.Foreground = value;
    }

    public void SetValue(string value, string sub = "")
    {
        SubText.Text = sub;
        if (value == _lastValue)
        {
            ValueText.Text = value;
            return;
        }
        _lastValue = value;
        ValueText.Text = value;

        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromPercent(0), new CubicEase()));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pulse.Duration = TimeSpan.FromMilliseconds(260);
        ValueScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ValueScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }
}
