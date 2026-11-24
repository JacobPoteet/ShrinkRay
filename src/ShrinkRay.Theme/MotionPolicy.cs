using System.ComponentModel;
using System.Windows;

namespace ShrinkRay.Theme;

/// <summary>
/// Decides whether decorative motion runs: reel spin, lamp blink and CRT flicker.
/// Follows the Windows "Show animations" setting (<see cref="SystemParameters.ClientAreaAnimation"/>).
/// Keys and needles still move, since they report state.
/// </summary>
public static class MotionPolicy
{
    private static bool? _override;

    static MotionPolicy()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    /// <summary>Raised when <see cref="IsMotionEnabled"/> may have changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>Forces motion on or off. Null follows the system setting.</summary>
    public static bool? Override
    {
        get => _override;
        set
        {
            _override = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool IsMotionEnabled => _override ?? SystemParameters.ClientAreaAnimation;

    private static void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && _override is null)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
