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
    // Cells use the famfamfam-silk coordinate notation: letter = row (a=1, b=2, …),
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
            ["bt/sequence"]           = "t31",   // arrow-right → sequential flow
            ["bt/selector"]           = "e7",  // arrow-branch → select
            ["bt/observer_selector"]  = "e8",  // arrow-circle → reactive
            ["bt/parallel"]           = "f7",  // arrow-fork → parallel
            ["bt/root"]               = "v11",   // house → root/entry
            ["bt/composite"]          = "b23",   // folder → composite category

            // ── BTree leaves ────────────────────────────────────────────────────
            ["bt/action"]             = "w13",   // lightning-bolt → action
            ["bt/condition"]          = "a2",   // tick circle → condition/check
            ["bt/wait"]               = "v6",   // clock → wait/delay
            ["bt/subtree"]            = "o6",   // page-code → subtree reference
            ["bt/leaf"]               = "ab1",   // bullet → leaf category

            // ── BTree Blueprint category (I4: composed AiPrimitive actions/conditions) ──
            // Reuses the existing node-graph glyph for the category header (same icon as
            // asset/blueprint) and the existing leaf action/condition glyphs for entries, so
            // Blueprint palette entries are no longer blank while staying action/condition
            // distinguishable.
            ["bt/blueprint"]          = "n15",   // node-graph → blueprint category (same as asset/blueprint)
            ["bt/blueprint_action"]   = "w13",   // lightning-bolt → composed blueprint action (same as bt/action)
            ["bt/blueprint_condition"]= "a2",    // tick circle → composed blueprint condition (same as bt/condition)

            // ── BTree decorator pills ────────────────────────────────────────────
            ["bt/decorator"]          = "ad6",   // tag → decorator category
            ["bt/inverter"]           = "g8",   // exclamation → invert
            ["bt/repeater"]           = "f8",   // refresh → repeat
            ["bt/cooldown"]           = "v6",   // hourglass → cooldown
            ["bt/force_success"]      = "a2",   // tick → force success
            ["bt/force_failure"]      = "i14",   // cross → force failure
            ["bt/until_success"]      = "ae13",   // arrow-circle-ok → until success
            ["bt/until_failure"]      = "ae14",   // arrow-circle-error → until failure

            // ── HSM states ───────────────────────────────────────────────────────
            ["hsm/state_simple"]      = "n8",   // circle → simple state
            ["hsm/state_composite"]   = "b23",   // layers → composite state
            ["hsm/state_parallel"]    = "p29",   // parallel-lines → parallel state
            ["hsm/state_final"]       = "e30",   // stop-disc → final state
            ["hsm/state_history"]     = "ae12",   // clock-history → history
            ["hsm/state_deep_history"]= "ae13",   // clock-double → deep history
            ["hsm/transition"]        = "d8",   // arrow-right → transition
            ["hsm/initial"]           = "s32",   // dot → initial pseudostate

            // ── Blueprint node categories ────────────────────────────────────────
            ["bp/event"]              = "w13",   // lightning → event
            ["bp/function"]           = "f26",   // gear → function
            ["bp/variable_get"]       = "e29",   // box-get → variable read
            ["bp/variable_set"]       = "c29",   // box-set → variable write
            ["bp/pure"]               = "m26",   // leaf-pure → pure function
            ["bp/flow"]               = "ad1",   // diamond → flow control
            ["bp/macro"]              = "e11",   // cube → macro
            ["bp/comment"]            = "q7",   // chat-bubble → comment node
            ["bp/cast"]               = "m32",   // wand → type cast

            // ── Status / diagnostic ──────────────────────────────────────────────
            ["status/error"]          = "v13",   // error badge
            ["status/warning"]        = "l7",   // warning badge
            ["status/info"]           = "c23",   // information badge
            ["status/ok"]             = "af12",   // tick badge
            ["status/running"]        = "j14",   // spinner/running

            // ── Debug controls (§5.1) ───────────────────────────────────────
            ["debug/continue"]        = "d8",   // play / continue
            ["debug/step_back"]       = "g5",   // rewind / step back
            ["debug/step_over"]       = "f8",   // step over
            ["debug/step_into"]       = "g1",   // step into
            ["debug/step_out"]        = "h4",   // step out

            // ── Asset kind icons (§5.1, §5.2) ──────────────────────────────
            ["asset/scenario"]        = "ab15",   // world → scenario
            ["asset/blueprint"]       = "n15",   // blueprint
            ["asset/btree"]           = "o6",  // branch → behavior tree
            ["asset/hsm"]             = "p5",  // state machine
            ["asset/blackboard"]      = "w31",  // blackboard
            ["asset/utility"]         = "ad32",   // utility

            // ── Generic / browser (§5.1) ────────────────────────────────────
            ["browser/open"]          = "d4",   // folder → open browser
            ["asset/new"]             = "b9",   // new document
            ["folder"]                = "u3",   // folder (closed)
            ["folder_open"]           = "u3",   // folder open

            // ── Perspective toolbar icons ────────────────────────────────────
            ["perspective/editor"]    = "v11",   // house → Editor (home/main perspective)

            // ── Shell commands (toolbar) ──────────────────────────────────────
            ["shell/save"]            = "m19",   // disk / floppy — save
            ["shell/saveAs"]          = "m19",   // disk variant — save as
            ["shell/saveAll"]         = "n19",   // disk shape — save all

            // ── Build / reload (toolbar) ──────────────────────────────────────
            ["build/compile"]         = "w13",   // lightning → compile / quick reload
            ["build/rebuild"]         = "b8",   // refresh → full rebuild
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
