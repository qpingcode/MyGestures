using Microsoft.Extensions.Logging.Abstractions;
using MyGestures.Services;
using MyGestures.Utils;
using MyGestures.Views;
using NUnit.Framework;

namespace MyGestures.Test;

[TestFixture]
public sealed class GestureRegistryTests
{
    [Test]
    public void CandidateList_PrefersProcessSpecificMapping_AndKeepsOtherGlobalGestures()
    {
        using var detector = new MouseGestureDetector(new MouseHelper(), NullLogger<MouseGestureDetector>.Instance, NullLogger<MouseTrailWindow>.Instance);
        using var registry = new GestureRegistry(NullLogger<GestureRegistry>.Instance, detector);
        registry.RegisterGesture([MoveDirection.Left], _ => { }, "Global back");
        registry.RegisterGesture([MoveDirection.Left], "chrome", _ => { }, "Browser back");
        registry.RegisterGesture([MoveDirection.Right], _ => { }, "Global forward");
        var candidates = registry.GetPossibleGestures("Chrome", null);
        Assert.That(candidates.Select(candidate => candidate.ActionName), Is.EqualTo(new[] { "Browser back", "Global forward" }));
        Assert.That(registry.FindActionName("CHROME", [MoveDirection.Left]), Is.EqualTo("Browser back"));
        Assert.That(registry.FindActionName("other", [MoveDirection.Left]), Is.EqualTo("Global back"));
    }
}
