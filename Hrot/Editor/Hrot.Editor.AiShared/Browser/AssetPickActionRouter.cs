using System;
using Hrot.Editor.AiShared;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// Routes an asset-pick action to the appropriate host handler based on
/// <see cref="AssetKind"/>. Wired as the callback at the picker / docked-host
/// composition point (BATCH-15).
/// </summary>
/// <remarks>
/// <para>
/// Uses delegate seams (<see cref="Action{T}"/>) rather than depending on concrete types like
/// <see cref="Documents.AiDocumentManager"/> or the editor-only <c>IEditorLogic</c>, so the router is
/// unit-testable without a full editor host.
/// </para>
/// <para>
/// In production, wire:
/// <list type="bullet">
///   <item><c>openDocument</c> → <c>documentManager.Open</c></item>
///   <item><c>loadScenario</c> → <c>IScenarioSession.OpenForEdit</c></item>
/// </list>
/// </para>
/// <para>⭐⭐ <b><c>CE-049</c> (Axis-C E2) — LIFTED here from <c>Hrot.Editor/Browser</c>.</b> 📐 Measured
/// before the move *(HN-037 lesson: check the captures, do not <c>s/old/new/</c>)*: the class holds exactly
/// two <see cref="Action{T}"/> fields and names no host type, so the lift is a namespace change and
/// nothing else. ⛔ Its own doc had ALREADY promised host-independence — the file was in the wrong
/// assembly, not the wrong shape.</para>
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
