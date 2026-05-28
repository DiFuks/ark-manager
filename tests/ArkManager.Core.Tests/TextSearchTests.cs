using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class TextSearchTests
{
    [Fact]
    public void NextMatch_FindsFromGivenIndex()
    {
        // "ab" appears at 0 and 4; searching for next from index 1 → 4.
        Assert.Equal(4, TextSearch.NextMatch("abxxabxx", "ab", 1));
    }

    [Fact]
    public void NextMatch_IsCaseInsensitive()
    {
        Assert.Equal(6, TextSearch.NextMatch("....\n\nMaxPlayers=70", "maxplayers", 0));
    }

    [Fact]
    public void NextMatch_WrapsAroundWhenNothingAfter()
    {
        // from index 5 there are no matches after the last "ab" → wraps back to 0.
        Assert.Equal(0, TextSearch.NextMatch("abxxab", "ab", 5));
    }

    [Fact]
    public void NextMatch_ReturnsMinusOneWhenAbsent()
    {
        Assert.Equal(-1, TextSearch.NextMatch("hello world", "zzz", 0));
    }

    [Fact]
    public void NextMatch_ReturnsMinusOneForEmptyTerm()
    {
        Assert.Equal(-1, TextSearch.NextMatch("hello", "", 0));
    }

    [Fact]
    public void NextMatch_ClampsOutOfRangeIndex()
    {
        // fromIndex past the end of the string → wrap and find from the beginning.
        Assert.Equal(0, TextSearch.NextMatch("abc", "abc", 999));
    }
}
