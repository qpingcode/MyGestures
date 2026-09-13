using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
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
    private volatile bool _initialMoveValid;
    private int captureGeneration;
    private readonly MouseHelper _mouseHelper;
    private readonly Func<Native.POINT, bool> _isTaskbarAt;
    private volatile bool _suspended;
    private int _isRunning;
    private bool _bypassRightButtonSequence;
    private Func<string?, MoveDirection[], string?>? _findActionName;
    private Func<string?, MoveDirection[]?, int, List<PossibleGesture>>? _getPossibleGestures;

    public event EventHandler<MouseGestureEventArgs>? GestureDetected;

    internal bool IsRunning => Volatile.Read(ref _isRunning) != 0;

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
        : this(mouseHelper, logger, trailWindowLogger, new MouseHook(logger), TaskbarWindowDetector.IsTaskbarAt, localization)
    {
    }

    internal MouseGestureDetector(
        MouseHelper mouseHelper,
        ILogger<MouseGestureDetector> logger,
        ILogger<MouseTrailWindow> trailWindowLogger,
        IMouseHook mouseHook,
        Func<Native.POINT, bool>? isTaskbarAt = null,
        LocalizationService? localization = null)
    {
        _logger = logger;
        _trailWindowLogger = trailWindowLogger;
        _mouseHook = mouseHook;
        _gestureDirectionStorage = new();
        _mouseHook.MouseHookEvent += OnMouseHookEvent;
        _mouseHelper = mouseHelper;
        _isTaskbarAt = isTaskbarAt ?? TaskbarWindowDetector.IsTaskbarAt;
        this.localization = localization ?? new LocalizationService();
        this.localization.LocaleChanged += OnLocaleChanged;
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
            case Native.MouseMsg.WM_RBUTTONDOWN:
                _bypassRightButtonSequence = _isTaskbarAt(e.ScreenPoint);
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
                _ = Application.Current.Dispatcher.BeginInvoke(() => HandleInput(message));
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

        _previousProcessName = GetProcessName();
        _trailViewModel = new MouseTrailViewModel();
        _trailViewModel.ProcessName = _previousProcessName;
        
        // 初始状态：显示前5个可能的手势
        UpdatePossibleGestures();
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            _trailWindow = new MouseTrailWindow(_trailWindowLogger, _trailViewModel);
            _trailWindow.Show();
        });

    }

    private string GetProcessName()
    {
        var previousFocusHwd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(previousFocusHwd, out uint processId);
        var process = Process.GetProcessById((int)processId);
        return process.ProcessName;
    }

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
        }
       
        Application.Current.Dispatcher.Invoke(() =>
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
            Application.Current.Dispatcher.Invoke(() =>
            {
                _trailWindow?.Close();
                _trailViewModel = null;
            });
            return;
        }
        else
        {
            ResetInvalidMove();
           // _logger.LogDebug("Mouse Right Click Up");
        }
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            _trailWindow?.Close();
            _trailViewModel = null;
        });
        
        try
        {
            _logger.LogDebug("Starting gesture for process: {ProcessName} {Gestures}", _previousProcessName, _gestureDirectionStorage.DirectionsToDisplay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get process name or file name, {processName}", _previousProcessName);
        }

        var directions = _gestureDirectionStorage.Directions;
        GestureDetected?.Invoke(
            this,
            new MouseGestureEventArgs(_previousProcessName, _point, directions, null));

        _gestureDirectionStorage.Reset();
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
        if (_trailViewModel == null) return;

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
