using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using ShrinkRay.App.ViewModels;

namespace ShrinkRay.App.Views;

/// <summary>The main deck. Everything it shows comes from <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 2;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly double _captionHeight;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        _timer.Tick += (_, _) => viewModel.Tick();
        Loaded += (_, _) => _timer.Start();
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

        // Open no larger than the work area. The deck scales down to fit a smaller window.
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height));

        _captionHeight = WindowChrome.GetWindowChrome(this)?.CaptionHeight ?? 0;
        Scaler.ScaleChanged += (_, _) => ScaleCaption();
    }

    public MainViewModel ViewModel { get; }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && System.IO.Directory.Exists(paths[0]) ? paths[0] : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolder(e) is not null && !ViewModel.IsEncoding ? DragDropEffects.Link : DragDropEffects.None;
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
    }

    // A window without a caption bar maximizes over the whole monitor, taskbar included, and
    // past its edges. Maximize it to the monitor's work area instead.
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            FitMaximizeToWorkArea(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private static void FitMaximizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        limits.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
        limits.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        Marshal.StructureToPtr(limits, lParam, false);
    }

    // The caption drag strip is in window pixels, so it shrinks with the deck's header.
    private void ScaleCaption()
    {
        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.CaptionHeight = _captionHeight * Scaler.Scale;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
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
