using Microsoft.Win32;
using MyGestures.Services;
using NUnit.Framework;

namespace MyGestures.Test;

[TestFixture]
public sealed class AutoStartServiceTests
{
    private string keyPath = null!;
    private string valueName = null!;

    [SetUp]
    public void SetUp()
    {
        keyPath = @"Software\MyGestures.Test\AutoStart";
        valueName = Guid.NewGuid().ToString("N");
    }

    [TearDown]
    public void TearDown()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\MyGestures.Test", throwOnMissingSubKey: false);
    }

    [Test]
    public void Apply_WritesAndRemovesRunValue()
    {
        var service = new AutoStartService(keyPath, valueName);
        service.Apply(true);
        Assert.That(service.IsEnabled(), Is.True);
        using (var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false))
        {
            var command = key?.GetValue(valueName) as string;
            Assert.That(command, Does.Contain(AutoStartService.BackgroundStartArgument));
        }
        service.Apply(false);
        Assert.That(service.IsEnabled(), Is.False);
    }

    [Test]
    public void GetCommand_StartsInBackground()
    {
        Assert.That(AutoStartService.GetCommand(), Does.Contain(AutoStartService.BackgroundStartArgument));
    }
}
