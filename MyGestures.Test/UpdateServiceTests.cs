using System.Net;
using System.Net.Http;
using MyGestures.Services;
using NUnit.Framework;

namespace MyGestures.Test;

[TestFixture]
public sealed class UpdateServiceTests
{
    [Test]
    public void RepositoryUrl_UsesGitHubProject()
    {
        Assert.That(UpdateService.RepositoryUrl, Is.EqualTo("https://github.com/qpingcode/MyGestures"));
        Assert.That(UpdateService.ReleasesUrl, Is.EqualTo("https://github.com/qpingcode/MyGestures/releases"));
    }

    [TestCase("stable", false)]
    [TestCase("beta", true)]
    [TestCase("BETA", true)]
    [TestCase("lite-stable", false)]
    [TestCase("lite-beta", true)]
    public void IncludeGitHubPrereleases_IsEnabledForBeta(string channel, bool expected)
    {
        Assert.That(UpdateService.IncludeGitHubPrereleases(channel), Is.EqualTo(expected));
    }

    [TestCase("MyGestures v1.2.3 (Beta)", "1.2.3")]
    [TestCase("v0.0.12", "0.0.12")]
    [TestCase("release-2026-09-13", null)]
    public void ParseVersion_ReadsSemanticVersions(string text, string? expected)
    {
        Assert.That(UpdateService.ParseVersion(text), Is.EqualTo(expected));
    }

    [Test]
    public void IsNewer_ComparesSemanticVersions()
    {
        Assert.That(UpdateService.IsNewer("0.0.2", "0.0.1"), Is.True);
        Assert.That(UpdateService.IsNewer("0.0.1", "0.0.1"), Is.False);
        Assert.That(UpdateService.IsNewer("0.0.1", "0.0.2"), Is.False);
    }

    [Test]
    public async Task CheckForUpdates_WhenNotInstalled_ReportsNewerGitHubRelease()
    {
        var handler = new StubHandler
        {
            Response = """{"name":"MyGestures v9.9.9 (Beta)","tag_name":"v9.9.9","draft":false}"""
        };
        var updates = new UpdateService(handler);
        var result = await updates.CheckForUpdatesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(updates.IsInstalled, Is.False);
            Assert.That(result.Installed, Is.False);
            Assert.That(result.Status, Is.EqualTo(UpdateStatus.UpdateAvailable));
            Assert.That(result.Version, Is.EqualTo("9.9.9"));
        });
    }

    [Test]
    public async Task CheckForUpdates_WhenNotInstalled_AndGitHubIsUnreachable_ReturnsNotInstalled()
    {
        var handler = new StubHandler { Status = HttpStatusCode.NotFound, Response = "[]" };
        var updates = new UpdateService(handler);
        var result = await updates.CheckForUpdatesAsync();
        Assert.That(result.Status, Is.EqualTo(UpdateStatus.NotInstalled));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Response { get; set; } = "";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Response) });
    }
}
