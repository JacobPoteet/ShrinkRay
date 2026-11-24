using System.Windows;

namespace ShrinkRay.Theme;

/// <summary>
/// The one dictionary an app merges: tokens, fonts, panel styles and the starting palette.
/// <code>&lt;sr:ShrinkRayTheme Phosphor="Amber" Chassis="Beige" /&gt;</code>
/// Switch later with <see cref="ThemeManager"/>.
/// </summary>
public sealed class ShrinkRayTheme : ResourceDictionary
{
    private Phosphor _phosphor = Phosphor.Amber;
    private Chassis _chassis = Chassis.Beige;

    public ShrinkRayTheme()
    {
        MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ShrinkRay.Theme;component/Themes/Theme.xaml", UriKind.Absolute),
        });
        ThemeManager.ApplyTo(this, _phosphor, _chassis);
    }

    /// <summary>The starting phosphor.</summary>
    public Phosphor Phosphor
    {
        get => _phosphor;
        set
        {
            _phosphor = value;
            ThemeManager.ApplyTo(this, _phosphor, _chassis);
        }
    }

    /// <summary>The starting chassis.</summary>
    public Chassis Chassis
    {
        get => _chassis;
        set
        {
            _chassis = value;
            ThemeManager.ApplyTo(this, _phosphor, _chassis);
        }
    }
}
