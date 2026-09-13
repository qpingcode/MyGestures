using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyGestures.Models;
using MyGestures.Views;
using MyGestures.Localization;

namespace MyGestures.Utils;

public class MouseGestureDetector : IDisposable
{
    private readonly ILogger<MouseGestureDetector> _logger;
    private readonly ILogger<MouseTrailWindow> _trailWindowLogger;
    private const int InitialMessageQueueCapacity = 32;
    private const int VisibleCandidateLimit = 5;
    private const int MouseMoveThrottleMilliseconds = 60;
    private readonly Queue<GestureInput> _msgQueue = new(InitialMessageQueueCapacity);
    private readonly LocalizationService localization;
    private Point _point;
    
    private string? _previousProcessName;
    private bool _isCapturing;
    private readonly IMouseHook _mouseHook;
    private MouseTrailWindow? _trailWindow;
    private MouseTrailViewModel? _trailViewModel;
    private readonly GestureDirectionStorage _gestureDirectionStorage;
    
    private Point _startPoint;
    private const int InitialValidMove = 5;
    private const uint AncestorRoot = Native.AncestorRoot;
    private volatile bool _initialMoveValid;
    private TaskCompletionSource<GestureCaptureResult?>? triggerCapture;
    private string? selectedTriggerProcessName;
    private readonly Func<Native.POINT, string?>? resolveProcessName;
    private int captureGeneration;
    private readonly MouseHelper _mouseHelper;
    private readonly Func<Native.POINT, bool> _isTaskbarAt;
    private readonly Func<bool> _isForegroundFullscreen;
    public bool SuppressWhenFullscreen { get; set; } = true;
    private volatile bool _suspended;
    private int _isRunning;
    private bool _bypassRightButtonSequence;
    private Func<string?, MoveDirection[], string?>? _findActionName;
    private Func<string?, MoveDirection[]?, int, List<PossibleGesture>>? _getPossibleGestures;

    public event EventHandler<MouseGestureEventArgs>? GestureDetected;
    public event Action<string?>? TriggerTargetChanged;
    public event Action<string>? TriggerGestureChanged;

    internal bool IsRunning => Volatile.Read(ref _isRunning) != 0;
    internal bool IsTriggerCaptureActive => Volatile.Read(ref triggerCapture) != null;

    /// <summary>
    /// 设置查找 actionName 的方法
    /// </summary>
    /// <param name="findActionName">查找 actionName 的委托，参数为进程名和当前手势方向数组，返回匹配的 actionName</param>
    public void SetActionNameFinder(Func<string?, MoveDirection[], string?> findActionName)
    {
        _findActionName = findActionName;
    }

    /// <summary>
    /// 设置获取可能手势的方法
    /// </summary>
    /// <param name="getPossibleGestures">获取可能手势的委托，参数为进程名、当前手势方向数组和最大数量，返回可能的手势列表</param>
    public void SetPossibleGesturesFinder(Func<string?, MoveDirection[]?, int, List<PossibleGesture>> getPossibleGestures)
    {
        _getPossibleGestures = getPossibleGestures;
    }

    public MouseGestureDetector(MouseHelper mouseHelper, ILogger<MouseGestureDetector> logger, ILogger<MouseTrailWindow> trailWindowLogger, LocalizationService? localization = null)
        : this(mouseHelper, logger, trailWindowLogger, new MouseHook(logger), TaskbarWindowDetector.IsTaskbarAt, FullscreenWindowDetector.IsForegroundFullscreen, localization)
    {
    }

    internal MouseGestureDetector(
        MouseHelper mouseHelper,
        ILogger<MouseGestureDetector> logger,
        ILogger<MouseTrailWindow> trailWindowLogger,
        IMouseHook mouseHook,
        Func<Native.POINT, bool>? isTaskbarAt = null,
        Func<bool>? isForegroundFullscreen = null,
        LocalizationService? localization = null,
        Func<Native.POINT, string?>? resolveProcessName = null)
    {
        _logger = logger;
        _trailWindowLogger = trailWindowLogger;
        _mouseHook = mouseHook;
        _gestureDirectionStorage = new();
        _mouseHook.MouseHookEvent += OnMouseHookEvent;
        _mouseHelper = mouseHelper;
        _isTaskbarAt = isTaskbarAt ?? TaskbarWindowDetector.IsTaskbarAt;
        _isForegroundFullscreen = isForegroundFullscreen ?? FullscreenWindowDetector.IsForegroundFullscreen;
        this.localization = localization ?? new LocalizationService();
        this.localization.LocaleChanged += OnLocaleChanged;
        this.resolveProcessName = resolveProcessName;
    }

    public Task<GestureCaptureResult?> CaptureTriggerAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<GestureCaptureResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.Exchange(ref triggerCapture, completion);
        previous?.TrySetResult(null);
        selectedTriggerProcessName = null;
        Resume();
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (Interlocked.CompareExchange(ref triggerCapture, null, completion) == completion)
                {
                    CancelCapture();
                    selectedTriggerProcessName = null;
                    completion.TrySetResult(null);
                }
            });
        }

        return completion.Task;
    }

    private void OnMouseHookEvent(MouseHook.MouseHookEventArgs e)
    {
        // 暂停期间不拦截任何鼠标事件，让右键菜单等正常工作
        if (_suspended)
        {
            return;
        }

        if (_mouseHelper.IsSimulatingInput || e.ExtraInfo == MouseHelper.SimulatedEventTag)
        {
            return;
        }
        var message = e.Msg;
        switch (message)
        {
            case Native.MouseMsg.WM_LBUTTONUP:
                if (IsTriggerCaptureActive) RememberTriggerProcess(e.ScreenPoint);
                return;
            case Native.MouseMsg.WM_RBUTTONDOWN:
                _bypassRightButtonSequence = _isTaskbarAt(e.ScreenPoint)
                    || (!IsTriggerCaptureActive && SuppressWhenFullscreen && _isForegroundFullscreen());
                if (_bypassRightButtonSequence)
                {
                    return;
                }

                lastEventTime = null;
                Post(GestureMessage.GestureButtonDown, ToPoint(e.ScreenPoint));
                e.Handled = true;
                break;
            case Native.MouseMsg.WM_MOUSEMOVE:
                if (_bypassRightButtonSequence) break;
                if (Throttling()) break;
                Post(GestureMessage.GestureButtonMove, ToPoint(e.ScreenPoint));
                break;
            case Native.MouseMsg.WM_RBUTTONUP:
                if (_bypassRightButtonSequence)
                {
                    _bypassRightButtonSequence = false;
                    return;
                }

                Post(GestureMessage.GestureButtonUp, ToPoint(e.ScreenPoint));
                e.Handled = true;
                break;
        }
    }

    private static Point ToPoint(Native.POINT point) => new(point.x, point.y);


    private DateTime? lastEventTime;
    bool Throttling()
    {
        if (!_initialMoveValid)
        {
            return false;
        }
        if (lastEventTime == null)
        {
            lastEventTime = new DateTime();
            return false;
        }

        var now = DateTime.Now;
        var timeInterval = now - lastEventTime.Value;
        if (timeInterval.TotalMilliseconds > MouseMoveThrottleMilliseconds)
        {
            lastEventTime = now;
            return false;
        }

        return true;
    }

    
    /// <summary>
    /// 暂停手势检测：忽略所有鼠标事件，右键菜单等正常工作。
    /// 仅在检测器已启动时有效。
    /// </summary>
    public void Suspend()
    {
        _suspended = true;
        Interlocked.Increment(ref captureGeneration);
        CancelCapture();
        _logger.LogDebug("MouseGestureDetector suspended");
    }

    /// <summary>
    /// 恢复手势检测。
    /// </summary>
    public void Resume()
    {
        _suspended = false;
        _logger.LogDebug("MouseGestureDetector resumed");
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return;
        }

        _suspended = false;
        Interlocked.Increment(ref captureGeneration);
        try
        {
            _mouseHook.StartListening();
            _logger.LogInformation("MouseGestureDetector started");

            while (true)
            {
                var message = WaitForMessage();
                if (message.Message == GestureMessage.Stop)
                {
                    break;
                }

                // Messages already queued when detection is suspended or stopped must not
                // create a trail window or execute a gesture.
                if (_suspended)
                {
                    continue;
                }

                // Rendering and gesture state belong to the WPF dispatcher. Queue work
                // without blocking the listener, so disabling detection cannot deadlock
                // while the UI thread waits for the listener to exit.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null) HandleInput(message);
                else _ = dispatcher.BeginInvoke(() => HandleInput(message));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Gesture listener failed.");
        }
        finally
        {
            _mouseHook.Dispose();
            lock (_msgQueue)
            {
                _msgQueue.Clear();
            }
            Interlocked.Exchange(ref _isRunning, 0);
            _logger.LogInformation("MouseGestureDetector stopped");
        }
    }

    private void HandleInput(GestureInput input)
    {
        if (_suspended || input.Generation != Volatile.Read(ref captureGeneration)) return;
        _point = input.Point;
        try
        {
                switch (input.Message)
                {
                    case GestureMessage.GestureButtonDown:
                        OnMouseDown();
                        break;
                    case GestureMessage.GestureButtonMove:
                        OnMouseMove();
                        break;
                    case GestureMessage.GestureButtonUp:
                        OnMouseUp();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Gesture input failed.");
            CancelCapture();
        }
    }

    public void Stop()
    {
        _suspended = true;
        Interlocked.Increment(ref captureGeneration);
        CancelCapture();
        CompleteTriggerCapture(null);

        if (Volatile.Read(ref _isRunning) == 0)
        {
            return;
        }

        lock (_msgQueue)
        {
            _msgQueue.Clear();
            _msgQueue.Enqueue(new GestureInput(GestureMessage.Stop, default, captureGeneration));
            Monitor.PulseAll(_msgQueue);
        }
    }
    
    GestureInput WaitForMessage()
    {
        GestureInput gestureMessage;
        lock (_msgQueue)
        {
            while (_msgQueue.Count == 0) Monitor.Wait(_msgQueue);
            gestureMessage = _msgQueue.Dequeue();
        }
        return gestureMessage;
    }
    
    private void OnMouseDown()
    {
        _startPoint = _point;
        if (_isCapturing)
        {
            _logger.LogDebug("Mouse Right Click Down, but ignore as isCapturing");
            return;
        }
        else
        {
            _isCapturing = true;
            _logger.LogDebug("Mouse Right Click Down, start capturing");
        }

        ResetInvalidMove();
        _gestureDirectionStorage.Reset();

        if (IsTriggerCaptureActive)
        {
            if (string.IsNullOrEmpty(selectedTriggerProcessName))
            {
                selectedTriggerProcessName = ResolveProcessName(ToNativePoint(_point));
                TriggerTargetChanged?.Invoke(selectedTriggerProcessName);
            }

            _previousProcessName = selectedTriggerProcessName;
        }
        else
        {
            _previousProcessName = ResolveProcessName(ToNativePoint(_point));
        }

        _trailViewModel = new MouseTrailViewModel();
        _trailViewModel.ProcessName = _previousProcessName ?? "";
        _trailViewModel.AddPoint(_point);
        
        // 初始状态：显示前5个可能的手势
        UpdatePossibleGestures();
        
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.Invoke(() =>
        {
            _trailWindow = new MouseTrailWindow(_trailWindowLogger, _trailViewModel);
            _trailWindow.ShowActivated = false;
            _trailWindow.Show();
            _trailWindow.Topmost = false;
            _trailWindow.Topmost = true;
            _trailWindow.UpdateDrawing();
        });

    }

    private void RememberTriggerProcess(Native.POINT point)
    {
        var processName = ResolveProcessName(point);
        if (string.IsNullOrEmpty(processName)) return;
        selectedTriggerProcessName = processName;
        TriggerTargetChanged?.Invoke(processName);
    }

    private string? ResolveProcessName(Native.POINT point)
    {
        if (resolveProcessName != null) return NormalizeProcessName(resolveProcessName(point));
        try
        {
            var fromPoint = ProcessNameFromWindow(Native.WindowFromPoint(point));
            if (IsUsableTargetProcess(fromPoint)) return fromPoint;
            var fromFocus = ProcessNameFromWindow(Native.GetForegroundWindow());
            if (IsUsableTargetProcess(fromFocus)) return fromFocus;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not resolve the target process.");
        }

        return null;
    }

    private static string? ProcessNameFromWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        var root = Native.GetAncestor(window, AncestorRoot);
        if (root != IntPtr.Zero) window = root;
        Native.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return null;
        using var process = Process.GetProcessById((int)processId);
        return NormalizeProcessName(process.ProcessName);
    }

    private static string? NormalizeProcessName(string? processName)
        => string.IsNullOrWhiteSpace(processName) ? null : processName.Trim().ToLowerInvariant();

    private static bool IsUsableTargetProcess(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        using var current = Process.GetCurrentProcess();
        return !processName.Equals(current.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static Native.POINT ToNativePoint(Point point) => new() { x = (int)point.X, y = (int)point.Y };

    void ResetInvalidMove()
    {
        _initialMoveValid = false;
    }

    private void OnMouseMove()
    {
        if (!_isCapturing)
        {
            return;
        }

        if (!IsValidMove())
        {
            return;
        }
        _gestureDirectionStorage.Detect(_point);
        if (_trailViewModel != null)
        {
            _trailViewModel.DirectionsText = _gestureDirectionStorage.DirectionsToDisplay;
            _trailViewModel.AddPoint(_point);
            
            // 更新可能的手势列表
            UpdatePossibleGestures();
            if (IsTriggerCaptureActive)
                TriggerGestureChanged?.Invoke(_gestureDirectionStorage.DirectionsToDisplay);
        }
       
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.Invoke(() =>
        {
            _trailWindow?.UpdateDrawing();
        });
        
        bool IsValidMove()
        {
            if (_initialMoveValid) return true;
        
            var initialMoveDist = GetPointDistance(ref _point, ref _startPoint);
            if (initialMoveDist > InitialValidMove)
            {
                _initialMoveValid = true;
            }
            return _initialMoveValid;
        }
        
        float GetPointDistance(ref Point a, ref Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (int)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private void OnMouseUp()
    {
    
        _isCapturing = false;
        
        if (!_initialMoveValid)
        {
           // _logger.LogDebug("Mouse Right Click Up ( Invalid Move, Simulating Right Click )");
            _mouseHelper.RightClick(_point);
            CloseTrailWindow();
            return;
        }
        else
        {
            ResetInvalidMove();
           // _logger.LogDebug("Mouse Right Click Up");
        }
        
        CloseTrailWindow();
        
        try
        {
            _logger.LogDebug("Starting gesture for process: {ProcessName} {Gestures}", _previousProcessName, _gestureDirectionStorage.DirectionsToDisplay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get process name or file name, {processName}", _previousProcessName);
        }

        var directions = _gestureDirectionStorage.Directions;
        if (IsTriggerCaptureActive)
        {
            if (directions.Length == 0) return;
            CompleteTriggerCapture(new GestureCaptureResult
            {
                Directions = directions.Select(direction => direction.ToString()).ToArray(),
                ProcessName = NormalizeProcessName(_previousProcessName),
            });
            _gestureDirectionStorage.Reset();
            return;
        }

        GestureDetected?.Invoke(
            this,
            new MouseGestureEventArgs(_previousProcessName, _point, directions, null));

        _gestureDirectionStorage.Reset();
    }

    private void CompleteTriggerCapture(GestureCaptureResult? result)
    {
        var completion = Interlocked.Exchange(ref triggerCapture, null);
        selectedTriggerProcessName = null;
        completion?.TrySetResult(result);
    }

    private void CloseTrailWindow()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _trailWindow = null;
            _trailViewModel = null;
            return;
        }

        dispatcher.Invoke(() =>
        {
            _trailWindow?.Close();
            _trailViewModel = null;
        });
    }
    
    private void Post(GestureMessage msg, Point point)
    {
        lock (_msgQueue)
        {
            if (_suspended || Volatile.Read(ref _isRunning) == 0)
            {
                return;
            }

            _msgQueue.Enqueue(new GestureInput(msg, point, Volatile.Read(ref captureGeneration)));
            Monitor.Pulse(_msgQueue);
        }
    }

    private enum GestureMessage : uint
    {
        GestureButtonDown = 1,
        GestureButtonUp = 2,
        GestureButtonMove = 3,
        Stop = 4,
    }

    private readonly record struct GestureInput(GestureMessage Message, Point Point, int Generation);

    private void UpdatePossibleGestures()
    {
        if (_trailViewModel == null || IsTriggerCaptureActive) return;

        var currentDirections = _gestureDirectionStorage.Directions;
        
        if (_getPossibleGestures != null)
        {
            var possibleGestures = _getPossibleGestures(_previousProcessName, currentDirections.Length > 0 ? currentDirections : null, VisibleCandidateLimit);
            
            _trailViewModel.PossibleGestures.Clear();
            foreach (var gesture in possibleGestures)
            {
                _trailViewModel.PossibleGestures.Add(gesture);
            }
            
            // 如果没有匹配的手势，显示提示信息
            if (possibleGestures.Count == 0 && currentDirections.Length > 0)
            {
                _trailViewModel.NoMatchMessage = localization.GetCaption("Gestures.Trail.NoMatch", "No matching gesture");
            }
            else
            {
                _trailViewModel.NoMatchMessage = string.Empty;
            }
        }
        else
        {
            _trailViewModel.PossibleGestures.Clear();
            _trailViewModel.NoMatchMessage = string.Empty;
        }
    }

    public void Dispose()
    {
        Stop();
        localization.LocaleChanged -= OnLocaleChanged;
        _mouseHook.MouseHookEvent -= OnMouseHookEvent;
    }

    private void OnLocaleChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || _trailViewModel == null) return;
        _ = dispatcher.BeginInvoke(() =>
        {
            UpdatePossibleGestures();
            _trailWindow?.UpdateDrawing();
        });
    }

    private void CancelCapture()
    {
        _isCapturing = false;
        ResetInvalidMove();
        _gestureDirectionStorage.Reset();

        var trailWindow = _trailWindow;
        _trailWindow = null;
        _trailViewModel = null;
        if (trailWindow == null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            trailWindow.Close();
        }
        else
        {
            _ = dispatcher.BeginInvoke(trailWindow.Close);
        }
    }
}

public class MouseGestureEventArgs(
    string? processName,
    Point lastPoint,
    MoveDirection[] directions,
    string? selectionText)
    : EventArgs
{
    public MoveDirection[] Gesture { get; } = directions;

    public Point LastPoint { get; } = lastPoint;
    public string? ProcessName { get; } = processName;

    public string? SelectionText { get; } = selectionText;
}
