using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using MyGestures.Utils;
using MyGestures.Views;
using NUnit.Framework;

namespace MyGestures.Test.Utils;

[TestFixture, Apartment(ApartmentState.STA), NonParallelizable]
public sealed class MouseGestureOverlayIntegrationTests
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan StationaryHoldDuration = TimeSpan.FromMilliseconds(150);
    private const int InitialMovementThreshold = 5;
    private const int DirectionStrokeDistance = 50;

    [Test]
    public void RightButtonSequence_ShowsOverlayOnlyAfterMovement_AndClosesBeforeReplayingClick()
    {
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources["UiFontFamily"] = new System.Windows.Media.FontFamily("Segoe UI");
        var hook = new TestMouseHook();
        MouseTrailWindow? trail = null;
        var replayedClicks = 0;
        var replayedWhileTrailOpen = false;
        var simulatedTags = new List<long>();
        var mouse = new MouseHelper((count, inputs, size) =>
        {
            replayedClicks++;
            replayedWhileTrailOpen |= trail == null || trail.IsVisible || application.Windows.OfType<MouseTrailWindow>().Any();
            simulatedTags.AddRange(inputs.Select(input => input.mi.dwExtraInfo.ToInt64()));
            return count;
        });
        using var detector = new MouseGestureDetector(mouse,
            NullLogger<MouseGestureDetector>.Instance, NullLogger<MouseTrailWindow>.Instance,
            hook, _ => false, () => false, resolveProcessName: _ => "notepad");
        var detectedGestures = new List<MouseGestureEventArgs>();
        detector.GestureDetected += (_, args) => detectedGestures.Add(args);
        var listener = new Thread(detector.Start) { IsBackground = true };
        listener.Start();
        try
        {
            Assert.That(hook.Started.Wait(ResponseTimeout), Is.True);
            RunWithDispatcher(async () =>
            {
                // No movement, small jitter, and the exact movement threshold are all ordinary clicks.
                foreach (var movement in new[] { 0, 3, InitialMovementThreshold })
                {
                    var down = hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, 10);
                    await WaitUntil(() => application.Windows.OfType<MouseTrailWindow>().Any());
                    trail = application.Windows.OfType<MouseTrailWindow>().Single();
                    var canvas = (Canvas)trail.FindName("TrailCanvas");
                    trail.UpdateLayout();
                    Assert.That(canvas.IsVisible, Is.False, "Button down must not show the overlay.");

                    await Task.Delay(StationaryHoldDuration);
                    Assert.That(canvas.IsVisible, Is.False, "Holding without movement must not show the overlay.");
                    hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, 10 + movement);

                    var previousReplayCount = replayedClicks;
                    var up = hook.Raise(Native.MouseMsg.WM_RBUTTONUP, 10 + movement);
                    await WaitUntil(() => replayedClicks == previousReplayCount + 1);
                    Assert.Multiple(() =>
                    {
                        Assert.That(down.Handled, Is.True);
                        Assert.That(up.Handled, Is.True);
                        Assert.That(trail.IsVisible, Is.False);
                        Assert.That(((MouseTrailViewModel)trail.DataContext).ShowTrail, Is.False,
                            "Movement within the threshold must not enable the overlay.");
                        Assert.That(replayedWhileTrailOpen, Is.False, "The trail must already be closed when SendInput is called.");
                        Assert.That(detectedGestures, Is.Empty);
                    });
                }

                hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, 10);
                await WaitUntil(() => application.Windows.OfType<MouseTrailWindow>().Any());
                trail = application.Windows.OfType<MouseTrailWindow>().Single();
                var gestureCanvas = (Canvas)trail.FindName("TrailCanvas");
                hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, 10 + InitialMovementThreshold + 1);
                await WaitUntil(() => gestureCanvas.IsVisible);
                hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, 10 + DirectionStrokeDistance);
                hook.Raise(Native.MouseMsg.WM_RBUTTONUP, 10 + DirectionStrokeDistance);
                await WaitUntil(() => detectedGestures.Count == 1);
                Assert.Multiple(() =>
                {
                    Assert.That(trail.IsVisible, Is.False);
                    Assert.That(replayedClicks, Is.EqualTo(3), "A gesture must not replay an ordinary click.");
                    Assert.That(detectedGestures[0].Gesture, Is.EqualTo(new[] { MoveDirection.Right }));
                    Assert.That(simulatedTags, Has.Count.EqualTo(6));
                    Assert.That(simulatedTags, Is.All.EqualTo(MouseHelper.SimulatedEventTag));
                });
            });
        }
        finally
        {
            detector.Stop();
            Assert.That(listener.Join(ResponseTimeout), Is.True);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + ResponseTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(PollInterval);
        }
        Assert.Fail("Gesture input did not complete before the timeout.");
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

    private sealed class TestMouseHook : IMouseHook
    {
        public ManualResetEventSlim Started { get; } = new();
        public event MouseHook.MouseHookEventHandler? MouseHookEvent;
        public void StartListening() => Started.Set();
        public void Dispose() { }

        public MouseHook.MouseHookEventArgs Raise(Native.MouseMsg message, int x)
        {
            var args = new MouseHook.MouseHookEventArgs(message, 0, new Native.POINT { x = x, y = 20 });
            MouseHookEvent?.Invoke(args);
            return args;
        }
    }
}
