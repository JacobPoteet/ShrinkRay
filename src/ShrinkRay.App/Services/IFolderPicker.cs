using Microsoft.Win32;

namespace ShrinkRay.App.Services;

public interface IFolderPicker
{
    /// <returns>The chosen folder, or null when the user cancels.</returns>
    string? PickFolder(string title, string? initialFolder);
}

public sealed class WpfFolderPicker : IFolderPicker
{
    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
