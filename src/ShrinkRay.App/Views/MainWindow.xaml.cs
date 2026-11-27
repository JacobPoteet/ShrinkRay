using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ShrinkRay.App.Controls;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Theme;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.Views;

/// <summary>The main deck. Everything it shows comes from <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private static readonly TimeSpan SelfTestTime = TimeSpan.FromSeconds(1.4);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly double _captionHeight;
    private bool _isMoving;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        _timer.Tick += (_, _) => viewModel.Tick();
        Loaded += (_, _) => _timer.Start();
        Loaded += (_, _) => SelfTest();
        // The screens warm up from a line. It only draws, so recovery and the scan carry on.
        Loaded += (_, _) => CrtWarmUp.Play(Chassis);
        Closed += (_, _) => _timer.Stop();
        Drop += OnDrop;
        DragOver += OnDragOver;

        // Closing during a run asks first; the view model decides, and closes the window again
        // once the run has ended.
        Closing += (_, e) => e.Cancel = !viewModel.RequestClose();
        viewModel.CloseReady += (_, _) => Dispatcher.BeginInvoke(Close);
        CloseOverlay.IsVisibleChanged += (_, _) =>
        {
            if (CloseOverlay.IsVisible)
            {
                KeepRunningKey.Focus();
            }
        };

        StopOverlay.IsVisibleChanged += (_, _) =>
        {
            if (StopOverlay.IsVisible)
            {
                StopPrimaryKey.Focus();
            }
        };

        // Each tour stop takes the keyboard as it appears.
        foreach (var mark in new[] { LibraryMark, EncodeMark, SavingsMark })
        {
            mark.IsVisibleChanged += (_, _) =>
            {
                if (mark.IsVisible)
                {
                    Dispatcher.BeginInvoke(mark.FocusNext, DispatcherPriority.Input);
                }
            };
        }

        // The window can land on a screen of another size: moved there, or changed under it.
        LocationChanged += (_, _) => QueueFit();
        StateChanged += (_, _) => QueueFit();

        _captionHeight = WindowChrome.GetWindowChrome(this)?.CaptionHeight ?? 0;
        Scaler.ScaleChanged += (_, _) => ScaleCaption();
    }

    public MainViewModel ViewModel { get; }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && System.IO.Directory.Exists(paths[0]) ? paths[0] : null;

    // Power on: every lamp and segment lights and the needles swing, then each takes up its reading.
    private void SelfTest()
    {
        if (!MotionPolicy.IsMotionEnabled)
        {
            return;
        }

        PowerOn.SetIsTesting(Chassis, true);
        var done = new DispatcherTimer { Interval = SelfTestTime };
        done.Tick += (_, _) =>
        {
            done.Stop();
            PowerOn.SetIsTesting(Chassis, false);
        };
        done.Start();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolder(e) is not null && ViewModel.CanDropFolder ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedFolder(e) is { } folder)
        {
            ViewModel.DropFolder(folder);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        FitToScreen();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        QueueFit();
    }

    // A drag is left alone until it ends, so the window never changes size under the pointer.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEnterSizeMove:
                _isMoving = true;
                break;
            case WmExitSizeMove:
                _isMoving = false;
                QueueFit();
                break;
            case WmDisplayChange:
            case WmSettingChange:
                QueueFit();
                break;
        }

        return IntPtr.Zero;
    }

    // Queued, so Windows and WPF finish what they are doing before the window is measured.
    private void QueueFit() => Dispatcher.BeginInvoke(FitToScreen, DispatcherPriority.Background);

    private void FitToScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _isMoving || WindowState != WindowState.Normal)
        {
            return;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || !GetWindowRect(hwnd, out var window))
        {
            return;
        }

        var design = new Size(Scaler.DesignWidth, Scaler.DesignHeight);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (WindowFit.Refit(window.ToRect(), info.Work.ToRect(), design, dpi) is { } fit)
        {
            SetWindowPos(hwnd, IntPtr.Zero, (int)fit.X, (int)fit.Y, (int)fit.Width, (int)fit.Height, SwpNoZOrder | SwpNoActivate);
        }
    }

    // The caption drag strip is in window pixels, so it shrinks with the deck's header.
    private void ScaleCaption()
    {
        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.CaptionHeight = _captionHeight * Scaler.Scale;
        }
    }

    // A right click opens a show's menu without picking the show, which would load it or browse it.
    private void OnShowRightDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // The latch keys show the view model's state, not their own toggle.
    private void OnLatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton key)
        {
            key.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        }
    }

    private void OnScrimDown(object sender, MouseButtonEventArgs e) => ViewModel.CloseSettingsCommand.Execute(null);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }
}
