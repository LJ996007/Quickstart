namespace Quickstart.Utils;

using System.Collections.Concurrent;

/// <summary>
/// 后台 STA 线程串行执行图标提取等 COM/Shell 相关工作，避免阻塞 UI 线程。
/// </summary>
public sealed class AsyncIconLoader : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private bool _disposed;

    public AsyncIconLoader(string name = "Quickstart.IconLoader")
    {
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = name,
            Priority = ThreadPriority.BelowNormal
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Enqueue(Action work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _queue.Add(work);
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding 之后丢弃
        }
    }

    private void ThreadMain()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch
            {
                // 单次任务失败不影响后续队列
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(2000))
        {
            // 后台线程，进程退出时由 OS 回收
        }
        _queue.Dispose();
    }
}
