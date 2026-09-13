using System.IO;
using System.Text.Json;
using MyGestures.Localization;
using MyGestures.Models;
using MyGestures.Utils;

namespace MyGestures.Services;

/// <summary>Owns local configuration. Legacy MyTools files are read only on the first run.</summary>
public sealed class GestureSettingsStore
{
    public const string ApplicationDirectoryName = "MyGestures";
    public const string ConfigurationFileName = "Configuration.json";
    public const string LegacyApplicationDirectoryName = "MyTools.Desktop";
    public const string LegacyGesturesFileName = "Gestures.json";
    public const string LegacySettingsFileName = "Settings.json";
    private const string LegacyEnableSettingKey = "Gestures.EnableGesture";
    private const string TemporaryFileSuffix = ".tmp";
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationDirectoryName);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static readonly HashSet<string> DirectionNames = new(Enum.GetNames<MoveDirection>(), StringComparer.Ordinal);
    private static readonly HashSet<string> MouseButtonNames = new(Enum.GetNames<System.Windows.Input.MouseButton>(), StringComparer.Ordinal);
    private readonly string directory;
    private readonly string legacyDirectory;
    private readonly LocalizationService localization;
    public GestureSettings Current { get; private set; }

    public GestureSettingsStore(LocalizationService localization, string? directory = null, string? legacyDirectory = null)
    {
        this.localization = localization;
        this.directory = directory ?? DataDirectory;
        this.legacyDirectory = legacyDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyApplicationDirectoryName);
        var path = Path.Combine(this.directory, ConfigurationFileName);
        // A damaged existing configuration must never be overwritten with defaults.
        Current = File.Exists(path)
            ? JsonSerializer.Deserialize<GestureSettings>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Configuration is empty.")
            : ImportOrCreate();
        localization.SetLocale(Current.Locale);
        Validate(Current);
        if (!File.Exists(path)) Save(Current);
    }

    public void Save(GestureSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ConfigurationFileName);
        var temporaryPath = path + TemporaryFileSuffix;
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
        Current = settings;
        localization.SetLocale(settings.Locale);
    }

    private GestureSettings ImportOrCreate()
    {
        var settings = new GestureSettings();
        localization.SetLocale(settings.Locale);
        var gesturesPath = Path.Combine(legacyDirectory, LegacyGesturesFileName);
        settings.Gestures = File.Exists(gesturesPath)
            ? JsonSerializer.Deserialize<List<GestureConfig>>(File.ReadAllText(gesturesPath), JsonOptions) ?? throw new InvalidDataException("Legacy gestures are empty.")
            : GestureDefaults.Create(localization);
        var settingsPath = Path.Combine(legacyDirectory, LegacySettingsFileName);
        if (File.Exists(settingsPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("Name", out var name) && name.GetString() == LegacyEnableSettingKey
                    && entry.TryGetProperty("Value", out var value))
                    settings.Enabled = bool.TryParse(value.ToString(), out var enabled) && enabled;
            }
        }
        return settings;
    }

    public static void Validate(GestureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Gestures);
        ArgumentNullException.ThrowIfNull(settings.Locale);
        if (!LocalizationService.SupportedLocales.Contains(settings.Locale))
        {
            // Normalize OS locales on first use; only supported locales are persisted.
            var language = new LocalizationService();
            language.SetLocale(settings.Locale);
            settings.Locale = language.Locale;
        }
        foreach (var gesture in settings.Gestures)
        {
            ArgumentNullException.ThrowIfNull(gesture);
            ArgumentNullException.ThrowIfNull(gesture.Directions);
            ArgumentNullException.ThrowIfNull(gesture.ProcessNames);
            if (gesture.Directions.Any(direction => !DirectionNames.Contains(direction)))
                throw new InvalidDataException("Unknown gesture direction.");
            if (gesture.ActionType != GestureConfig.HotKeyActionType && gesture.ActionType != GestureConfig.MouseActionType)
                throw new InvalidDataException("Unknown gesture action type.");
            if (gesture.ActionType == GestureConfig.MouseActionType && !string.IsNullOrEmpty(gesture.MouseButton)
                && !MouseButtonNames.Contains(gesture.MouseButton))
                throw new InvalidDataException("Unknown mouse button.");
            if (gesture.ActionType == GestureConfig.HotKeyActionType && !string.IsNullOrEmpty(gesture.HotKey)
                && new HotKeyConfig(gesture.HotKey).Key == System.Windows.Input.Key.None)
                throw new InvalidDataException("Unknown keyboard shortcut.");
            if (string.IsNullOrWhiteSpace(gesture.Id)) gesture.Id = Guid.NewGuid().ToString("N");
            gesture.ProcessNames = gesture.ProcessNames.Select(name => name.Trim().ToLowerInvariant()).Where(name => name.Length > 0).Distinct().ToList();
        }
    }
}
