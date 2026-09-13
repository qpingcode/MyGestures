using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

namespace MyGestures.Services;

public static class UpdateStatus
{
    public const string NoUpdate = "noUpdate";
    public const string UpdateAvailable = "updateAvailable";
    public const string NotInstalled = "notInstalled";
    public const string Busy = "busy";
}

public sealed record UpdateCheckResult(string Status, string CurrentVersion, string? Version = null, bool Installed = false);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/qpingcode/MyGestures";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string DefaultChannel = "stable";
    public const string BetaChannel = "beta";
    private const string GitHubLatestReleaseUrl = "https://api.github.com/repos/qpingcode/MyGestures/releases/latest";
    private const string GitHubReleasesUrl = "https://api.github.com/repos/qpingcode/MyGestures/releases?per_page=5";
    private const string UserAgent = "MyGestures";
    private static readonly Regex SemanticVersionPattern = new(
        @"(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly HttpMessageHandler? httpHandler;
    private UpdateManager? pendingUpdateManager;
    private UpdateInfo? pendingUpdate;

    public UpdateService(HttpMessageHandler? httpHandler = null)
    {
        this.httpHandler = httpHandler;
    }

    public string CurrentVersion
    {
        get
        {
            if (VelopackLocator.IsCurrentSet)
            {
                var installed = VelopackLocator.Current?.CurrentlyInstalledVersion;
                if (installed != null) return installed.ToString();
            }
            var informational = typeof(UpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return informational?.Split('+')[0] ?? "0.0.0";
        }
    }

    public bool IsInstalled
    {
        get
        {
            try { return VelopackLocator.IsCurrentSet && new UpdateManager(RepositoryUrl).IsInstalled; }
            catch { return false; }
        }
    }

    public object Describe() => new { currentVersion = CurrentVersion, installed = IsInstalled, releasesUrl = ReleasesUrl };

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!await operationLock.WaitAsync(0, cancellationToken))
            return new UpdateCheckResult(UpdateStatus.Busy, CurrentVersion, Installed: IsInstalled);

        try
        {
            pendingUpdateManager = null;
            pendingUpdate = null;
            var currentVersion = CurrentVersion;
            if (!IsInstalled)
            {
                var latest = await ReadLatestGitHubVersionAsync(cancellationToken);
                if (latest != null && IsNewer(latest, currentVersion))
                    return new UpdateCheckResult(UpdateStatus.UpdateAvailable, currentVersion, latest, false);
                return latest == null
                    ? new UpdateCheckResult(UpdateStatus.NotInstalled, currentVersion)
                    : new UpdateCheckResult(UpdateStatus.NoUpdate, currentVersion, currentVersion);
            }

            var channel = VelopackLocator.Current?.Channel ?? DefaultChannel;
            var updateManager = new UpdateManager(
                new GithubSource(RepositoryUrl, accessToken: null, IncludeGitHubPrereleases(channel)),
                new UpdateOptions { ExplicitChannel = channel, AllowVersionDowngrade = false });
            if (!updateManager.IsInstalled)
                return new UpdateCheckResult(UpdateStatus.NotInstalled, currentVersion);

            var update = await updateManager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (update == null)
                return new UpdateCheckResult(UpdateStatus.NoUpdate, currentVersion, currentVersion, true);

            pendingUpdateManager = updateManager;
            pendingUpdate = update;
            return new UpdateCheckResult(UpdateStatus.UpdateAvailable, currentVersion, update.TargetFullRelease.Version.ToString(), true);
        }
        catch (NotInstalledException)
        {
            return new UpdateCheckResult(UpdateStatus.NotInstalled, CurrentVersion);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task DownloadAndPrepareUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var updateManager = pendingUpdateManager ?? throw new InvalidOperationException("Check for updates before downloading an update.");
            var update = pendingUpdate ?? throw new InvalidOperationException("No update is available to download.");
            await updateManager.DownloadUpdatesAsync(update, value => progress?.Report(value), cancellationToken);
            updateManager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true, restartArgs: []);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public static void OpenReleasesPage()
    {
        Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
    }

    internal static bool IncludeGitHubPrereleases(string channel)
        => channel.Equals(BetaChannel, StringComparison.OrdinalIgnoreCase)
           || channel.EndsWith($"-{BetaChannel}", StringComparison.OrdinalIgnoreCase);

    internal static bool IsNewer(string candidate, string current)
    {
        var next = ParseVersion(candidate);
        var installed = ParseVersion(current);
        return next != null && installed != null && Version.Parse(next) > Version.Parse(installed);
    }

    internal static string? ParseVersion(string text)
    {
        var match = SemanticVersionPattern.Match(text.Trim());
        return match.Success && Version.TryParse(match.Groups["version"].Value, out var version) ? version.ToString() : null;
    }

    internal async Task<string?> ReadLatestGitHubVersionAsync(CancellationToken cancellationToken)
    {
        using var client = httpHandler == null ? new HttpClient() : new HttpClient(httpHandler, disposeHandler: false);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var latest = await ReadGitHubVersionAsync(client, GitHubLatestReleaseUrl, cancellationToken);
        if (latest != null) return latest;
        return await ReadGitHubVersionAsync(client, GitHubReleasesUrl, cancellationToken);
    }

    private static async Task<string?> ReadGitHubVersionAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
                var version = ReadReleaseVersion(release);
                if (version != null) return version;
            }
            return null;
        }
        return ReadReleaseVersion(document.RootElement);
    }

    private static string? ReadReleaseVersion(JsonElement release)
    {
        if (release.TryGetProperty("name", out var name))
        {
            var version = ParseVersion(name.GetString() ?? "");
            if (version != null) return version;
        }
        return release.TryGetProperty("tag_name", out var tag) ? ParseVersion(tag.GetString() ?? "") : null;
    }
}
