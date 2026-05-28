using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace ArkManager.App.Services;

/// <summary>
/// Thin wrapper over Avalonia StorageProvider so view models can call the
/// file/folder picker without a direct dependency on Avalonia.Controls.
/// </summary>
public static class Browse
{
    /// <summary>The main window. Set in App.OnFrameworkInitializationCompleted.</summary>
    public static TopLevel? Owner { get; set; }

    public static async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        var owner = Owner;
        if (owner == null) return null;
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try { if (Directory.Exists(startPath)) start = await owner.StorageProvider.TryGetFolderFromPathAsync(startPath); }
            catch { /* ignore */ }
        }
        var result = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task CopyToClipboardAsync(string text)
    {
        var owner = Owner;
        if (owner?.Clipboard == null) return;
        await owner.Clipboard.SetTextAsync(text);
    }

    public static async Task<string?> PickFileAsync(string title, string? startPath = null)
    {
        var owner = Owner;
        if (owner == null) return null;
        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try
            {
                var dir = Directory.Exists(startPath) ? startPath : Path.GetDirectoryName(startPath);
                if (!string.IsNullOrEmpty(dir))
                    start = await owner.StorageProvider.TryGetFolderFromPathAsync(dir);
            }
            catch { /* ignore */ }
        }
        var result = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
}
