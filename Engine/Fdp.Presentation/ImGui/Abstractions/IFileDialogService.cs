using System.Threading.Tasks;

namespace Fdp.Presentation.Abstractions;

/// <summary>
/// Service for presenting a modal "Save As" file dialog to the user.
/// The dialog is rendered by the <see cref="Fdp.Presentation.WindowManager.WindowManager"/>
/// each frame; it resolves asynchronously when the user confirms or cancels.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Displays a "Save As" modal dialog.
    /// </summary>
    /// <param name="defaultFileName">Pre-populated file name in the dialog's input field.</param>
    /// <param name="extensionFilter">File extension filter string, e.g. <c>"*.json"</c>.</param>
    /// <returns>
    /// The full save path chosen by the user, or <c>null</c> if the user cancelled
    /// or the dialog was superseded by a subsequent call.
    /// </returns>
    Task<string?> ShowSaveAsDialogAsync(string defaultFileName, string extensionFilter);
}
