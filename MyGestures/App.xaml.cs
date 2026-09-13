using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using MyGestures.Localization;
using MyGestures.Services;
using MyGestures.Utils;
using MyGestures.Views;
using Forms = System.Windows.Forms;

namespace MyGestures;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\MyGestures.Application";
    private static readonly Uri ApplicationIconUri = new("pack://application:,,,/MyGestures;component/Assets/mygestures.ico");
    private Mutex? instanceMutex;
    private bool ownsMutex;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private GestureRegistry? gestures;
    private SettingsWindow? settingsWindow;
    private readonly LocalizationService localization = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instanceMutex = new Mutex(true, SingleInstanceMutexName, out ownsMutex);
        if (!ownsMutex) { Shutdown(); return; }
        try
        {
            var store = new GestureSettingsStore(localization);
            var autoStart = new AutoStartService();
            TryApplyAutoStart(autoStart, store.Current.AutoStart);
            var mouse = new MouseHelper();
            var detector = new MouseGestureDetector(mouse, NullLogger<MouseGestureDetector>.Instance, NullLogger<MouseTrailWindow>.Instance, localization);
            gestures = new GestureRegistry(NullLogger<GestureRegistry>.Instance, detector) { SuppressWhenFullscreen = store.Current.GameMode };
            settingsWindow = new SettingsWindow(store, gestures, mouse, localization, autoStart);
            MainWindow = settingsWindow;
            if (store.Current.Enabled) gestures.EnableDetection(store.Current.Gestures, mouse);
            var iconResource = GetResourceStream(ApplicationIconUri) ?? throw new FileNotFoundException("Application icon resource is missing.");
            using (var iconStream = iconResource.Stream)
            using (var icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize))
            {
                trayIcon = (System.Drawing.Icon)icon.Clone();
            }
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "MyGestures", Visible = true };
            tray.DoubleClick += (_, _) => ShowSettings();
            localization.LocaleChanged += (_, _) => UpdateTrayMenu();
            UpdateTrayMenu();
            if (!e.Args.Contains(AutoStartService.BackgroundStartArgument)) ShowSettings();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("MyGestures startup failed: {0}", exception);
            MessageBox.Show(localization.GetCaption("Gestures.Error.Startup", "MyGestures could not start. Check its configuration and WebView2 installation."), "MyGestures", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void TryApplyAutoStart(AutoStartService autoStart, bool enabled)
    {
        try { autoStart.Apply(enabled); }
        catch (Exception exception) { System.Diagnostics.Trace.TraceWarning("Automatic startup could not be applied: {0}", exception); }
    }

    private void ShowSettings()
    {
        settingsWindow?.Show();
        if (settingsWindow != null) settingsWindow.WindowState = WindowState.Normal;
        settingsWindow?.Activate();
    }

    private void UpdateTrayMenu()
    {
        if (tray == null) return;
        var old = tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(localization.GetCaption("Gestures.Tray.Settings", "Settings"), null, (_, _) => ShowSettings());
        menu.Items.Add(localization.GetCaption("Gestures.Tray.CheckUpdates", "Check for updates"), null, (_, _) => settingsWindow?.ShowAndCheckForUpdates());
        menu.Items.Add(localization.GetCaption("Gestures.Tray.Exit", "Exit"), null, (_, _) => Shutdown());
        tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        settingsWindow?.PrepareForExit();
        gestures?.Dispose();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        trayIcon?.Dispose();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
