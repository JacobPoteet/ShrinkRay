using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ShrinkRay.App.Views;

/// <summary>Setup's cards. Everything they show comes from the window's view model.</summary>
public partial class SetupCards : UserControl
{
    public SetupCards()
    {
        InitializeComponent();

        // Each card hands the keyboard to the key that moves on. The originals card keeps it
        // itself, and the first-episode card gives it to the key that starts nothing.
        foreach (var target in new UIElement[]
        {
            CheckAgainKey, ChooseLibraryKey, LibraryNextKey, UseScratchKey, OriginalsCard, DoneKey,
        })
        {
            target.IsVisibleChanged += (_, _) =>
            {
                if (target.IsVisible)
                {
                    Dispatcher.BeginInvoke(() => target.Focus(), DispatcherPriority.Input);
                }
            };
        }
    }
}
