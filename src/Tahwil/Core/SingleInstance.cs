using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Tahwil.Core;

/// <summary>
/// The classic menu starts one process per selected file. The first process becomes the primary;
/// the rest hand their request over a named pipe and exit, so all files land in one window.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    static readonly string Id = WindowsIdentity.GetCurrent().User?.Value ?? "user";
    static readonly string PipeName = $"Tahwil.{Id}";
    readonly Mutex _mutex;
    readonly CancellationTokenSource _cts = new();

    public bool IsPrimary { get; }
    public event Action<ConversionRequest>? Received;

    public SingleInstance()
    {
        _mutex = new Mutex(true, $@"Local\Tahwil.{Id}", out bool created);
        IsPrimary = created;
    }

    public void Listen() => _ = Task.Run(async () =>
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cts.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();
                if (JsonSerializer.Deserialize<ConversionRequest>(json) is { } req) Received?.Invoke(req);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("pipe", e); }
        }
    });

    /// <summary>Sends the request to the primary. Retries briefly while it is still starting up.</summary>
    public static bool Send(ConversionRequest req)
    {
        for (int i = 0; i < 40; i++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(250);
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req));
                client.Write(bytes);
                return true;
            }
            catch { Thread.Sleep(100); }
        }
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { if (IsPrimary) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
