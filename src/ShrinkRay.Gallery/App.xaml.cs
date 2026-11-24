using System.Windows;
using ShrinkRay.Theme;

namespace ShrinkRay.Gallery;

/// <summary>
/// The theme gallery. Options for screenshots:
/// <c>--phosphor Amber|Green|Ice</c>, <c>--chassis Beige|Graphite</c>, <c>--still</c> (reduced motion).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var phosphor = Phosphor.Amber;
        var chassis = Chassis.Beige;
        for (var i = 0; i < e.Args.Length; i++)
        {
            var next = i + 1 < e.Args.Length ? e.Args[i + 1] : string.Empty;
            switch (e.Args[i].ToLowerInvariant())
            {
                case "--phosphor" when Enum.TryParse(next, ignoreCase: true, out Phosphor p):
                    phosphor = p;
                    i++;
                    break;
                case "--chassis" when Enum.TryParse(next, ignoreCase: true, out Chassis c):
                    chassis = c;
                    i++;
                    break;
                case "--still":
                    MotionPolicy.Override = false;
                    break;
            }
        }

        ThemeManager.Apply(phosphor, chassis);
        new MainWindow().Show();
    }
}
