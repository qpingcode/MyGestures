namespace MyGestures.Models;

public static class AppearanceTheme
{
    public const string Light = "light";
    public const string Dark = "dark";

    public static bool IsKnown(string theme)
        => theme == Light || theme == Dark;

    public static string Normalize(string? theme)
        => IsKnown(theme ?? "") ? theme! : Dark;
}
