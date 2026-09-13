using System.Globalization;

namespace MyGestures.Models;

public sealed class GestureSettings
{
    public bool Enabled { get; set; }
    public bool AutoStart { get; set; }
    public bool GameMode { get; set; } = true;
    public string Locale { get; set; } = CultureInfo.CurrentUICulture.Name;
    public string Theme { get; set; } = AppearanceTheme.Dark;
    public List<GestureConfig> Gestures { get; set; } = new();
}
