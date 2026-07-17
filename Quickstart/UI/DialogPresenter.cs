namespace Quickstart.UI;

internal static class DialogPresenter
{
    public static DialogResult ShowModal(Form dialog, Form? owner = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var realOwner = owner is { IsDisposed: false } ? owner : null;
        var ownerWasTopMost = realOwner?.TopMost == true;
        var dialogWasTopMost = dialog.TopMost;
        Form? fallbackOwner = null;
        EventHandler? shownHandler = null;

        try
        {
            Form modalOwner;
            if (realOwner != null)
            {
                realOwner.TopMost = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
                // 有父窗体时不进任务栏，避免与主弹窗叠两个图标
                dialog.ShowInTaskbar = false;
                modalOwner = realOwner;
            }
            else
            {
                // 托盘应用无主窗体：无 owner 的 ShowDialog 在 NotifyIcon 菜单场景下
                // 可能无法可靠激活。使用隐藏 owner 保证模态与激活行为稳定。
                fallbackOwner = CreateFallbackOwner();
                fallbackOwner.Show();
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.ShowInTaskbar = true;
                modalOwner = fallbackOwner;
            }

            dialog.TopMost = true;
            shownHandler = (_, _) =>
            {
                dialog.TopMost = true;
                dialog.BringToFront();
                dialog.Activate();
            };
            dialog.Shown += shownHandler;

            return dialog.ShowDialog(modalOwner);
        }
        finally
        {
            if (shownHandler != null)
                dialog.Shown -= shownHandler;

            if (!dialog.IsDisposed)
                dialog.TopMost = dialogWasTopMost;

            if (fallbackOwner != null)
            {
                fallbackOwner.Close();
                fallbackOwner.Dispose();
            }
            else if (realOwner is { IsDisposed: false })
            {
                realOwner.TopMost = ownerWasTopMost;
                realOwner.BringToFront();
                realOwner.Activate();
            }
        }
    }

    public static DialogResult ShowMessage(
        Form? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        using var messageOwner = new MessageBoxOwner(owner);
        return MessageBox.Show(messageOwner.Window, text, caption, buttons, icon);
    }

    private static Form CreateFallbackOwner()
        => new()
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
            Opacity = 0,
            ShowIcon = false,
            Text = string.Empty
        };

    private sealed class MessageBoxOwner : IDisposable
    {
        private readonly Form? _owner;
        private readonly Form? _fallback;

        public MessageBoxOwner(Form? owner)
        {
            _owner = owner is { IsDisposed: false } ? owner : null;

            if (_owner != null)
            {
                Window = _owner;
                return;
            }

            _fallback = CreateFallbackOwner();
            _fallback.Show();
            Window = _fallback;
        }

        public IWin32Window Window { get; }

        public void Dispose()
        {
            if (_fallback != null)
            {
                _fallback.Close();
                _fallback.Dispose();
            }

            if (_owner is { IsDisposed: false })
            {
                _owner.BringToFront();
                _owner.Activate();
            }
        }
    }
}
