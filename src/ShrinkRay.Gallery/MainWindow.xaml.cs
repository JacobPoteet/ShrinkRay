using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ShrinkRay.Theme;
using ShrinkRay.Theme.Controls;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Gallery;

/// <summary>The kit board, plus a live deck that runs a fake encode.</summary>
public partial class MainWindow : Window
{
    // Output size over source size for each CQ detent, from the design's estimates.
    private static readonly double[] Ratios = [0.62, 0.52, 0.45, 0.38, 0.32, 0.27];
    private const double EpisodeSeconds = 1323;
    private const double SourceGigabytes = 1.62;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Random _random = new(265);
    private double _progress = 0.527;
    private int _episode = 3;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        ThemeManager.ThemeChanged += (_, _) => SyncThemeControls();
        MotionPolicy.Changed += (_, _) => Dispatcher.BeginInvoke(SyncMotion);
        Loaded += (_, _) =>
        {
            SyncThemeControls();
            SyncMotion();
            EncodeKey.IsChecked = true;
            SetRunning();
            Render();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private bool IsRunning => EncodeKey.IsChecked == true && PauseKey.IsChecked != true;

    private void Tick()
    {
        if (IsRunning)
        {
            _progress += 0.006;
            if (_progress >= 1)
            {
                _progress = 0;
                _episode = _episode >= 12 ? 1 : _episode + 1;
            }

            BitrateMeter.Value = 1.8 + _random.NextDouble() * 0.9;
            LoadMeter.Value = 78 + _random.NextDouble() * 15;
        }

        Render();
    }

    private void Render()
    {
        var ratio = Ratios[Math.Clamp(QualityKnob.SelectedIndex, 0, Ratios.Length - 1)];
        SourceReel.Pack = ReelMath.SourceFill(_progress);
        OutputReel.Pack = ReelMath.OutputFill(_progress, ratio);

        var seconds = (int)Math.Round(_progress * EpisodeSeconds);
        TapeTime.Text = string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}").PadLeft(5);
        Percent.Text = (_progress * 100).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4);
        Progress.Fraction = _progress;

        SourceText.Text = string.Create(CultureInfo.InvariantCulture, $"Source {SourceGigabytes * (1 - _progress):0.00} GB left");
        OutputText.Text = string.Create(CultureInfo.InvariantCulture, $"Output {SourceGigabytes * ratio * _progress:0.00} GB");
        DeckLabel.Episode = string.Create(CultureInfo.InvariantCulture, $"S01E{_episode:00}");

        if (!IsRunning)
        {
            BitrateMeter.Value = 0;
            LoadMeter.Value = 3;
        }
    }

    private void SetRunning()
    {
        var running = IsRunning;
        SourceReel.IsSpinning = running;
        OutputReel.IsSpinning = running;
        EncodeKey.Lamp = running ? LampState.Red : LampState.Off;
        EncodeKey.LampBlink = running;

        if (running)
        {
            StatusLamp.State = LampState.Red;
            StatusLamp.Blink = true;
            StatusLamp.Content = "ENCODING";
        }
        else if (EncodeKey.IsChecked == true)
        {
            StatusLamp.State = LampState.Active;
            StatusLamp.Blink = false;
            StatusLamp.Content = "PAUSED";
        }
        else
        {
            StatusLamp.State = LampState.Off;
            StatusLamp.Blink = false;
            StatusLamp.Content = "STOPPED";
        }

        Render();
    }

    private void OnEncodeClick(object sender, RoutedEventArgs e)
    {
        // Encode latches; pressing it again does not release it. Stop does.
        EncodeKey.IsChecked = true;
        PauseKey.IsChecked = false;
        SetRunning();
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (EncodeKey.IsChecked != true)
        {
            PauseKey.IsChecked = false;
        }

        SetRunning();
    }

    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        EncodeKey.IsChecked = false;
        PauseKey.IsChecked = false;
        _progress = 0;
        SetRunning();
    }

    private void OnPhosphorChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out Phosphor phosphor)
            && phosphor != ThemeManager.Phosphor)
        {
            ThemeManager.SetPhosphor(phosphor);
        }
    }

    private void OnChassisChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out Chassis chassis)
            && chassis != ThemeManager.Chassis)
        {
            ThemeManager.SetChassis(chassis);
        }
    }

    private void OnMotionClick(object sender, RoutedEventArgs e) =>
        MotionPolicy.Override = MotionSwitch.IsChecked == true ? null : false;

    private void SyncMotion()
    {
        MotionSwitch.IsChecked = MotionPolicy.IsMotionEnabled;
        ReelLamp.State = MotionPolicy.IsMotionEnabled ? LampState.Ok : LampState.Off;
        ReelLamp.Content = MotionPolicy.IsMotionEnabled ? "MOTION ON" : "MOTION HELD";
    }

    private void SyncThemeControls()
    {
        PhosphorAmber.IsChecked = ThemeManager.Phosphor == Phosphor.Amber;
        PhosphorGreen.IsChecked = ThemeManager.Phosphor == Phosphor.Green;
        PhosphorIce.IsChecked = ThemeManager.Phosphor == Phosphor.Ice;
        ChassisBeige.IsChecked = ThemeManager.Chassis == Chassis.Beige;
        ChassisGraphite.IsChecked = ThemeManager.Chassis == Chassis.Graphite;
        Materials.ItemsSource = Swatch.Board(ThemeManager.Phosphor, ThemeManager.Chassis);
    }
}

/// <summary>How a material swatch is finished on the board.</summary>
public enum SwatchFinish
{
    Raised,
    Molded,
    Recessed,
    Lit,
}

/// <summary>One material on the kit board.</summary>
public sealed record Swatch(string Name, string Hex, string Role, Brush Fill, SwatchFinish Finish,
    double InsetBlur = 0, double InsetOffset = 0, Brush? GlowFill = null)
{
    public static IReadOnlyList<Swatch> Board(Phosphor phosphor, Chassis chassis)
    {
        var c = ChassisPalette.For(chassis);
        var p = PhosphorPalette.For(phosphor);
        var chassisName = chassis == Chassis.Beige ? "Beige ABS" : "Graphite ABS";
        var phosphorName = "Phosphor " + phosphor.ToString().ToLowerInvariant();
        var lampGreen = Color.FromRgb(0xA6, 0xD6, 0x51);
        return
        [
            new(chassisName, ToHex(c.Abs), "chassis", Gradient(c.AbsHi, c.Abs), SwatchFinish.Molded),
            new("ABS shadow", ToHex(c.AbsLo), "key sides", Solid(c.AbsLo), SwatchFinish.Raised),
            new("Anodized charcoal", "#262421", "recessed plates", Solid(Color.FromRgb(0x26, 0x24, 0x21)), SwatchFinish.Recessed, 8, 2),
            new("CRT black", "#0D0A05", "tube glass", Solid(Color.FromRgb(0x0D, 0x0A, 0x05)), SwatchFinish.Recessed, 20, 0),
            new(phosphorName, ToHex(p.On), "the only light", Solid(p.On), SwatchFinish.Lit, GlowFill: Solid(p.Glow)),
            new("Signal red", "#B8381F", "encode, delete",
                Gradient(Color.FromRgb(0xE3, 0x60, 0x3F), Color.FromRgb(0xB8, 0x38, 0x1F)), SwatchFinish.Raised),
            new("Tape oxide", "#3A281A", "reel packs", Solid(Color.FromRgb(0x3A, 0x28, 0x1A)), SwatchFinish.Raised),
            new("J-card cream", "#F1E9D6", "labels, track list", Solid(Color.FromRgb(0xF1, 0xE9, 0xD6)), SwatchFinish.Raised),
            new("Marker blue", "#1F3B73", "hand-written", Solid(Color.FromRgb(0x1F, 0x3B, 0x73)), SwatchFinish.Raised),
            new("Lamp green", "#A6D651", "checks passed", Solid(lampGreen), SwatchFinish.Lit,
                GlowFill: Solid(Color.FromArgb(0x80, lampGreen.R, lampGreen.G, lampGreen.B))),
        ];
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Brush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush Gradient(Color top, Color bottom)
    {
        var b = new LinearGradientBrush(top, bottom, 90);
        b.Freeze();
        return b;
    }
}
