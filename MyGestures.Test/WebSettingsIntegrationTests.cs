using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using MyGestures.Localization;
using MyGestures.Services;
using MyGestures.Utils;
using MyGestures.Views;
using NUnit.Framework;

namespace MyGestures.Test;

[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
public sealed class WebSettingsIntegrationTests
{
    private static readonly TimeSpan WebInitializationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private const int HiddenWindowCoordinate = -20000;
    private const string EditedActionName = "Edited gesture";
    private const string ReadRequestId = "smoke-read";
    private const string SaveRequestId = "smoke-save";

    [Test]
    public void PackagedWebPage_LoadsAndReadsAndSavesThroughNativeBridge()
    {
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources["UiFontFamily"] = new System.Windows.Media.FontFamily("Segoe UI");
        var root = Path.Combine(Path.GetTempPath(), nameof(WebSettingsIntegrationTests), Guid.NewGuid().ToString("N"));
        var localization = new LocalizationService();
        var store = new GestureSettingsStore(localization, root, Path.Combine(root, "legacy"));
        store.Current.Locale = "en-US";
        store.Save(store.Current);
        var mouse = new MouseHelper();
        using var detector = new MouseGestureDetector(mouse, NullLogger<MouseGestureDetector>.Instance, NullLogger<MouseTrailWindow>.Instance, localization);
        using var registry = new GestureRegistry(NullLogger<GestureRegistry>.Instance, detector);
        var window = new SettingsWindow(store, registry, mouse, localization) { ShowInTaskbar = false, ShowActivated = false, Left = HiddenWindowCoordinate, Top = HiddenWindowCoordinate };
        try
        {
            RunWithDispatcher(async () =>
            {
                window.Show();
                try
                {
                    await WaitUntil(async () => window.Browser.CoreWebView2 != null
                        && (await window.Browser.ExecuteScriptAsync("document.body.textContent.includes('Enable gestures')")) == "true", WebInitializationTimeout);
                }
                catch
                {
                    TestContext.WriteLine(window.Browser.CoreWebView2 == null ? "WebView not initialized" : await window.Browser.ExecuteScriptAsync("({href:location.href,text:document.body.innerText})"));
                    throw;
                }
                await window.Browser.ExecuteScriptAsync("document.querySelector('.col-name .flat-display').click();");
                await WaitUntil(async () => await window.Browser.ExecuteScriptAsync("Boolean(document.querySelector('.col-name input'))") == "true", ResponseTimeout);
                var serializedName = JsonSerializer.Serialize(EditedActionName);
                await window.Browser.ExecuteScriptAsync($"(() => {{ const input = document.querySelector('.col-name input'); Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, {serializedName}); input.dispatchEvent(new Event('input', {{ bubbles: true }})); }})();");
                await WaitUntil(() => Task.FromResult(store.Current.Gestures[0].ActionName == EditedActionName), ResponseTimeout);
                await window.Browser.ExecuteScriptAsync("document.querySelector('.n-base-selection').click();");
                await WaitUntil(async () => await window.Browser.ExecuteScriptAsync("Array.from(document.querySelectorAll('.n-base-select-option')).some(option => option.textContent.includes('Français'))") == "true", ResponseTimeout);
                await window.Browser.ExecuteScriptAsync("Array.from(document.querySelectorAll('.n-base-select-option')).find(option => option.textContent.includes('Français')).click();");
                await WaitUntil(async () => await window.Browser.ExecuteScriptAsync("document.body.textContent.includes('Activer les gestes')") == "true", ResponseTimeout);
                await WaitUntil(() => Task.FromResult(store.Current.Locale == "fr-FR"), ResponseTimeout);
                await window.Browser.ExecuteScriptAsync("window.smokeResponses = {}; chrome.webview.addEventListener('message', e => { window.smokeResponses[e.data.id] = e.data; });");
                var readRequest = JsonSerializer.Serialize(new { id = ReadRequestId, method = SettingsWindow.ReadSettingsMethod, payload = new { } });
                await window.Browser.ExecuteScriptAsync($"chrome.webview.postMessage({readRequest});");
                await WaitUntil(async () => await window.Browser.ExecuteScriptAsync($"Boolean(window.smokeResponses['{ReadRequestId}'])") == "true", ResponseTimeout);
                using var read = JsonDocument.Parse(await window.Browser.ExecuteScriptAsync($"window.smokeResponses['{ReadRequestId}']"));
                Assert.That(read.RootElement.GetProperty("result").GetProperty("gestures").GetArrayLength(), Is.EqualTo(store.Current.Gestures.Count));
                var saveRequest = JsonSerializer.Serialize(new { id = SaveRequestId, method = SettingsWindow.SaveSettingsMethod, payload = new { enabled = false, locale = "fr-FR", gestures = Array.Empty<object>() } });
                await window.Browser.ExecuteScriptAsync($"chrome.webview.postMessage({saveRequest});");
                await WaitUntil(async () => await window.Browser.ExecuteScriptAsync($"Boolean(window.smokeResponses['{SaveRequestId}'])") == "true", ResponseTimeout);
                Assert.That(store.Current.Locale, Is.EqualTo("fr-FR"));
                Assert.That(store.Current.Gestures, Is.Empty);
                var reloaded = new GestureSettingsStore(new LocalizationService(), root, Path.Combine(root, "legacy"));
                Assert.That(reloaded.Current.Locale, Is.EqualTo("fr-FR"));
            });
        }
        finally
        {
            window.PrepareForExit();
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(PollInterval);
        }
        Assert.Fail("The packaged Web settings did not respond before the timeout.");
    }

    private static void RunWithDispatcher(Func<Task> action)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var frame = new DispatcherFrame();
        Exception? failure = null;
        dispatcher.BeginInvoke(async () =>
        {
            try { await action(); }
            catch (Exception exception) { failure = exception; }
            finally { frame.Continue = false; }
        });
        Dispatcher.PushFrame(frame);
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
