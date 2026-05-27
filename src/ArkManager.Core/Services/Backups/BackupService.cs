using System.IO.Compression;

namespace ArkManager.Core.Services.Backups;

public sealed record BackupInfo(string FilePath, DateTime CreatedUtc, long SizeBytes, string? Note);

/// <summary>
/// Бэкапит ShooterGame/Saved/SavedArks (+ Config + Profiles) в zip-файл с таймстампом.
/// Ротация по count.
/// </summary>
public sealed class BackupService
{
    private readonly SettingsService _settings;

    public BackupService(SettingsService settings) => _settings = settings;

    private string ServerRoot =>
        _settings.Current.ServerInstallPath
        ?? throw new InvalidOperationException("ServerInstallPath is not set.");

    private string BackupsRoot =>
        _settings.Current.BackupsDirectory
        ?? throw new InvalidOperationException("BackupsDirectory is not set.");

    public async Task<BackupInfo> CreateBackupAsync(string? note, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(BackupsRoot);
        var savedDir = Path.Combine(ServerRoot, "ShooterGame", "Saved");
        if (!Directory.Exists(savedDir))
            throw new InvalidOperationException("ShooterGame/Saved folder not found. Run the server at least once.");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safeNote = string.IsNullOrWhiteSpace(note)
            ? ""
            : "_" + new string(note.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(40).ToArray());
        var fileName = $"asa-backup-{stamp}{safeNote}.zip";
        var filePath = Path.Combine(BackupsRoot, fileName);

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddDirToZip(zip, savedDir, "Saved", progress, ct);
        }, ct);

        var info = new FileInfo(filePath);
        var result = new BackupInfo(filePath, info.CreationTimeUtc, info.Length, note);
        await RotateAsync(ct);
        return result;
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(BackupsRoot)) return Array.Empty<BackupInfo>();
        return Directory.EnumerateFiles(BackupsRoot, "asa-backup-*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo(f.FullName, f.CreationTimeUtc, f.Length, null))
            .ToList();
    }

    public async Task RestoreAsync(string backupZipPath, bool wipeFirst, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(backupZipPath))
            throw new FileNotFoundException("Backup not found", backupZipPath);

        var savedDir = Path.Combine(ServerRoot, "ShooterGame", "Saved");

        if (wipeFirst && Directory.Exists(savedDir))
        {
            // Перед удалением — на всякий случай делаем quick-snapshot текущего Saved.
            await CreateBackupAsync(note: "pre-restore-auto", progress: null, ct: ct);
            Directory.Delete(savedDir, recursive: true);
        }

        Directory.CreateDirectory(savedDir);

        await Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(backupZipPath);
            var entries = zip.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = entries[i];
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(Path.Combine(savedDir, "..", entry.FullName));
                    continue;
                }
                var dest = Path.Combine(savedDir, "..", entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
                if (entries.Count > 0) progress?.Report((i + 1.0) / entries.Count);
            }
        }, ct);
    }

    public void Delete(string backupZipPath)
    {
        if (File.Exists(backupZipPath)) File.Delete(backupZipPath);
    }

    private async Task RotateAsync(CancellationToken ct)
    {
        var keep = _settings.Current.BackupRotationKeep;
        if (keep <= 0) return;
        await Task.Run(() =>
        {
            var files = ListBackups().ToList();
            foreach (var old in files.Skip(keep))
            {
                try { File.Delete(old.FilePath); } catch { /* ignore */ }
            }
        }, ct);
    }

    private static void AddDirToZip(ZipArchive zip, string srcDir, string entryPrefix, IProgress<double>? progress, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories).ToList();
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = files[i];
            var rel = Path.GetRelativePath(srcDir, f).Replace(Path.DirectorySeparatorChar, '/');
            var entryName = entryPrefix + "/" + rel;
            try
            {
                zip.CreateEntryFromFile(f, entryName, CompressionLevel.Fastest);
            }
            catch (IOException)
            {
                // Файл может быть залочен (например, текущий лог). Пробуем скопировать через стрим с share-read.
                try
                {
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    using var dst = entry.Open();
                    using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    src.CopyTo(dst);
                }
                catch { /* пропускаем недоступный файл */ }
            }
            if (files.Count > 0) progress?.Report((i + 1.0) / files.Count);
        }
    }
}
