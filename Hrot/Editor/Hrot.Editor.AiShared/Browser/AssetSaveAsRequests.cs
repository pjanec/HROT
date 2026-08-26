using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Catalog;
using NodeEditor.UI.Dialogs;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐⭐ <b><c>CE-049</c> (Axis-C <b>E2</b>) — the <see cref="SaveAsRequest"/> builder, shared.</b>
/// 📄 <c>docs/DESIGN_Cgf_Asset_Picker_Shell_Slice.md</c> §3 ③ *(the as-built adds this type — see §8)*.
///
/// <para>⚠⚠ <b>NOT in the design's item list, and here is the argument for adding it.</b> Item ③ says CGF
/// must pass a <b>real</b> <c>openSaveAsDialog</c> to <c>ScenarioMenuCommands.Register</c>. 📐 Measured:
/// the editor's dialog is built by a ~35-line local function *(<c>BuildSaveAsRequest</c>)* plus a
/// <c>FolderOf</c> helper, both closed over the catalog and the base-folder resolver. ⇒ ⛔ giving CGF a
/// real dialog by re-typing that body would create the <b>third</b> copy of a create/save-path helper in
/// the very batch whose item ② exists to collapse the second. ⭐ So it moved here instead.</para>
///
/// <para>⭐ <b>The <c>New</c>, <c>Save-As</c> and <c>Save Scenario As</c> flows all use this one builder</b>
/// — that was already true inside the editor *(one local function, three call sites)*; this only widens the
/// scope from one assembly to both hosts.</para>
/// </summary>
public static class AssetSaveAsRequests
{
    /// <summary>
    /// ⭐ The base-folder resolver both hosts use: the kind's Assets root, or <c>null</c> for a kind that
    /// has none. ⚠ <c>Scenario</c> is exactly that case — it is NAS/orchestrator-backed and
    /// <see cref="AssetRoots"/> throws for it, so the <c>catch</c> is the intended path and not a swallow.
    /// </summary>
    public static string? DefaultBaseFolderFor(AssetKind kind)
    {
        try { return AssetRoots.AssetsFor(kind); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>The directory part of an asset's relative path for the given kind; <c>""</c> at the root.</summary>
    public static string FolderOf(IEditableAsset asset, AssetKind kind, Func<AssetKind, string?> baseFolderFor)
    {
        var rel = AssetRelPath.RelPath(asset, baseFolderFor(kind));
        int lastSlash = rel.LastIndexOf('/');
        return lastSlash >= 0 ? rel.Substring(0, lastSlash) : "";
    }

    /// <summary>
    /// Builds the Save-As browser request for one kind: the folder tree, that folder's existing assets,
    /// folder creation, the name-collision probe and name validation.
    /// </summary>
    /// <param name="folderPicker">
    /// ⚠ Passed in rather than built here because the CALLER decides which folders are offered — the New
    /// flow seeds it from the kind's known subfolders, and a Save-As of an open document seeds it from
    /// that document's own folder.
    /// </param>
    public static SaveAsRequest Build(
        IAssetCatalog             catalog,
        AssetKind                 kind,
        string                    title,
        string                    initialName,
        string                    initialDestination,
        string                    confirmLabel,
        FolderPickerState         folderPicker,
        Func<AssetKind, string?>? baseFolderFor = null)
    {
        if (catalog      is null) throw new ArgumentNullException(nameof(catalog));
        if (folderPicker is null) throw new ArgumentNullException(nameof(folderPicker));

        var baseFolder = baseFolderFor ?? DefaultBaseFolderFor;

        return new SaveAsRequest
        {
            Title              = title,
            InitialName        = initialName,
            InitialDestination = initialDestination,
            ConfirmLabel       = confirmLabel,
            GetFolderTree = () => AssetFolderDerivation.ToCategoryNode(folderPicker.FolderPaths.ToList()),
            GetFolderContents = folder => catalog.All
                .Where(a => a.Kind == kind && FolderOf(a, kind, baseFolder) == folder)
                .Select(a => new SaveAsContentItem(a.Name, AssetKindIcons.GetIconKey(kind)))
                .ToList(),
            OnCreateFolder = (parent, newName) => folderPicker.AddFolder(parent, newName),
            NameExists = (name, dest) => catalog.All.Any(a =>
                a.Kind == kind &&
                FolderOf(a, kind, baseFolder) == dest &&
                a.Name == name),
            ValidateName = name => string.IsNullOrWhiteSpace(name)
                ? "Name must not be empty."
                : null,
        };
    }
}
