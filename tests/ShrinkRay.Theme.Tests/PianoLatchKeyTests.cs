using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.Theme.Tests;

public sealed class PianoLatchKeyTests
{
    [Fact]
    public void Toggle_runs_the_command_and_leaves_the_binding_in_charge() => Sta(() =>
    {
        var model = new Model();
        var command = new CountingCommand();
        var key = new PianoLatchKey { Command = command };
        BindingOperations.SetBinding(key, ToggleButton.IsCheckedProperty,
            new Binding(nameof(Model.Latched)) { Source = model, Mode = BindingMode.OneWay });
        var clicks = 0;
        key.Click += (_, _) => clicks++;

        GetToggle(key).Toggle();

        Assert.Equal(1, command.Runs);
        Assert.Equal(1, clicks);
        Assert.False(key.IsChecked);
        Assert.NotNull(BindingOperations.GetBindingExpression(key, ToggleButton.IsCheckedProperty));

        model.Latched = true;
        Assert.True(key.IsChecked);
    });

    [Fact]
    public void Toggle_on_a_disabled_key_throws() => Sta(() =>
    {
        var command = new CountingCommand();
        var key = new PianoLatchKey { Command = command, IsEnabled = false };

        Assert.Throws<ElementNotEnabledException>(() => GetToggle(key).Toggle());
        Assert.Equal(0, command.Runs);
    });

    private static IToggleProvider GetToggle(PianoLatchKey key) =>
        (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(key).GetPattern(PatternInterface.Toggle);

    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
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

    private sealed class Model : DependencyObject
    {
        public static readonly DependencyProperty LatchedProperty =
            DependencyProperty.Register(nameof(Latched), typeof(bool), typeof(Model));

        public bool Latched
        {
            get => (bool)GetValue(LatchedProperty);
            set => SetValue(LatchedProperty, value);
        }
    }

    private sealed class CountingCommand : ICommand
    {
        public int Runs { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => Runs++;
    }
}
