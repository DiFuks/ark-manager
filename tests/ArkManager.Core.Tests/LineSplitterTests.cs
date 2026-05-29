using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class LineSplitterTests
{
    [Fact]
    public void Splits_on_LF()
    {
        var s = new LineSplitter();
        var lines = s.Feed("hello\nworld\n".AsSpan());
        Assert.Equal(new[] { "hello", "world" }, lines);
        Assert.Null(s.Flush());
    }

    [Fact]
    public void Splits_on_CR_only()
    {
        // steamcmd's in-place progress updates use \r without a trailing \n.
        var s = new LineSplitter();
        var lines = s.Feed("[ 24%] dl\r[ 28%] dl\r".AsSpan());
        Assert.Equal(new[] { "[ 24%] dl", "[ 28%] dl" }, lines);
    }

    [Fact]
    public void Treats_CRLF_as_one_terminator()
    {
        var s = new LineSplitter();
        var lines = s.Feed("a\r\nb\r\n".AsSpan());
        // \r emits "a", \n hits empty buffer and is suppressed; same for "b".
        Assert.Equal(new[] { "a", "b" }, lines);
    }

    [Fact]
    public void Buffers_across_feed_calls()
    {
        var s = new LineSplitter();
        Assert.Empty(s.Feed("[ 24%".AsSpan()));
        Assert.Empty(s.Feed("] dl".AsSpan()));
        var lines = s.Feed("\n".AsSpan());
        Assert.Equal(new[] { "[ 24%] dl" }, lines);
    }

    [Fact]
    public void Buffers_split_CRLF_across_chunks_without_emitting_empty_line()
    {
        var s = new LineSplitter();
        var part1 = s.Feed("a\r".AsSpan());
        var part2 = s.Feed("\nb".AsSpan());
        Assert.Equal(new[] { "a" }, part1);
        Assert.Empty(part2);
        Assert.Equal("b", s.Flush());
    }

    [Fact]
    public void Flush_returns_unterminated_tail()
    {
        var s = new LineSplitter();
        s.Feed("partial".AsSpan());
        Assert.Equal("partial", s.Flush());
        // Flush drains — second call returns null.
        Assert.Null(s.Flush());
    }

    [Fact]
    public void Suppresses_back_to_back_terminators()
    {
        var s = new LineSplitter();
        var lines = s.Feed("a\n\n\nb\n".AsSpan());
        Assert.Equal(new[] { "a", "b" }, lines);
    }
}
