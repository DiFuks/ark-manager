using System.Net.Sockets;
using System.Text;

namespace ArkManager.Core.Services.Rcon;

/// <summary>
/// Минимальная реализация Source RCON (Valve). ARK использует тот же протокол на RCONPort.
/// Формат пакета (little-endian):
///   int32 size      — размер пакета без поля size
///   int32 id        — клиентский ID запроса
///   int32 type      — 3=AUTH, 2=AUTH_RESPONSE, 2=EXECCOMMAND, 0=RESPONSE_VALUE
///   string body     — null-terminated ASCII
///   byte 0          — пустой terminator
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

        // Первым придёт пустой RESPONSE_VALUE (echo), затем AUTH_RESPONSE.
        // На некоторых серверах echo отсутствует — обрабатываем оба сценария.
        while (true)
        {
            var (id, type, body) = await ReadPacketAsync(ct);
            if (type == SERVERDATA_RESPONSE_VALUE) continue;
            if (type == SERVERDATA_AUTH_RESPONSE)
            {
                if (id == -1) throw new InvalidOperationException("RCON: неверный пароль.");
                if (id == authId) return;
                throw new InvalidOperationException("RCON: неожиданный auth response.");
            }
            throw new InvalidOperationException($"RCON: неожиданный пакет type={type}.");
        }
    }

    public async Task<string> SendAsync(string command, CancellationToken ct = default)
    {
        if (_stream == null) throw new InvalidOperationException("Не подключён.");
        var id = _nextId++;
        await WritePacketAsync(id, SERVERDATA_EXECCOMMAND, command, ct);

        // ASA эхо-ит id команды в ответном пакете. Читаем до пакета с нашим id,
        // игнорируя служебные (периодический "Keep Alive" приходит с id=0 и любым
        // чужим id). ВАЖНО: не полагаемся на эхо пустого RESPONSE_VALUE-маркера —
        // ASA его НЕ зеркалит для коротких ответов, из-за чего старый код висел
        // (и попутно копил тело "Keep Alive" в ответ).
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

        // Один пакет вмещает ~4096 байт. Если ответ упёрся в лимит — он
        // многосегментный: шлём пустой EXECCOMMAND-маркер (на него ASA ОТВЕЧАЕТ)
        // и дочитываем сегменты с нашим id до пакета-маркера.
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
        var bodyBytes = Encoding.ASCII.GetBytes(body);
        var size = 4 + 4 + bodyBytes.Length + 2; // id + type + body + 2 nul
        var buf = new byte[4 + size];
        BitConverter.GetBytes(size).CopyTo(buf, 0);
        BitConverter.GetBytes(id).CopyTo(buf, 4);
        BitConverter.GetBytes(type).CopyTo(buf, 8);
        bodyBytes.CopyTo(buf, 12);
        // последние два байта уже нули.
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
        var body = bodyLen > 0 ? Encoding.ASCII.GetString(rest, 8, bodyLen) : "";
        return (id, type, body);
    }

    private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
    {
        var buf = new byte[count];
        var off = 0;
        while (off < count)
        {
            var n = await _stream!.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n == 0) throw new IOException("RCON: соединение закрыто.");
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
