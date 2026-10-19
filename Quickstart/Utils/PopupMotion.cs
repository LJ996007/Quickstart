namespace Quickstart.Utils;

using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// 手势面板的方向滑入：右滑面板从左往右、左滑面板从右往左，方向与手势一致。
/// 只移动窗口位置、不改透明度，避免切换 WS_EX_LAYERED 影响圆角与命中检测。
/// </summary>
internal static class PopupMotion
{
    private const int DurationMs = 120;
    private const int OffsetLogical = 14;
    private static readonly ConditionalWeakTable<Form, SlideState> States = new();

    /// <summary>
    /// 调用前先把窗体放到最终位置；<paramref name="direction"/> 为 +1 表示从左向右滑入，-1 表示从右向左。
    /// 在 Show() 之前调用。
    /// </summary>
    public static void SlideIn(Form form, int direction)
    {
        if (States.TryGetValue(form, out var previous))
            previous.Stop();

        if (!SystemInformation.UIEffectsEnabled || direction == 0)
            return;

        var target = form.Location;
        var workingArea = Screen.FromRectangle(form.Bounds).WorkingArea;
        var offset = UiScaleHelper.Scale(form, OffsetLogical);
        var startX = target.X - Math.Sign(direction) * offset;
        // 起点留在同一块屏幕内：越界会触发跨屏 DPI 切换
        startX = Math.Clamp(startX, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - form.Width));
        if (startX == target.X)
            return;

        var state = new SlideState(form, new Point(startX, target.Y), target);
        States.AddOrUpdate(form, state);
        state.Start();
    }

    private sealed class SlideState
    {
        private readonly Form _form;
        private readonly Point _start;
        private readonly Point _target;
        private readonly Stopwatch _clock = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };
        private Point _lastSet;

        public SlideState(Form form, Point start, Point target)
        {
            _form = form;
            _start = start;
            _target = target;
            _timer.Tick += (_, _) => Step();
        }

        public void Start()
        {
            _form.Location = _start;
            _lastSet = _start;
            _clock.Start();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _timer.Dispose();
            States.Remove(_form);
        }

        private void Step()
        {
            if (_form.IsDisposed)
            {
                Stop();
                return;
            }

            // 动画期间窗体被别处移动（拖动、重新定位）或已隐藏：交还控制权
            if (_form.Location != _lastSet || !_form.Visible)
            {
                if (!_form.Visible && _form.Location == _lastSet)
                    _form.Location = _target;
                Stop();
                return;
            }

            var t = Math.Min(1d, _clock.Elapsed.TotalMilliseconds / DurationMs);
            var eased = 1d - Math.Pow(1d - t, 3);
            var x = (int)Math.Round(_start.X + (_target.X - _start.X) * eased);
            _lastSet = new Point(x, _target.Y);
            _form.Location = _lastSet;

            if (t >= 1d)
                Stop();
        }
    }
}
