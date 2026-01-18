using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CafePos.Desktop.Server;

public class WsHub
{
    private readonly List<WebSocket> _clients = new();
    private readonly object _lock = new();

    public Task AddClient(WebSocket ws)
    {
        lock (_lock) _clients.Add(ws);
        return Task.CompletedTask;
    }

    public async Task ListenUntilClosed(WebSocket ws)
    {
        var buffer = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                // We don’t need incoming messages in MVP.
            }
        }
        finally
        {
            lock (_lock) _clients.Remove(ws);
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }

    public async Task Broadcast(WsEvent ev)
    {
        var json = JsonSerializer.Serialize(ev);
        var bytes = Encoding.UTF8.GetBytes(json);

        List<WebSocket> snapshot;
        lock (_lock) snapshot = _clients.ToList();

        foreach (var ws in snapshot)
        {
            if (ws.State != WebSocketState.Open) continue;
            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
                // Ignore send errors; client will reconnect.
            }
        }
    }
}
