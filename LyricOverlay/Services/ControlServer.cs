using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using LyricOverlay.Diagnostics;

namespace LyricOverlay.Services;

/// <summary>
/// A tiny named-pipe control surface so the LyricsOverlay studio can read and change the
/// live overlay. Requests and replies are single-line JSON on <c>\\.\pipe\LyricOverlayControl</c>.
/// </summary>
public sealed class ControlServer : IDisposable
{
    public const string PipeName = "LyricOverlayControl";

    private readonly Func<JsonElement, string> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;

    public ControlServer(Func<JsonElement, string> handler) => _handler = handler;

    public void Start()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "LyricOverlay control",
        };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                server.WaitForConnection();

                using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true,
                };

                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string response;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    response = _handler(document.RootElement);
                }
                catch (Exception ex)
                {
                    Log.Error("control request failed", ex);
                    response = "{\"ok\":false,\"error\":\"bad request\"}";
                }

                writer.WriteLine(response);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                Thread.Sleep(150);
            }
            catch (Exception ex)
            {
                Log.Error("control server loop", ex);
                Thread.Sleep(250);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _thread?.Join(400);
        }
        catch
        {
            // Shutdown must never block.
        }
    }
}
