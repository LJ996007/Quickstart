namespace Quickstart.UI;

/// <summary>Keep native hot-label painting out of the custom Details renderer.</summary>
internal sealed class FileSearchListView : ListView
{
    private readonly ToolTip _pathTip = new();
    private readonly System.Windows.Forms.Timer _tipDelay = new() { Interval = 650 };
    private ListViewItem? _hovered;
    private Point _tipPoint;

    public FileSearchListView()
    {
        DoubleBuffered = true;
        _tipDelay.Tick += (_, _) =>
        {
            _tipDelay.Stop();
            if (_hovered != null && !string.IsNullOrEmpty(_hovered.ToolTipText))
                _pathTip.Show(_hovered.ToolTipText, this, _tipPoint.X + 12, _tipPoint.Y + 20, 5000);
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x2A1 && OwnerDraw && View == View.Details) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == 0x200 && m.WParam == IntPtr.Zero && OwnerDraw && View == View.Details)
        {
            // comctl32 repaints the name label on hover independently of custom row drawing.
            // Passive movement needs no native hot tracking; button/drag/scroll messages still pass through.
            var value = m.LParam.ToInt64();
            var point = new Point(unchecked((short)value), unchecked((short)(value >> 16)));
            var tracking = new MouseTracking { Size = System.Runtime.InteropServices.Marshal.SizeOf<MouseTracking>(), Flags = 2, Window = Handle };
            TrackMouseEvent(ref tracking);
            var item = GetItemAt(point.X, point.Y);
            if (item != _hovered)
            {
                _tipDelay.Stop(); _pathTip.Hide(this);
                _hovered = item; _tipPoint = point;
                if (item != null) _tipDelay.Start();
            }
            OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0));
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg is 0x2A3 or 0x201 or 0x204 or 0x20A) // leave, click, right click, wheel
        {
            _hovered = null; _tipDelay.Stop(); _pathTip.Hide(this);
        }
        base.WndProc(ref m);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MouseTracking { public int Size; public int Flags; public IntPtr Window; public int HoverTime; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref MouseTracking tracking);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tipDelay.Dispose(); _pathTip.Dispose(); }
        base.Dispose(disposing);
    }
}
