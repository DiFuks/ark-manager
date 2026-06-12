using System;
using ArkManager.Core.Util;
using Avalonia.Threading;

namespace ArkManager.App.Services;

/// <summary>
/// UI-side console log: a <see cref="ConsoleLogBuffer"/> driven by a <see cref="DispatcherTimer"/>
/// that publishes batched updates to the bound text property a few times a second. Console
/// ViewModels (Server / RCON / Install) own one of these instead of doing <c>Log += line</c> on
/// every line — see ConsoleLogBuffer for why that froze the UI under a log flood.
///
/// <see cref="Append"/> is safe from any thread (the server's stdout/stderr callback fires on a
/// background thread), so callers no longer need to marshal each line onto the UI thread.
/// </summary>
public sealed class ConsoleLog
{
    private readonly ConsoleLogBuffer _buffer;
    private readonly Action<string> _publish;
    private readonly DispatcherTimer _timer;

    public ConsoleLog(Action<string> publish, int maxChars = 120_000, int flushMs = 100)
    {
        _publish = publish;
        _buffer = new ConsoleLogBuffer(maxChars);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(flushMs) };
        _timer.Tick += (_, _) =>
        {
            if (_buffer.Flush() is { } text) _publish(text);
        };
        _timer.Start();
    }

    /// <summary>Queue a line for display. Safe from any thread; appears on the next flush tick.</summary>
    public void Append(string line) => _buffer.Append(line);

    /// <summary>Clear the console now (UI thread).</summary>
    public void Clear() => _publish(_buffer.Clear());
}
