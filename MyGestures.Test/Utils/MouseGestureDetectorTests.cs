using Microsoft.Extensions.Logging.Abstractions;
using MyGestures.Services;
using MyGestures.Utils;
using NUnit.Framework;

namespace MyGestures.Test.Utils;

[TestFixture]
public sealed class MouseGestureDetectorTests
{
    private static readonly TimeSpan ListenerTestTimeout = TimeSpan.FromSeconds(2);
    [TestCase("Shell_TrayWnd")]
    [TestCase("Shell_SecondaryTrayWnd")]
    public void TaskbarWindowDetector_RecognizesTaskbarClasses(string className)
    {
        Assert.That(TaskbarWindowDetector.IsTaskbarClassName(className), Is.True);
    }

    [TestCase("MSTaskListWClass")]
    [TestCase("CabinetWClass")]
    [TestCase("")]
    public void TaskbarWindowDetector_RejectsOtherClasses(string className)
    {
        Assert.That(TaskbarWindowDetector.IsTaskbarClassName(className), Is.False);
    }

    [Test]
    public void TaskbarRightClick_PassesThroughEntireButtonSequence()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => true);
        var listenerThread = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        var down = hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        var move = hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, new Native.POINT { x = 20, y = 20 });
        var up = hook.Raise(Native.MouseMsg.WM_RBUTTONUP, new Native.POINT { x = 20, y = 20 });

        Assert.Multiple(() =>
        {
            Assert.That(down.Handled, Is.False);
            Assert.That(move.Handled, Is.False);
            Assert.That(up.Handled, Is.False);
        });

        detector.Stop();
        Assert.That(listenerThread.Join(ListenerTestTimeout), Is.True);
    }

    [Test]
    public void FullscreenRightClick_PassesThroughWhenGameModeIsOn()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => false, () => true);
        detector.SuppressWhenFullscreen = true;
        var listenerThread = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        var down = hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        var up = hook.Raise(Native.MouseMsg.WM_RBUTTONUP, new Native.POINT { x = 10, y = 20 });

        Assert.Multiple(() =>
        {
            Assert.That(down.Handled, Is.False);
            Assert.That(up.Handled, Is.False);
        });

        detector.Stop();
        Assert.That(listenerThread.Join(ListenerTestTimeout), Is.True);
    }

    [Test]
    public void FullscreenRightClick_IsCapturedWhenGameModeIsOff()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => false, () => true);
        detector.SuppressWhenFullscreen = false;
        var listenerThread = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        var down = hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        Assert.That(down.Handled, Is.True);

        detector.Stop();
        Assert.That(listenerThread.Join(ListenerTestTimeout), Is.True);
    }

    [Test]
    public void FullscreenWindowDetector_TreatsMonitorCoverAsFullscreen()
    {
        var monitor = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var fullscreen = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var maximized = new Native.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        Assert.Multiple(() =>
        {
            Assert.That(FullscreenWindowDetector.CoversMonitor(fullscreen, monitor), Is.True);
            Assert.That(FullscreenWindowDetector.CoversMonitor(maximized, monitor), Is.False);
            Assert.That(FullscreenWindowDetector.IsDesktopShellClassName("Progman"), Is.True);
            Assert.That(FullscreenWindowDetector.IsDesktopShellClassName("Chrome_WidgetWin_1"), Is.False);
        });
    }

    [Test]
    public void Registry_EnableAndDisable_ControlsListenerLifetime()
    {
        var hook = new TestMouseHook();
        var mouseHelper = new MouseHelper();
        var detector = CreateDetector(mouseHelper, hook);
        using var registry = new GestureRegistry(NullLogger<GestureRegistry>.Instance, detector);

        registry.EnableDetection([], mouseHelper);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        registry.DisableDetection();
        Assert.That(detector.IsRunning, Is.False);
        Assert.That(hook.DisposeCount, Is.EqualTo(1));

        hook.ResetStarted();
        registry.EnableDetection([], mouseHelper);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        registry.DisableDetection();
        Assert.That(detector.IsRunning, Is.False);
        Assert.That(hook.StartCount, Is.EqualTo(2));
        Assert.That(hook.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void Stop_UnblocksListenerAndAllowsRestart()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook);

        var firstThread = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        detector.Stop();

        Assert.That(firstThread.Join(ListenerTestTimeout), Is.True);
        Assert.That(hook.DisposeCount, Is.EqualTo(1));

        hook.ResetStarted();
        var secondThread = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        detector.Stop();

        Assert.That(secondThread.Join(ListenerTestTimeout), Is.True);
        Assert.That(hook.StartCount, Is.EqualTo(2));
        Assert.That(hook.DisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void NormalGesture_ResolvesProcessFromEachRightClick()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => false, () => false, point => point.x < 50 ? "chrome" : "notepad");
        var listener = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        var detected = new List<MouseGestureEventArgs>();
        using var gotFirst = new ManualResetEventSlim(false);
        using var gotSecond = new ManualResetEventSlim(false);
        detector.GestureDetected += (_, args) =>
        {
            detected.Add(args);
            if (detected.Count == 1) gotFirst.Set();
            else if (detected.Count >= 2) gotSecond.Set();
        };

        RaiseHorizontalStroke(hook, 10, 20);
        Assert.That(gotFirst.Wait(ListenerTestTimeout), Is.True);
        RaiseHorizontalStroke(hook, 80, 20);
        Assert.That(gotSecond.Wait(ListenerTestTimeout), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(detected, Has.Count.EqualTo(2));
            Assert.That(detected[0].ProcessName, Is.EqualTo("chrome"));
            Assert.That(detected[1].ProcessName, Is.EqualTo("notepad"));
        });

        detector.Stop();
        Assert.That(listener.Join(ListenerTestTimeout), Is.True);
    }

    [Test]
    public void CaptureTrigger_RecordsDirectionsAndLeftClickProcess()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => false, () => false, point => point.x == 1 ? "chrome" : "other");
        var listener = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        MouseGestureEventArgs? detected = null;
        string? liveGesture = null;
        detector.GestureDetected += (_, args) => detected = args;
        detector.TriggerGestureChanged += directions => liveGesture = directions;
        var capture = detector.CaptureTriggerAsync(CancellationToken.None);
        hook.Raise(Native.MouseMsg.WM_LBUTTONUP, new Native.POINT { x = 1, y = 1 });
        hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, new Native.POINT { x = 20, y = 20 });
        hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, new Native.POINT { x = 60, y = 20 });
        hook.Raise(Native.MouseMsg.WM_RBUTTONUP, new Native.POINT { x = 60, y = 20 });

        Assert.That(capture.Wait(ListenerTestTimeout), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(capture.Result, Is.Not.Null);
            Assert.That(capture.Result!.Directions, Is.EqualTo(new[] { nameof(MoveDirection.Right) }));
            Assert.That(capture.Result.ProcessName, Is.EqualTo("chrome"));
            Assert.That(liveGesture, Is.EqualTo("→"));
            Assert.That(detected, Is.Null);
        });

        detector.Stop();
        Assert.That(listener.Join(ListenerTestTimeout), Is.True);
    }

    [Test]
    public void CaptureTrigger_KeepsWaitingOnShortClick()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => false, () => false, _ => "chrome");
        var listener = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        using var cancellation = new CancellationTokenSource();
        var capture = detector.CaptureTriggerAsync(cancellation.Token);
        hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        Assert.That(capture.Wait(TimeSpan.FromMilliseconds(150)), Is.False);

        cancellation.Cancel();
        Assert.That(capture.Wait(ListenerTestTimeout), Is.True);
        Assert.That(capture.Result, Is.Null);

        detector.Stop();
        Assert.That(listener.Join(ListenerTestTimeout), Is.True);
    }

    [Test]
    public void CaptureTrigger_CapturesFullscreenGesture()
    {
        var hook = new TestMouseHook();
        using var detector = CreateDetector(new MouseHelper(), hook, _ => false, () => true, _ => "game");
        detector.SuppressWhenFullscreen = true;
        var listener = Start(detector);
        Assert.That(hook.WaitForStart(ListenerTestTimeout), Is.True);

        var capture = detector.CaptureTriggerAsync(CancellationToken.None);
        var down = hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = 10, y = 20 });
        Assert.That(down.Handled, Is.True);

        detector.Stop();
        Assert.That(capture.Wait(ListenerTestTimeout), Is.True);
        Assert.That(listener.Join(ListenerTestTimeout), Is.True);
    }

    private static MouseGestureDetector CreateDetector(
        MouseHelper mouseHelper,
        IMouseHook hook,
        Func<Native.POINT, bool>? isTaskbarAt = null,
        Func<bool>? isForegroundFullscreen = null,
        Func<Native.POINT, string?>? resolveProcessName = null)
        => new(
            mouseHelper,
            NullLogger<MouseGestureDetector>.Instance,
            NullLogger<global::MyGestures.Views.MouseTrailWindow>.Instance,
            hook,
            isTaskbarAt,
            isForegroundFullscreen ?? (() => false),
            null,
            resolveProcessName);

    private static void RaiseHorizontalStroke(TestMouseHook hook, int startX, int y)
    {
        hook.Raise(Native.MouseMsg.WM_RBUTTONDOWN, new Native.POINT { x = startX, y = y });
        hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, new Native.POINT { x = startX + 10, y = y });
        hook.Raise(Native.MouseMsg.WM_MOUSEMOVE, new Native.POINT { x = startX + 50, y = y });
        hook.Raise(Native.MouseMsg.WM_RBUTTONUP, new Native.POINT { x = startX + 50, y = y });
    }

    private static Thread Start(MouseGestureDetector detector)
    {
        var thread = new Thread(detector.Start) { IsBackground = true };
        thread.Start();
        return thread;
    }

    private sealed class TestMouseHook : IMouseHook
    {
        private readonly ManualResetEventSlim started = new(false);

        public event MouseHook.MouseHookEventHandler? MouseHookEvent;

        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void StartListening()
        {
            StartCount++;
            started.Set();
        }

        public bool WaitForStart(TimeSpan timeout) => started.Wait(timeout);

        public void ResetStarted() => started.Reset();

        public MouseHook.MouseHookEventArgs Raise(Native.MouseMsg message, Native.POINT point)
        {
            var args = new MouseHook.MouseHookEventArgs(message, 0, point);
            MouseHookEvent?.Invoke(args);
            return args;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
