using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.AiShared.Recipes;

/// <summary>Common surface for the generic name+folder modal (New / Save-As).</summary>
public interface INameFolderDialog
{
    /// <summary>Modal title, e.g. "New Blueprint" / "Save As".</summary>
    string Title { get; }

    /// <summary>The display name for the asset (without extension).</summary>
    string Name { get; set; }

    /// <summary>The folder picker state tracking the selected target subfolder.</summary>
    FolderPickerState FolderPicker { get; }

    /// <summary>Returns <see langword="true"/> when the dialog can be confirmed.</summary>
    bool CanConfirm();

    /// <summary>
    /// Validates, collision-checks, creates/saves, and invokes <paramref name="onCreated"/>
    /// with the result.
    /// </summary>
    ConfirmResult Confirm(Action<IEditableAsset>? onCreated = null);
}
