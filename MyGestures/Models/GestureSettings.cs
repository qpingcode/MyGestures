using System.Globalization;

namespace MyGestures.Models;

public sealed class GestureSettings
{
    public bool Enabled { get; set; }
    public string Locale { get; set; } = CultureInfo.CurrentUICulture.Name;
    public List<GestureConfig> Gestures { get; set; } = new();
}
