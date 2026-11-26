using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ShrinkRay.Theme.Tests;

/// <summary>
/// The switch that turns on replacing originals must read on whenever it is on, however it got
/// drawn.
/// </summary>
[Collection(nameof(ThemeManagerCollection))]
public sealed class ToggleSwitchTests
{
    private const double OnOffset = 18;

    [Theory]
    [InlineData(Chassis.Beige)]
    [InlineData(Chassis.Graphite)]
    public void A_switch_drawn_before_its_clock_runs_shows_its_state(Chassis chassis) => Sta(() =>
    {
        var (on, off) = (Switch(chassis, true), Switch(chassis, false));

        Assert.Equal(OnOffset, KnobLeft(on) - KnobLeft(off), 1);
    });

    [Fact]
    public void A_switch_keeps_its_knob_through_a_chassis_change() => Sta(() =>
    {
        var theme = new ShrinkRayTheme();
        var toggle = Switch(theme, true);
        var off = KnobLeft(Switch(theme, false));
        Wait(TimeSpan.FromMilliseconds(300));
        ThemeManager.ApplyTo(theme, Phosphor.Amber, Chassis.Graphite);
        Wait(TimeSpan.FromMilliseconds(50));

        Assert.Equal(OnOffset, KnobLeft(toggle) - off, 1);

        toggle.IsChecked = false;
        Wait(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0, KnobLeft(toggle) - off, 1);

        toggle.IsChecked = true;
        Wait(TimeSpan.FromMilliseconds(300));
        Assert.Equal(OnOffset, KnobLeft(toggle) - off, 1);
    });

    private static ToggleButton Switch(Chassis chassis, bool isChecked)
    {
        var theme = new ShrinkRayTheme();
        ThemeManager.ApplyTo(theme, Phosphor.Amber, chassis);
        return Switch(theme, isChecked);
    }

    private static ToggleButton Switch(ResourceDictionary theme, bool isChecked)
    {
        var host = new Grid { Resources = theme };
        var toggle = new ToggleButton { IsChecked = isChecked, Content = "Replace after checks pass" };
        host.Children.Add(toggle);
        toggle.Style = (Style)theme["SR.ToggleSwitch"];
        host.Measure(new Size(400, 60));
        host.Arrange(new Rect(0, 0, 400, 60));
        host.UpdateLayout();

        // Draws the switch the way an off-screen render does.
        new RenderTargetBitmap(400, 60, 96, 96, PixelFormats.Pbgra32).Render(host);
        return toggle;
    }

    private static double KnobLeft(ToggleButton toggle)
    {
        var knob = Find<Ellipse>(toggle)!;
        return knob.TransformToAncestor(toggle).Transform(new Point(0, 0)).X;
    }

    private static T? Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = child as T ?? Find<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void Wait(TimeSpan time)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = time };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Registers the pack:// scheme without starting an Application.
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
