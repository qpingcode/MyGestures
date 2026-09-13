using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyGestures.Localization;
using MyGestures.Models;

namespace MyGestures.Views;

public sealed class InputActionCaptureWindow : Window
{
    private const string ControlKeyToken = "Ctrl";
    private const string WindowsKeyToken = "Win";
    private readonly TextBlock valueText;
    private readonly LocalizationService localization;
    public CapturedAction? Result { get; private set; }

    public InputActionCaptureWindow(LocalizationService localization, CapturedAction? current)
    {
        this.localization = localization;
        Title = localization.GetCaption("Gestures.Capture.Title", "Choose action");
        Width = 460;
        Height = 240;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Result = current;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = localization.GetCaption("Gestures.Capture.Hint", "Press a keyboard shortcut or click a mouse button in the box."), TextWrapping = TextWrapping.Wrap });
        valueText = new TextBlock { Text = current?.HotKey ?? FormatMouseButton(current?.MouseButton), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var input = new Border { Child = valueText, Height = 64, Margin = new Thickness(0, 12, 0, 12), Background = System.Windows.Media.Brushes.LightGray, Focusable = true };
        panel.Children.Add(input);
        input.PreviewMouseDown += (_, e) =>
        {
            Result = new CapturedAction { Kind = GestureConfig.MouseActionType, MouseButton = e.ChangedButton.ToString() };
            valueText.Text = FormatMouseButton(Result.MouseButton);
            e.Handled = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = localization.GetCaption("Gestures.Capture.Confirm", "Confirm"), Padding = new Thickness(12, 6, 12, 6) };
        var cancel = new Button { Content = localization.GetCaption("Gestures.Capture.Cancel", "Cancel"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(12, 0, 0, 0), IsCancel = true };
        save.Click += (_, _) => { if (Result != null) DialogResult = true; };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => input.Focus();
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { DialogResult = false; return; }
            e.Handled = true;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            var parts = new List<string>();
            var modifiers = Keyboard.Modifiers;
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add(ControlKeyToken);
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add(nameof(ModifierKeys.Alt));
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add(nameof(ModifierKeys.Shift));
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add(WindowsKeyToken);
            parts.Add(key.ToString());
            Result = new CapturedAction { HotKey = string.Join("+", parts) };
            valueText.Text = Result.HotKey;
        };
    }

    private string FormatMouseButton(string? button) => button switch
    {
        nameof(MouseButton.Left) => localization.GetCaption("Gestures.Capture.MouseLeft", "Left button"),
        nameof(MouseButton.Right) => localization.GetCaption("Gestures.Capture.MouseRight", "Right button"),
        nameof(MouseButton.Middle) => localization.GetCaption("Gestures.Capture.MouseMiddle", "Middle button"),
        nameof(MouseButton.XButton1) => localization.GetCaption("Gestures.Capture.MouseBack", "Back button"),
        nameof(MouseButton.XButton2) => localization.GetCaption("Gestures.Capture.MouseForward", "Forward button"),
        _ => ""
    };

    public sealed class CapturedAction
    {
        public string Kind { get; set; } = GestureConfig.HotKeyActionType;
        public string? HotKey { get; set; }
        public string? MouseButton { get; set; }
    }
}
