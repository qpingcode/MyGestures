using System.Globalization;
using System.Resources;

namespace MyGestures.Localization;

public sealed class LocalizationService
{
    private static readonly ResourceManager Resources = new("MyGestures.Localization.HostStrings", typeof(LocalizationService).Assembly);
    public static readonly string[] SupportedLocales = { "en-US", "zh-CN", "fr-FR" };
    public event EventHandler? LocaleChanged;
    public string Locale { get; private set; } = SupportedLocales[0];

    public void SetLocale(string locale)
    {
        var resolvedLocale = SupportedLocales.FirstOrDefault(value => value.Equals(locale, StringComparison.OrdinalIgnoreCase))
            ?? SupportedLocales.FirstOrDefault(value => value.StartsWith(locale.Split('-')[0] + "-", StringComparison.OrdinalIgnoreCase))
            ?? SupportedLocales[0];
        if (Locale == resolvedLocale) return;
        Locale = resolvedLocale;
        LocaleChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetCaption(string key, string defaultValue) => Resources.GetString(key, CultureInfo.GetCultureInfo(Locale)) ?? defaultValue;
}
