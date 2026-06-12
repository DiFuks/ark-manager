using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class ConsoleLogBufferTests
{
    [Fact]
    public void Flush_ReturnsAppendedLines_NewlineTerminated()
    {
        var b = new ConsoleLogBuffer(maxChars: 10_000);
        b.Append("hello");
        Assert.Equal("hello\n", b.Flush());
    }

    [Fact]
    public void Flush_BatchesEveryPendingAppendIntoOneResult()
    {
        // The whole point: many Appends collapse into a single flush (one UI re-render),
        // regardless of how fast lines arrive.
        var b = new ConsoleLogBuffer(maxChars: 10_000);
        b.Append("a");
        b.Append("b");
        b.Append("c");
        Assert.Equal("a\nb\nc\n", b.Flush());
    }

    [Fact]
    public void Flush_AccumulatesAcrossFlushes()
    {
        var b = new ConsoleLogBuffer(maxChars: 10_000);
        b.Append("one");
        b.Flush();
        b.Append("two");
        Assert.Equal("one\ntwo\n", b.Flush());
    }

    [Fact]
    public void Flush_ReturnsNull_WhenNothingPending()
    {
        var b = new ConsoleLogBuffer(maxChars: 10_000);
        b.Append("x");
        b.Flush();
        Assert.Null(b.Flush()); // nothing new since last flush
    }

    [Fact]
    public void Flush_TrimsHeadAtLineBoundary_WhenOverCap()
    {
        // cap small enough that early lines must be dropped; trimming keeps whole lines.
        var b = new ConsoleLogBuffer(maxChars: 20);
        for (var i = 0; i < 50; i++) b.Append("line" + i);
        var text = b.Flush()!;

        Assert.True(text.Length <= 20, $"expected <= 20, got {text.Length}");
        // Head trimmed on a line boundary → no partial leading line.
        Assert.DoesNotContain("\n\n", text);
        Assert.StartsWith("line", text);          // starts at a real line start, not mid-token
        Assert.EndsWith("line49\n", text);        // newest line is always kept
    }

    [Fact]
    public void Clear_EmptiesTextAndPending()
    {
        var b = new ConsoleLogBuffer(maxChars: 10_000);
        b.Append("stuff");
        b.Flush();
        Assert.Equal("", b.Clear());
        Assert.Equal("", b.Text);
        Assert.Null(b.Flush()); // pending was dropped too
    }

    [Fact]
    public void Text_ReflectsLastFlush()
    {
        var b = new ConsoleLogBuffer(maxChars: 10_000);
        b.Append("z");
        Assert.Equal("", b.Text);   // not flushed yet
        b.Flush();
        Assert.Equal("z\n", b.Text);
    }
}
