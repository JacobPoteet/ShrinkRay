using Microsoft.Win32;

namespace ShrinkRay.App.Services;

public interface IFolderPicker
{
    /// <returns>The chosen folder, or null when the user cancels.</returns>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>Asks for one program by its file name, such as <c>ffmpeg.exe</c>.</summary>
    /// <returns>The chosen file, or null when the user cancels.</returns>
    string? PickProgram(string title, string fileName);
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

    public string? PickProgram(string title, string fileName)
    {
        // The filter names the one program, so the HandBrake window app cannot stand in for the CLI.
        var dialog = new OpenFileDialog { Title = title, Filter = $"{fileName}|{fileName}", CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
