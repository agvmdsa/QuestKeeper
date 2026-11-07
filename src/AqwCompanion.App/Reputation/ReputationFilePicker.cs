using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace AqwCompanion.App.Reputation;

/// <summary>Shows the system file picker (one JSON file) over the main window and opens the chosen file read-only.</summary>
public sealed class ReputationFilePicker : IReputationFilePicker
{
    private static readonly FilePickerFileType JsonFiles = new("JSON files")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    public async Task<PickedFile?> PickAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }
            || TopLevel.GetTopLevel(owner)?.StorageProvider is not { } storage)
        {
            throw new InvalidOperationException("The main window is not available.");
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import reputation file",
            AllowMultiple = false,
            FileTypeFilter = [JsonFiles],
        });
        if (files.Count == 0)
        {
            return null;
        }

        return new PickedFile(await files[0].OpenReadAsync(), files[0].Name);
    }
}
