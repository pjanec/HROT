using System;
using Hrot.Editor.AiShared;

namespace Hrot.Editor;

/// <summary>
/// Routes an asset-pick action to the appropriate editor handler based on
/// <see cref="AssetKind"/>. Wired as the callback at the picker / docked-host
/// composition point (BATCH-15).
/// </summary>
/// <remarks>
/// <para>
/// Uses delegate seams (<see cref="Action{IEditableAsset}"/> /
/// <see cref="Action{string}"/>) rather than depending on concrete types like
/// <see cref="AiShared.Documents.AiDocumentManager"/> or <see cref="IEditorLogic"/>,
/// so the router is unit-testable without a full editor host.
/// </para>
/// <para>
/// In production, wire:
/// <list type="bullet">
///   <item><c>openDocument</c> → <c>documentManager.Open</c></item>
///   <item><c>loadScenario</c> → <c>editorLogic.LoadScenarioByName</c></item>
/// </list>
/// </para>
/// </remarks>
public sealed class AssetPickActionRouter
{
    private readonly Action<IEditableAsset> _openDocument;
    private readonly Action<string> _loadScenario;

    /// <summary>
    /// Initializes the router.
    /// </summary>
    /// <param name="openDocument">
    ///   Called for file-kind assets (Blueprint, BTree, Hsm).
    ///   Production wires to <c>AiDocumentManager.Open</c>.
    /// </param>
    /// <param name="loadScenario">
    ///   Called for Scenario assets with the scenario's relative path.
    ///   Production wires to <c>IEditorLogic.LoadScenarioByName</c>.
    /// </param>
    public AssetPickActionRouter(
        Action<IEditableAsset> openDocument,
        Action<string> loadScenario)
    {
        _openDocument = openDocument ?? throw new ArgumentNullException(nameof(openDocument));
        _loadScenario = loadScenario ?? throw new ArgumentNullException(nameof(loadScenario));
    }

    /// <summary>
    /// Routes the picked <paramref name="asset"/> to the appropriate handler.
    /// </summary>
    /// <param name="asset">The asset picked in the browser.</param>
    public void Route(IEditableAsset asset)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        switch (asset.Kind)
        {
            case AssetKind.Scenario:
                _loadScenario(asset.Name);
                break;

            case AssetKind.Blueprint:
            case AssetKind.BTree:
            case AssetKind.Hsm:
                _openDocument(asset);
                break;

            default:
                // Blackboard, Utility, or future kinds — no-op; never throw on
                // a normal path.
                break;
        }
    }
}
