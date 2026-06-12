using System.Collections.Generic;
using System.Numerics;
using Fdp.Presentation.Icons;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Adapters;

/// <summary>
/// <see cref="IIconProvider"/> backed by the engine's famfamfam-silk icon atlas.
/// Maps NodeEdit icon keys (e.g. <c>bt/sequence</c>, <c>hsm/state_simple</c>,
/// <c>bp/event</c>) to silk atlas cell coordinates and returns an
/// <see cref="IconHandle"/> containing the correct UV sub-rect.
/// <para>
/// Construction takes an <see cref="IconAtlas"/> (no GPU calls), so the class
/// is headless-testable.
/// </para>
/// <para>
/// Unknown keys return <see langword="false"/> from <see cref="TryGet"/>
/// (no fallback cell is synthesised so the caller can suppress the icon).
/// </para>
/// </summary>
public sealed class SilkIconProvider : IIconProvider
{
    private readonly IconAtlas _atlas;
    private readonly IReadOnlyDictionary<string, string> _keyToCell;

    // ── Silk atlas cell map ────────────────────────────────────────────────────
    //
    // Cells use the famfamfam-silk coordinate notation: letter = row (a=0, b=1, …),
    // number = column (1-based).  The mapping below is a best-effort semantic
    // assignment using silk icons that are visually appropriate.
    //
    // BTree composite nodes  → "flow" icons
    // BTree leaf nodes       → "action" / "lightning" icons
    // BTree decorator pills  → "tag" / "wrench" icons
    // HSM states             → "shape" / "application" icons
    // Blueprint nodes        → "brick/event" icons
    // Status icons           → "icon_error" / "bullet" row icons
    //
    private static readonly IReadOnlyDictionary<string, string> DefaultCellMap =
        new Dictionary<string, string>
        {
            // ── BTree composites ────────────────────────────────────────────────
            ["bt/sequence"]           = "c9",   // arrow-right → sequential flow
            ["bt/selector"]           = "c10",  // arrow-branch → select
            ["bt/observer_selector"]  = "c11",  // arrow-circle → reactive
            ["bt/parallel"]           = "c12",  // arrow-fork → parallel
            ["bt/root"]               = "a1",   // house → root/entry
            ["bt/composite"]          = "c8",   // folder → composite category

            // ── BTree leaves ────────────────────────────────────────────────────
            ["bt/action"]             = "b4",   // lightning-bolt → action
            ["bt/condition"]          = "b5",   // tick circle → condition/check
            ["bt/wait"]               = "b6",   // clock → wait/delay
            ["bt/subtree"]            = "b7",   // page-code → subtree reference
            ["bt/leaf"]               = "b3",   // bullet → leaf category

            // ── BTree decorator pills ────────────────────────────────────────────
            ["bt/decorator"]          = "d2",   // tag → decorator category
            ["bt/inverter"]           = "d3",   // exclamation → invert
            ["bt/repeater"]           = "d4",   // refresh → repeat
            ["bt/cooldown"]           = "d5",   // hourglass → cooldown
            ["bt/force_success"]      = "d6",   // tick → force success
            ["bt/force_failure"]      = "d7",   // cross → force failure
            ["bt/until_success"]      = "d8",   // arrow-circle-ok → until success
            ["bt/until_failure"]      = "d9",   // arrow-circle-error → until failure

            // ── HSM states ───────────────────────────────────────────────────────
            ["hsm/state_simple"]      = "e1",   // circle → simple state
            ["hsm/state_composite"]   = "e2",   // layers → composite state
            ["hsm/state_parallel"]    = "e3",   // parallel-lines → parallel state
            ["hsm/state_final"]       = "e4",   // stop-disc → final state
            ["hsm/state_history"]     = "e5",   // clock-history → history
            ["hsm/state_deep_history"]= "e6",   // clock-double → deep history
            ["hsm/transition"]        = "e7",   // arrow-right → transition
            ["hsm/initial"]           = "e8",   // dot → initial pseudostate

            // ── Blueprint node categories ────────────────────────────────────────
            ["bp/event"]              = "f1",   // lightning → event
            ["bp/function"]           = "f2",   // gear → function
            ["bp/variable_get"]       = "f3",   // box-get → variable read
            ["bp/variable_set"]       = "f4",   // box-set → variable write
            ["bp/pure"]               = "f5",   // leaf-pure → pure function
            ["bp/flow"]               = "f6",   // diamond → flow control
            ["bp/macro"]              = "f7",   // cube → macro
            ["bp/comment"]            = "f8",   // chat-bubble → comment node
            ["bp/cast"]               = "f9",   // wand → type cast

            // ── Status / diagnostic ──────────────────────────────────────────────
            ["status/error"]          = "g1",   // error badge
            ["status/warning"]        = "g2",   // warning badge
            ["status/info"]           = "g3",   // information badge
            ["status/ok"]             = "g4",   // tick badge
            ["status/running"]        = "g5",   // spinner/running

            // ── Debug controls (§5.1) ───────────────────────────────────────
            ["debug/continue"]        = "a2",   // play / continue
            ["debug/step_back"]       = "a3",   // rewind / step back
            ["debug/step_over"]       = "a4",   // step over
            ["debug/step_into"]       = "a5",   // step into
            ["debug/step_out"]        = "a6",   // step out

            // ── Asset kind icons (§5.1, §5.2) ──────────────────────────────
            ["asset/scenario"]        = "b1",   // world → scenario
            ["asset/blueprint"]       = "b2",   // blueprint
            ["asset/btree"]           = "c10",  // branch → behavior tree
            ["asset/hsm"]             = "c11",  // state machine
            ["asset/blackboard"]      = "c12",  // blackboard
            ["asset/utility"]         = "b8",   // utility

            // ── Generic / browser (§5.1) ────────────────────────────────────
            ["browser/open"]          = "c8",   // folder → open browser
            ["asset/new"]             = "b9",   // new document
            ["folder"]                = "c8",   // folder (closed)
            ["folder_open"]           = "a1",   // folder open

            // ── Perspective toolbar icons ────────────────────────────────────
            ["perspective/editor"]    = "a1",   // house → Editor (home/main perspective)

            // ── Shell commands (toolbar) ──────────────────────────────────────
            ["shell/save"]            = "g9",   // disk / floppy — save
            ["shell/saveAs"]          = "h8",   // disk variant — save as
            ["shell/saveAll"]         = "i1",   // disk shape — save all

            // ── Build / reload (toolbar) ──────────────────────────────────────
            ["build/compile"]         = "b4",   // lightning → compile / quick reload
            ["build/rebuild"]         = "d4",   // refresh → full rebuild
        };

    /// <summary>
    /// Creates a <see cref="SilkIconProvider"/> using the default key→cell map.
    /// </summary>
    /// <param name="atlas">Engine icon atlas (pre-loaded; no GPU calls made here).</param>
    public SilkIconProvider(IconAtlas atlas)
        : this(atlas, DefaultCellMap) { }

    /// <summary>
    /// Creates a <see cref="SilkIconProvider"/> with a custom key→cell map.
    /// Allows hosts to override individual mappings or extend the table.
    /// </summary>
    public SilkIconProvider(IconAtlas atlas, IReadOnlyDictionary<string, string> cellMap)
    {
        _atlas    = atlas;
        _keyToCell = cellMap;
    }

    /// <inheritdoc/>
    public bool TryGet(string key, out IconHandle handle)
    {
        if (key is not null && _keyToCell.TryGetValue(key, out var cell))
        {
            var (uv0, uv1) = _atlas.GetUvCoordinates(cell);
            handle = new IconHandle(
                textureId: _atlas.TextureId,
                width:  (uint)_atlas.IconSizeVec.X,
                height: (uint)_atlas.IconSizeVec.Y,
                uv0:    uv0,
                uv1:    uv1);
            return true;
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// Returns a read-only snapshot of the key→silk-cell map used by this provider.
    /// Exposed for test introspection.
    /// </summary>
    public IReadOnlyDictionary<string, string> KeyToCellMap => _keyToCell;

    /// <summary>The underlying atlas.</summary>
    public IconAtlas Atlas => _atlas;
}
