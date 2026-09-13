using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MyGestures.Localization;
using MyGestures.Models;
using MyGestures.Utils;

namespace MyGestures.Views;

internal sealed class GestureRecordHintWindow : Window
{
    private const double HorizontalMargin = 24;
    private const double VerticalMargin = 16;
    private const double BannerOffsetFromTop = 28;
    private readonly LocalizationService localization;
    private readonly TextBlock message;
    private readonly TextBlock liveGesture;
    private EscapeKeyListener? escapeListener;
    public event Action? Cancelled;

    public GestureRecordHintWindow(LocalizationService localization, string theme)
    {
        this.localization = localization;
        var dark = AppearanceTheme.Normalize(theme) != AppearanceTheme.Light;
        Title = localization.GetCaption("Gestures.Record.Title", "Record gesture");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        message = new TextBlock
        {
            Text = localization.GetCaption("Gestures.Record.Banner", "Click the target app, then hold the right mouse button and draw. Release to finish. Esc cancels."),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            FontSize = 14,
            Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xF3, 0xF1, 0xEC) : Color.FromRgb(0x1C, 0x1B, 0x19)),
        };
        var cancel = new Button
        {
            Content = localization.GetCaption("Gestures.Record.Cancel", "Cancel"),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(16, 0, 0, 0),
            MinWidth = 88,
        };
        cancel.Click += (_, _) => Cancelled?.Invoke();
        liveGesture = new TextBlock
        {
            Text = string.Empty,
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0),
            Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xC4, 0xA5, 0x74) : Color.FromRgb(0x8A, 0x6A, 0x3C)),
            Visibility = Visibility.Collapsed,
        };
        var copy = new StackPanel();
        copy.Children.Add(message);
        copy.Children.Add(liveGesture);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(copy);
        row.Children.Add(cancel);
        Content = new Border
        {
            Child = row,
            Padding = new Thickness(HorizontalMargin, VerticalMargin, HorizontalMargin, VerticalMargin),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(dark ? Color.FromRgb(0x1D, 0x1D, 0x1D) : Color.FromRgb(0xFF, 0xFD, 0xF9)),
            BorderBrush = new SolidColorBrush(dark ? Color.FromRgb(0xC4, 0xA5, 0x74) : Color.FromRgb(0x8A, 0x6A, 0x3C)),
            BorderThickness = new Thickness(1),
        };
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            escapeListener?.Dispose();
            escapeListener = null;
        };
    }

    public void SetSelectedProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        var template = localization.GetCaption("Gestures.Record.BannerReady", "Target: {{process}}. Hold the right mouse button and draw. Release to finish. Esc cancels.");
        message.Text = template.Replace("{{process}}", processName, StringComparison.Ordinal);
    }

    public void SetLiveGesture(string directions)
    {
        liveGesture.Text = directions;
        liveGesture.Visibility = string.IsNullOrWhiteSpace(directions) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Top + BannerOffsetFromTop;
        var handle = new WindowInteropHelper(this).Handle;
        var styles = Native.GetWindowLongPtr(handle, Native.ExtendedWindowStyleIndex).ToInt64();
        Native.SetWindowLongPtr(handle, Native.ExtendedWindowStyleIndex, new IntPtr(styles | Native.NoActivateExtendedStyle | Native.ToolWindowExtendedStyle));
        escapeListener = new EscapeKeyListener();
        escapeListener.Escaped += () => Cancelled?.Invoke();
    }

    private sealed class EscapeKeyListener : IDisposable
    {
        private Native.LowLevelKeyboardProc? hookProcedure;
        private IntPtr hookHandle;

        public event Action? Escaped;

        public EscapeKeyListener()
        {
            hookProcedure = OnHook;
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            hookHandle = Native.SetWindowsHookEx(Native.KeyboardLowLevelHookId, hookProcedure, Native.GetModuleHandle(module?.ModuleName ?? ""), 0);
        }

        private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var message = wParam.ToInt32();
                if (message == Native.KeyDownMessage || message == Native.SystemKeyDownMessage)
                {
                    var key = Marshal.ReadInt32(lParam);
                    if (key == Native.EscapeVirtualKey)
                    {
                        Escaped?.Invoke();
                        return new IntPtr(1);
                    }
                }
            }

            return Native.CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (hookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }

            hookProcedure = null;
        }
    }
}
