namespace Quickstart.Utils;

using Microsoft.Win32;

/// <summary>
/// 监听显示环境变化（分辨率切换、显示器热插拔、DPI / 桌面设置变更），去抖后
/// 在 UI 线程统一触发一次 <see cref="Changed"/>，让常驻弹窗实时重新适配，无需重启程序。
///
/// 说明：
/// - <see cref="SystemEvents.DisplaySettingsChanged"/> 是分辨率变化与显示器增删的唯一可靠来源，
///   WinForms 不会为已创建的窗体自动处理它。
/// - 该事件在一次系统调整中会连续触发多次（每个显示器一次），必须去抖，
///   否则会在拖动显示器布局时反复重排，造成闪烁与卡顿。
/// - 事件回调不在 UI 线程，统一通过绑定的控件 BeginInvoke 回投。
/// </summary>
internal static class DisplayEnvironmentWatcher
{
    /// <summary>去抖窗口：显示器布局调整期间事件会连续触发。</summary>
    private const int DebounceMs = 300;

    private static readonly object Gate = new();

    private static Control? _uiContext;
    private static System.Threading.Timer? _debounceTimer;
    private static bool _started;

    /// <summary>显示环境变化（已去抖、已回到 UI 线程）。</summary>
    public static event Action? Changed;

    /// <summary>
    /// 绑定 UI 消息上下文并开始监听。可在启动早期调用，重复调用只更新上下文。
    /// </summary>
    public static void Start(Control uiContext)
    {
        ArgumentNullException.ThrowIfNull(uiContext);

        lock (Gate)
        {
            _uiContext = uiContext;

            if (_started)
                return;

            _started = true;
            _debounceTimer = new System.Threading.Timer(_ => Pump());
        }

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>停止监听并释放定时器（进程退出前调用，避免静态事件持有已销毁的控件）。</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            if (!_started)
                return;

            _started = false;
            _uiContext = null;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        try
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
            // 退出阶段注销失败可忽略
        }
    }

    private static void OnDisplaySettingsChanged(object? sender, EventArgs e) => Schedule();

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // 只有桌面类别（分辨率/显示器布局/主题）会影响布局；配色、键盘等不触发重排。
        if (e.Category == UserPreferenceCategory.Desktop)
            Schedule();
    }

    private static void Schedule()
    {
        lock (Gate)
        {
            if (!_started || _debounceTimer == null)
                return;

            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private static void Pump()
    {
        Control? uiContext;
        lock (Gate)
        {
            uiContext = _uiContext;
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (uiContext == null)
            return;

        void Raise()
        {
            // 逐个调用并隔离异常：单个窗体的适配失败不应影响其它窗体。
            var handlers = Changed;
            if (handlers == null)
                return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch
                {
                    // 单个订阅者失败忽略
                }
            }
        }

        try
        {
            if (uiContext.IsDisposed || !uiContext.IsHandleCreated)
                return;

            uiContext.BeginInvoke(Raise);
        }
        catch (InvalidOperationException)
        {
            // 应用正在退出（ObjectDisposedException 也派生自 InvalidOperationException）
        }
    }
}
