using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class FileLogTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arkmanager-filelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Prune_KeepsNewestN_DeletesTheRest()
    {
        var dir = TempDir();
        // Timestamped names sort lexicographically = chronologically; newest = highest stamp.
        for (var i = 1; i <= 6; i++)
            File.WriteAllText(Path.Combine(dir, $"server-2026010{i}-000000.log"), "x");

        FileLog.Prune(dir, "server", keep: 3);

        var left = Directory.GetFiles(dir, "server-*.log").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[] { "server-20260104-000000.log", "server-20260105-000000.log", "server-20260106-000000.log" },
            left);
    }

    [Fact]
    public void Prune_LeavesOtherPrefixesUntouched()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "server-20260101-000000.log"), "x");
        File.WriteAllText(Path.Combine(dir, "server-20260102-000000.log"), "x");
        File.WriteAllText(Path.Combine(dir, "app-20260101-000000.log"), "x");

        FileLog.Prune(dir, "server", keep: 1);

        Assert.True(File.Exists(Path.Combine(dir, "app-20260101-000000.log")));
        Assert.Single(Directory.GetFiles(dir, "server-*.log"));
    }

    [Fact]
    public void Write_PersistsLinesToDisk()
    {
        var dir = TempDir();
        string? path;
        using (var log = new FileLog(dir, "server"))
        {
            path = log.Path;
            log.Write("hello world");
        }

        Assert.NotNull(path);
        Assert.Contains("hello world", File.ReadAllText(path!));
    }

    [Fact]
    public void Write_StopsAfterSizeCap_AndLeavesANotice()
    {
        var dir = TempDir();
        string? path;
        using (var log = new FileLog(dir, "arkmanager", maxBytes: 1024))
        {
            path = log.Path;
            for (var i = 0; i < 200; i++) log.Write(new string('x', 100)); // ~20 KB worth, cap is 1 KB
            log.Write("THIS_LINE_SHOULD_BE_DROPPED");
        }

        var text = File.ReadAllText(path!);
        Assert.Contains("size cap", text);
        Assert.DoesNotContain("THIS_LINE_SHOULD_BE_DROPPED", text);
        Assert.True(text.Length < 4096, $"capped file should stay small, was {text.Length}");
    }
}
