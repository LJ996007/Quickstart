namespace Quickstart.Core;

using System.IO.Pipes;

public sealed class SingleInstance : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private Task? _pipeTask;

    public event Action<string>? ArgumentReceived;

    public SingleInstance()
    {
        _mutexName = "Quickstart_SingleInstance_Mutex";
        _pipeName = "Quickstart_SingleInstance_Pipe";
    }

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, _mutexName, out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        return true;
    }

    public void StartListening()
    {
        _cts = new CancellationTokenSource();
        _pipeTask = ListenAsync(_cts.Token);
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var message = await reader.ReadToEndAsync(ct);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    ArgumentReceived?.Invoke(message.Trim());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore pipe errors, retry
            }
        }
    }

    public static bool SendToRunningInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".",
                "Quickstart_SingleInstance_Pipe", PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(message);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _pipeTask?.Wait(1000); } catch { }
        _cts?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
