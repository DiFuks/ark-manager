using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class TextSearchTests
{
    [Fact]
    public void NextMatch_FindsFromGivenIndex()
    {
        // "ab" встречается на 0 и 4; от индекса 1 ищем следующее → 4.
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
        // от индекса 5 после последнего "ab" совпадений нет → заворот к 0.
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
        // fromIndex за пределами длины → заворачиваемся и находим с начала.
        Assert.Equal(0, TextSearch.NextMatch("abc", "abc", 999));
    }
}
