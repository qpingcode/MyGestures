using System.Windows.Input;

namespace MyGestures.Utils;

public static class KeyboardHelper
{
    private const int KeyPressDelayMilliseconds = 10;
    private const int KeyUpFlag = 0x0002;

    public static void SimulateKeyPress(Key key) => SimulateKeyPress(ModifierKeys.None, key);

    public static void SimulateKeyPress(ModifierKeys modifiers, Key key)
    {
        PressModifiers(modifiers);
        try
        {
            Thread.Sleep(KeyPressDelayMilliseconds);
            PressKey(key);
            Thread.Sleep(KeyPressDelayMilliseconds);
            PressKey(key, release: true);
        }
        finally
        {
            Thread.Sleep(KeyPressDelayMilliseconds);
            PressModifiers(modifiers, release: true);
        }
    }

    private static void PressKey(Key key, bool release = false)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0) throw new ArgumentException("Key has no virtual key code.", nameof(key));
        Native.keybd_event(checked((byte)virtualKey), 0, release ? KeyUpFlag : 0, 0);
    }

    private static void PressModifiers(ModifierKeys modifiers, bool release = false)
    {
        if (modifiers.HasFlag(ModifierKeys.Control)) PressKey(Key.LeftCtrl, release);
        if (modifiers.HasFlag(ModifierKeys.Shift)) PressKey(Key.LeftShift, release);
        if (modifiers.HasFlag(ModifierKeys.Alt)) PressKey(Key.LeftAlt, release);
        if (modifiers.HasFlag(ModifierKeys.Windows)) PressKey(Key.LWin, release);
    }
}
