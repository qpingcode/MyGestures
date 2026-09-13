using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MyGestures.Localization;
using MyGestures.Models;
using MyGestures.Services;
using MyGestures.Utils;

namespace MyGestures.Views;

public partial class SettingsWindow : Window
{
    private const string WebHostName = "mygestures.local";
    private const string WebOrigin = "https://mygestures.local/";
    private const string WebEntryFileName = "index.html";
    internal const string ReadSettingsMethod = "getSettings";
    internal const string SaveSettingsMethod = "saveSettings";
    private const string SuspendGesturesMethod = "suspendGestures";
    private const string ResumeGesturesMethod = "resumeGestures";
    private const string CaptureActionMethod = "captureInputAction";
    private const string WebViewDataDirectoryName = "WebView2";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly GestureSettingsStore store;
    private readonly GestureRegistry gestures;
    private readonly MouseHelper mouse;
    private readonly LocalizationService localization;
    private bool exiting;
    private bool initialized;
    private bool recordingSuspended;

    public SettingsWindow(GestureSettingsStore store, GestureRegistry gestures, MouseHelper mouse, LocalizationService localization)
    {
        this.store = store;
        this.gestures = gestures;
        this.mouse = mouse;
        this.localization = localization;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        IsVisibleChanged += (_, _) => { if (!IsVisible) ResumeDetection(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            var webDirectory = Path.Combine(AppContext.BaseDirectory, "Web");
            if (!File.Exists(Path.Combine(webDirectory, WebEntryFileName))) throw new FileNotFoundException("Web settings assets are missing.");
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), GestureSettingsStore.ApplicationDirectoryName, WebViewDataDirectoryName);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(WebHostName, webDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!args.Uri.StartsWith(WebOrigin, StringComparison.OrdinalIgnoreCase)) args.Cancel = true;
                ResumeDetection();
            };
            Browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            Browser.CoreWebView2.ProcessFailed += (_, _) => ResumeDetection();
            Browser.CoreWebView2.WebMessageReceived += OnWebMessage;
            Browser.Source = new Uri(WebOrigin + WebEntryFileName);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Settings initialization failed: {0}", exception);
            MessageBox.Show(this, localization.GetCaption("Gestures.Error.Settings", "The settings page could not be opened. Check the WebView2 installation and rebuild the Web assets."), "MyGestures", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(WebOrigin, StringComparison.OrdinalIgnoreCase)) return;
        string? id = null;
        try
        {
            var request = JsonSerializer.Deserialize<WebRequest>(e.WebMessageAsJson, JsonOptions) ?? throw new InvalidDataException("Empty Web request.");
            id = request.Id;
            object? result;
            switch (request.Method)
            {
                case ReadSettingsMethod:
                    result = store.Current;
                    break;
                case SaveSettingsMethod:
                    var settings = request.Payload.Deserialize<GestureSettings>(JsonOptions) ?? throw new InvalidDataException("Empty settings.");
                    store.Save(settings);
                    if (settings.Enabled) gestures.EnableDetection(settings.Gestures, mouse);
                    else gestures.DisableDetection();
                    if (recordingSuspended) gestures.SuspendDetection();
                    result = store.Current;
                    break;
                case SuspendGesturesMethod:
                    recordingSuspended = true;
                    gestures.SuspendDetection();
                    result = new { success = true };
                    break;
                case ResumeGesturesMethod:
                    ResumeDetection();
                    result = new { success = true };
                    break;
                case CaptureActionMethod:
                    var wasSuspended = recordingSuspended;
                    gestures.SuspendDetection();
                    try
                    {
                        var capture = new InputActionCaptureWindow(localization, request.Payload.Deserialize<InputActionCaptureWindow.CapturedAction>(JsonOptions)) { Owner = this };
                        result = capture.ShowDialog() == true ? capture.Result : null;
                    }
                    finally { if (!wasSuspended) ResumeDetection(); }
                    break;
                default:
                    throw new NotSupportedException("Unknown settings method.");
            }
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, result }, JsonOptions));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Settings request failed: {0}", exception);
            Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, error = localization.GetCaption("Gestures.Error.Save", "The operation failed. Check your configuration and try again.") }, JsonOptions));
        }
    }

    private void ResumeDetection()
    {
        recordingSuspended = false;
        gestures.ResumeDetection();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        Hide();
    }

    public void PrepareForExit()
    {
        exiting = true;
        Browser.Dispose();
    }

    private sealed class WebRequest
    {
        public string Id { get; set; } = "";
        public string Method { get; set; } = "";
        public JsonElement Payload { get; set; }
    }
}
