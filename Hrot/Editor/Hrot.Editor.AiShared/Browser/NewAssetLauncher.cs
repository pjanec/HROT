using System;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Recipes;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Encapsulates "build a <see cref="RecipePickerSource"/> from the per-kind
/// <see cref="INewAssetService"/> registry → build a Tree <see cref="PickerRequest"/>
/// from <see cref="RecipePickerSource.BuildEntries"/> → open it → route the picked
/// (kind, recipe) through <c>showNewAssetDialog</c>". The <c>openPicker</c> and
/// <c>showNewAssetDialog</c> are injected delegate seams so the launcher is
/// unit-testable without ImGui or a live registry.
/// </summary>
/// <remarks>
/// <para>
/// In production, wire <c>openPicker</c> to <see cref="PickerRegistry.OpenPicker"/>.
/// The pick result invokes <c>showNewAssetDialog(kind, recipe)</c>, which seeds and
/// confirms a <see cref="NewAssetDialog"/> to create+open the new asset.
/// </para>
/// <para>
/// <b>D-T7-1:</b> The interactive name/folder UI is deferred as DBT-A3.
/// Production seeds the dialog with a default name and confirms immediately.
/// </para>
/// </remarks>
public sealed class NewAssetLauncher
{
    private readonly Action<PickerRequest, Action<PickerResult>> _openPicker;
    private readonly IReadOnlyDictionary<AssetKind, INewAssetService> _services;
    private readonly Action<AssetKind, IEditableAsset> _showNewAssetDialog;
    private readonly Func<IEditableAsset, string?> _describe;
    private readonly Func<IEditableAsset, string?> _recipeCategory;

    /// <summary>
    /// Initializes the launcher.
    /// </summary>
    /// <param name="openPicker">
    /// Seam that opens a picker window from a <see cref="PickerRequest"/>.
    /// Production wires to <see cref="PickerRegistry.OpenPicker"/>; tests inject a fake.
    /// </param>
    /// <param name="services">
    /// Registry of per-kind <see cref="INewAssetService"/> implementations (never <see langword="null"/>).
    /// </param>
    /// <param name="showNewAssetDialog">
    /// Called when a recipe is picked — receives the <see cref="AssetKind"/> and the
    /// chosen recipe <see cref="IEditableAsset"/>. Production seeds and confirms a
    /// <see cref="NewAssetDialog"/>.
    /// </param>
    /// <param name="describe">
    /// Optional function returning a description for a recipe. Passed through to
    /// <see cref="RecipePickerSource"/>. When <see langword="null"/>, descriptions are omitted.
    /// </param>
    /// <param name="recipeCategory">
    /// Optional function returning a sub-category for a recipe. Passed through to
    /// <see cref="RecipePickerSource"/>. When <see langword="null"/>, the category is just the kind label.
    /// </param>
    public NewAssetLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        IReadOnlyDictionary<AssetKind, INewAssetService> services,
        Action<AssetKind, IEditableAsset> showNewAssetDialog,
        Func<IEditableAsset, string?>? describe = null,
        Func<IEditableAsset, string?>? recipeCategory = null)
    {
        _openPicker = openPicker ?? throw new ArgumentNullException(nameof(openPicker));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _showNewAssetDialog = showNewAssetDialog ?? throw new ArgumentNullException(nameof(showNewAssetDialog));
        _describe = describe ?? (_ => null);
        _recipeCategory = recipeCategory ?? (_ => null);
    }

    /// <summary>
    /// Build a Tree-layout <see cref="PickerRequest"/> from
    /// <see cref="RecipePickerSource.BuildEntries"/> and open it.
    /// On pick → <c>showNewAssetDialog(kind, recipe)</c>.
    /// Cancel → nothing.
    /// </summary>
    public void Open()
    {
        var source = new RecipePickerSource(_services, _describe, _recipeCategory);

        var request = new PickerRequest
        {
            ContextKey = "assets.new",
            Title = "New Asset",
            Layout = PickerLayout.Tree,
            SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = () => source.BuildEntries("", null),
        };

        _openPicker(request, result =>
        {
            if (result.Cancelled)
                return;

            if (result.First?.Tag is RecipeChoice rc)
                _showNewAssetDialog(rc.Kind, rc.Recipe);
        });
    }
}
