using System.IO.Compression;
using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Backups;
using Xunit;

namespace ArkManager.Core.Tests;

public class BackupServiceTests
{
    private sealed class RecordingFlusher : IWorldFlusher
    {
        public int Calls { get; private set; }
        public bool SeenSavedFileWhenFlushed { get; private set; }
        public Func<bool>? OnFlush { get; init; }

        public Task<bool> TrySaveWorldAsync(CancellationToken ct = default)
        {
            Calls++;
            SeenSavedFileWhenFlushed = OnFlush?.Invoke() ?? true;
            return Task.FromResult(true);
        }
    }

    private static (SettingsService settings, string savedDir, string backupsDir) MakeEnv(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "ark-bak-" + Guid.NewGuid().ToString("N"));
        var srv = Path.Combine(root, "srv");
        var backups = Path.Combine(root, "backups");
        var savedDir = Path.Combine(srv, "ShooterGame", "Saved");
        Directory.CreateDirectory(savedDir);

        var paths = new AppPaths(root);
        File.WriteAllText(paths.SettingsFile, $$"""
            {
              "schemaVersion": 2,
              "serverInstallPath": "{{srv.Replace("\\", "\\\\")}}",
              "backupsDirectory": "{{backups.Replace("\\", "\\\\")}}"
            }
            """);
        return (new SettingsService(paths), savedDir, backups);
    }

    [Fact]
    public async Task CreateBackup_FlushesWorld_BeforeZipping()
    {
        var (settings, savedDir, _) = MakeEnv(out var root);
        try
        {
            File.WriteAllText(Path.Combine(savedDir, "world.ark"), "data");
            var flusher = new RecordingFlusher();
            var svc = new BackupService(settings, flusher);

            var info = await svc.CreateBackupAsync(note: null);

            Assert.Equal(1, flusher.Calls);
            Assert.True(File.Exists(info.FilePath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CreateBackup_FlushRunsBeforeFilesAreRead()
    {
        var (settings, savedDir, _) = MakeEnv(out var root);
        try
        {
            // The flusher writes the live world file; only if saveworld runs *before* enumeration
            // does the snapshot contain it. Asserts ordering, not just that the call happened.
            var liveFile = Path.Combine(savedDir, "live.ark");
            var flusher = new RecordingFlusher
            {
                OnFlush = () => { File.WriteAllText(liveFile, "flushed"); return true; },
            };
            var svc = new BackupService(settings, flusher);

            var info = await svc.CreateBackupAsync(note: null);

            using var zip = ZipFile.OpenRead(info.FilePath);
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("live.ark", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CreateBackup_WithoutFlusher_StillWorks()
    {
        var (settings, savedDir, _) = MakeEnv(out var root);
        try
        {
            File.WriteAllText(Path.Combine(savedDir, "world.ark"), "data");
            var svc = new BackupService(settings);

            var info = await svc.CreateBackupAsync(note: null);

            Assert.True(File.Exists(info.FilePath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
