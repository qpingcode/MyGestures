using System.Text.Json;
using MyGestures.Localization;
using MyGestures.Models;
using MyGestures.Services;
using MyGestures.Utils;
using NUnit.Framework;

namespace MyGestures.Test;

[TestFixture]
public sealed class GestureSettingsStoreTests
{
    private string root = null!;
    private string own = null!;
    private string legacy = null!;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), nameof(GestureSettingsStoreTests), Guid.NewGuid().ToString("N"));
        own = Path.Combine(root, "own");
        legacy = Path.Combine(root, "legacy");
        Directory.CreateDirectory(legacy);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, recursive: true);

    [Test]
    public void FirstRun_ImportsGesturesAndEnabledState_WithoutChangingLegacyFiles()
    {
        var gesture = new GestureConfig { Id = "existing", ActionName = "My action", Directions = [nameof(MoveDirection.Left)], HotKey = "Ctrl+W", ProcessNames = ["Chrome"] };
        var gestureJson = JsonSerializer.Serialize(new[] { gesture }, JsonOptions);
        var settingsJson = "[{\"Name\":\"Gestures.EnableGesture\",\"Value\":\"True\"}]";
        var gesturePath = Path.Combine(legacy, GestureSettingsStore.LegacyGesturesFileName);
        var settingsPath = Path.Combine(legacy, GestureSettingsStore.LegacySettingsFileName);
        File.WriteAllText(gesturePath, gestureJson);
        File.WriteAllText(settingsPath, settingsJson);
        var store = CreateStore();
        Assert.Multiple(() =>
        {
            Assert.That(store.Current.Enabled, Is.True);
            Assert.That(store.Current.Gestures.Single().Id, Is.EqualTo(gesture.Id));
            Assert.That(store.Current.Gestures.Single().ProcessNames, Is.EqualTo(new[] { "chrome" }));
            Assert.That(File.ReadAllText(gesturePath), Is.EqualTo(gestureJson));
            Assert.That(File.ReadAllText(settingsPath), Is.EqualTo(settingsJson));
        });
    }

    [Test]
    public void LaterRuns_UseOwnConfiguration_AndIgnoreLegacyChanges()
    {
        var store = CreateStore();
        store.Save(new GestureSettings { Enabled = true, Locale = "fr-FR", Gestures = [] });
        File.WriteAllText(Path.Combine(legacy, GestureSettingsStore.LegacyGesturesFileName), "invalid legacy json");
        var loaded = CreateStore();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Current.Enabled, Is.True);
            Assert.That(loaded.Current.Locale, Is.EqualTo("fr-FR"));
            Assert.That(loaded.Current.Gestures, Is.Empty);
        });
    }

    [TestCase("invalid")]
    [TestCase("1")]
    public void InvalidSave_DoesNotOverwritePreviouslySavedConfiguration(string invalidDirection)
    {
        var store = CreateStore();
        var path = Path.Combine(own, GestureSettingsStore.ConfigurationFileName);
        var previous = File.ReadAllText(path);
        Assert.Throws<InvalidDataException>(() => store.Save(new GestureSettings { Gestures = [new GestureConfig { Directions = [invalidDirection] }] }));
        Assert.That(File.ReadAllText(path), Is.EqualTo(previous));
    }

    [Test]
    public void DamagedExistingConfiguration_IsPreserved()
    {
        Directory.CreateDirectory(own);
        var path = Path.Combine(own, GestureSettingsStore.ConfigurationFileName);
        const string damagedJson = "damaged config";
        File.WriteAllText(path, damagedJson);
        Assert.Throws<JsonException>(() => CreateStore());
        Assert.That(File.ReadAllText(path), Is.EqualTo(damagedJson));
    }

    private GestureSettingsStore CreateStore() => new(new LocalizationService(), own, legacy);
}
