using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace MyGestures.Services;

public sealed class AutoStartService
{
    public const string BackgroundStartArgument = "--background";
    public const string ApplicationName = "MyGestures";
    public const string DefaultRunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private readonly string runKeyPath;
    private readonly string valueName;

    public AutoStartService(string? runKeyPath = null, string? valueName = null)
    {
        this.runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        this.valueName = valueName ?? ApplicationName;
    }

    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true)
                ?? throw new InvalidOperationException("The automatic startup registry key could not be opened.");
            if (enabled) key.SetValue(valueName, GetCommand());
            else key.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException("Automatic startup could not be updated.", exception);
        }
    }

    internal bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false);
        return key?.GetValue(valueName) != null;
    }

    internal static string GetCommand()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Assembly.GetExecutingAssembly().Location;
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                path = Path.ChangeExtension(path, ".exe");
        }
        return $"\"{path}\" {BackgroundStartArgument}";
    }
}
