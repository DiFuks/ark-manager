using System.IO.Compression;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Backups;

public sealed record BackupInfo(string FilePath, DateTime CreatedUtc, long SizeBytes, string? Note)
{
    // Note carries the marker baked into the file name (parsed back by ListBackups):
    // the auto-backup sentinels read as their own labels, an empty note means a manual
    // snapshot the user didn't name, anything else is the user's own note.
    public string DisplayName => Note switch
    {
        BackupService.AutoNote => "Auto snapshot",
        BackupService.PreRestoreNote => "Pre-restore snapshot",
        null or "" => "Manual snapshot",
        var n => n,
    };
    public string Age => DisplayFormat.RelativeTime(CreatedUtc, DateTime.UtcNow);
    public string SizeText => DisplayFormat.HumanSize(SizeBytes);
}

/// <summary>
/// Backs up ShooterGame/Saved/SavedArks (+ Config + Profiles) into a timestamped zip file.
/// Rotation by count.
/// </summary>
public sealed class BackupService
{
    private readonly SettingsService _settings;
    private readonly IWorldFlusher? _flusher;

    // Reserved notes baked into auto-created snapshots. They double as DisplayName markers
    // (see BackupInfo.DisplayName), so the worker / restore path must use these constants
    // rather than literals.
    public const string AutoNote = "auto";
    public const string PreRestoreNote = "pre-restore-auto";

    // _flusher is optional: the designer / pure-logic tests construct without it. In the app it's
    // wired to ServerManager so every snapshot is preceded by a saveworld when the server is live.
    public BackupService(SettingsService settings, IWorldFlusher? flusher = null)
    {
        _settings = settings;
        _flusher = flusher;
    }

    private const string FileNamePrefix = "asa-backup-";

    /// <summary>
    /// Reads back the note baked into a backup file name
    /// (<c>asa-backup-{stamp}_{note}.zip</c>). The timestamp itself never contains an
    /// underscore, so the first underscore separates stamp from note. Returns null when
    /// there is no note suffix (a manual snapshot the user didn't name).
    /// </summary>
    internal static string? NoteFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        if (!name.StartsWith(FileNamePrefix, StringComparison.Ordinal)) return null;
        var rest = name[FileNamePrefix.Length..]; // {stamp} or {stamp}_{note}
        var us = rest.IndexOf('_');
        if (us < 0 || us + 1 >= rest.Length) return null;
        return rest[(us + 1)..];
    }

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

        // Flush the live world to disk first so the zip captures the current state, not just the
        // server's last periodic auto-save. No-op when the server isn't running (disk already final).
        if (_flusher != null)
            await _flusher.TrySaveWorldAsync(ct);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safeNote = string.IsNullOrWhiteSpace(note)
            ? ""
            : "_" + new string(note.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(40).ToArray());
        var fileName = $"{FileNamePrefix}{stamp}{safeNote}.zip";
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
        return Directory.EnumerateFiles(BackupsRoot, $"{FileNamePrefix}*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo(f.FullName, f.CreationTimeUtc, f.Length, NoteFromFileName(f.Name)))
            .ToList();
    }

    public async Task RestoreAsync(string backupZipPath, bool wipeFirst, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(backupZipPath))
            throw new FileNotFoundException("Backup not found", backupZipPath);

        var savedDir = Path.Combine(ServerRoot, "ShooterGame", "Saved");

        if (wipeFirst && Directory.Exists(savedDir))
        {
            // Before deleting — just in case, take a quick snapshot of the current Saved.
            await CreateBackupAsync(note: PreRestoreNote, progress: null, ct: ct);
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
                // The file may be locked (e.g. the current log). Try to copy via a stream with share-read.
                try
                {
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    using var dst = entry.Open();
                    using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    src.CopyTo(dst);
                }
                catch { /* skip inaccessible file */ }
            }
            if (files.Count > 0) progress?.Report((i + 1.0) / files.Count);
        }
    }
}
