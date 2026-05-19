using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ArkManager.App.Services;

/// <summary>
/// Тонкая обёртка над Avalonia StorageProvider — чтобы вьюмодели могли
/// вызывать file/folder picker без прямой зависимости от Avalonia.Controls.
/// </summary>
public static class Browse
{
    /// <summary>Главное окно. Выставляется в App.OnFrameworkInitializationCompleted.</summary>
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
