using System.Net.Sockets;
using System.Text;

namespace ArkManager.Core.Services.Rcon;

/// <summary>
/// Minimal Source RCON (Valve) implementation. ARK uses the same protocol on RCONPort.
/// Packet format (little-endian):
///   int32 size      — packet size excluding the size field
///   int32 id        — client request ID
///   int32 type      — 3=AUTH, 2=AUTH_RESPONSE, 2=EXECCOMMAND, 0=RESPONSE_VALUE
///   string body     — null-terminated ASCII
///   byte 0          — empty terminator
/// </summary>
public sealed class RconClient : IAsyncDisposable
{
    private const int SERVERDATA_AUTH = 3;
    private const int SERVERDATA_AUTH_RESPONSE = 2;
    private const int SERVERDATA_EXECCOMMAND = 2;
    private const int SERVERDATA_RESPONSE_VALUE = 0;

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _nextId = 1;

    public async Task ConnectAsync(string host, int port, string password, CancellationToken ct = default)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port, ct);
        _stream = _tcp.GetStream();

        var authId = _nextId++;
        await WritePacketAsync(authId, SERVERDATA_AUTH, password, ct);

        // First we receive an empty RESPONSE_VALUE (echo), then AUTH_RESPONSE.
        // Some servers don't send the echo — we handle both scenarios.
        while (true)
        {
            var (id, type, body) = await ReadPacketAsync(ct);
            if (type == SERVERDATA_RESPONSE_VALUE) continue;
            if (type == SERVERDATA_AUTH_RESPONSE)
            {
                if (id == -1) throw new InvalidOperationException("RCON: wrong password.");
                if (id == authId) return;
                throw new InvalidOperationException("RCON: unexpected auth response.");
            }
            throw new InvalidOperationException($"RCON: unexpected packet type={type}.");
        }
    }

    public async Task<string> SendAsync(string command, CancellationToken ct = default)
    {
        if (_stream == null) throw new InvalidOperationException("Not connected.");
        var id = _nextId++;
        await WritePacketAsync(id, SERVERDATA_EXECCOMMAND, command, ct);

        // ASA echoes the command id in the response packet. Read until a packet with our id,
        // ignoring service ones (the periodic "Keep Alive" arrives with id=0 and any other
        // foreign id). IMPORTANT: do NOT rely on echoing an empty RESPONSE_VALUE marker —
        // ASA does NOT mirror it for short responses, which made the old code hang
        // (while accumulating the "Keep Alive" body into the response).
        var sb = new StringBuilder();
        string first;
        while (true)
        {
            var (rid, _, body) = await ReadPacketAsync(ct);
            if (rid != id) continue;
            sb.Append(body);
            first = body;
            break;
        }

        // A single packet holds ~4096 bytes. If the response hit the limit — it's
        // multi-segment: send an empty EXECCOMMAND marker (which ASA DOES respond to)
        // and read the remaining segments with our id until the marker packet.
        if (first.Length >= 4000)
        {
            var markerId = _nextId++;
            await WritePacketAsync(markerId, SERVERDATA_EXECCOMMAND, "", ct);
            while (true)
            {
                var (rid, _, body) = await ReadPacketAsync(ct);
                if (rid == markerId) break;
                if (rid == id) sb.Append(body);
            }
        }
        return sb.ToString();
    }

    private async Task WritePacketAsync(int id, int type, string body, CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var size = 4 + 4 + bodyBytes.Length + 2; // id + type + body + 2 nul
        var buf = new byte[4 + size];
        BitConverter.GetBytes(size).CopyTo(buf, 0);
        BitConverter.GetBytes(id).CopyTo(buf, 4);
        BitConverter.GetBytes(type).CopyTo(buf, 8);
        bodyBytes.CopyTo(buf, 12);
        // last two bytes are already zero.
        await _stream!.WriteAsync(buf, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<(int id, int type, string body)> ReadPacketAsync(CancellationToken ct)
    {
        var header = await ReadExactlyAsync(4, ct);
        var size = BitConverter.ToInt32(header);
        if (size < 10 || size > 4096) throw new InvalidDataException("RCON: bad packet size=" + size);
        var rest = await ReadExactlyAsync(size, ct);
        var id = BitConverter.ToInt32(rest, 0);
        var type = BitConverter.ToInt32(rest, 4);
        var bodyLen = size - 4 - 4 - 2;
        var body = bodyLen > 0 ? Encoding.UTF8.GetString(rest, 8, bodyLen) : "";
        return (id, type, body);
    }

    private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var off = 0;
        while (off < count)
        {
            var n = await _stream!.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n == 0) throw new IOException("RCON: connection closed.");
            off += n;
        }
        return buf;
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_stream != null) await _stream.DisposeAsync(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null; _tcp = null;
    }
}
