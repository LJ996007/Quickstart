namespace Quickstart.UI;

internal static class DialogPresenter
{
    public static DialogResult ShowModal(Form dialog, Form? owner = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var safeOwner = owner is { IsDisposed: false } ? owner : null;
        var ownerWasTopMost = safeOwner?.TopMost == true;
        var dialogWasTopMost = dialog.TopMost;
        EventHandler? shownHandler = null;

        try
        {
            if (safeOwner != null)
            {
                safeOwner.TopMost = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
            }
            else if (dialog.StartPosition == FormStartPosition.CenterParent)
            {
                dialog.StartPosition = FormStartPosition.CenterScreen;
            }

            dialog.ShowInTaskbar = false;
            dialog.TopMost = true;
            shownHandler = (_, _) =>
            {
                dialog.TopMost = true;
                dialog.BringToFront();
                dialog.Activate();
            };
            dialog.Shown += shownHandler;

            return safeOwner != null
                ? dialog.ShowDialog(safeOwner)
                : dialog.ShowDialog();
        }
        finally
        {
            if (shownHandler != null)
                dialog.Shown -= shownHandler;

            if (!dialog.IsDisposed)
                dialog.TopMost = dialogWasTopMost;

            if (safeOwner is { IsDisposed: false })
            {
                safeOwner.TopMost = ownerWasTopMost;
                safeOwner.BringToFront();
                safeOwner.Activate();
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

            _fallback = new Form
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(1, 1),
                Opacity = 0,
                TopMost = true
            };
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
