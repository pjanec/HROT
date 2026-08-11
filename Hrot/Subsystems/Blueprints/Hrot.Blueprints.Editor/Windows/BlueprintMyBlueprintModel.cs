using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="IMyBlueprintModel"/> that projects the active <see cref="BlueprintAsset"/>
/// into the NodeEdit "My Blueprint" panel.
///
/// <para><b>Real sections</b> (data projected from the asset):</para>
/// <list type="bullet">
///   <item>Graphs — from <see cref="BlueprintAsset.Graphs"/></item>
///   <item>Custom Events — from <see cref="BlueprintAsset.CustomEvents"/></item>
///   <item>Variables — from <see cref="BlueprintAsset.Variables"/> (name/type/category/accent)</item>
/// </list>
///
/// <para><b>Faked/empty sections</b> (no data model yet in v1):</para>
/// <list type="bullet">
///   <item>Functions — always empty list; section header still present in fixed order</item>
///   <item>Macros — the asset's <c>GraphKind.Macro</c> graphs (BP-77/BP-224; was always empty)</item>
/// </list>
///
/// <para>
/// <b>BP-12c — no Event Dispatchers section.</b> BP-09 established that dispatchers are superseded
/// by <c>PublishEvent</c>/<c>EventEntry</c> and deleted the six dispatcher/squad node kinds from the
/// palette; nothing in the editor or the compiler consumes
/// <see cref="BlueprintAsset.EventDispatchers"/>, and no shipped asset declares one. Rather than
/// wire a create path for an abandoned concept, the section is gone. The field stays on the asset so
/// hand-authored JSON still round-trips.
/// </para>
///
/// Fixed section order per D.6.2 spec: Graphs, Functions, Macros, Custom Events, Variables.
///
/// Fire <see cref="Changed"/> when the model should be refreshed (e.g. after
/// <see cref="Retarget"/> is called with a new asset, or when the subscribed
/// <see cref="IEditableAsset.Changed"/> event fires).
/// </summary>
public sealed class BlueprintMyBlueprintModel : IMyBlueprintModel
{
    // ── Section ids (stable string keys) ─────────────────────────────────────

    public const string SectionGraphs      = "graphs";
    public const string SectionFunctions   = "functions";
    public const string SectionMacros      = "macros";
    public const string SectionCustomEvents = "customevents";
    public const string SectionVariables   = "variables";

    // ── Fixed section descriptors (D.6.2 order) ────────────────────────────

    private static readonly IReadOnlyList<MyBlueprintSectionDescriptor> _sections =
        new List<MyBlueprintSectionDescriptor>
        {
            new(SectionGraphs,       "Graphs",           0, null, false, false, null),
            new(SectionFunctions,    "Functions",        1, null, true,  true,  "editor.create-function"),
            new(SectionMacros,       "Macros",           2, null, true,  true,  "editor.create-macro"),
            new(SectionCustomEvents, "Custom Events",    3, null, true,  true,  "editor.create-custom-event"),
            new(SectionVariables,    "Variables",        4, null, true,  true,  "editor.create-variable"),
        };

    // ── State ─────────────────────────────────────────────────────────────────

    private BlueprintAsset? _asset;
    private IEditableAsset? _editableAsset;

    // ── IMyBlueprintModel ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<MyBlueprintSectionDescriptor> Sections => _sections;

    /// <inheritdoc/>
    public event System.Action? Changed;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch the model to project a different asset (or null to show nothing).
    /// Unsubscribes from the previous asset's <c>Changed</c> event and subscribes
    /// to the new one.  Fires <see cref="Changed"/> after retargeting.
    /// </summary>
    public void Retarget(IEditableAsset? editableAsset, BlueprintAsset? blueprintAsset)
    {
        // Unsubscribe old
        if (_editableAsset != null)
            _editableAsset.Changed -= OnAssetChanged;

        _editableAsset = editableAsset;
        _asset         = blueprintAsset;

        // Subscribe new
        if (_editableAsset != null)
            _editableAsset.Changed += OnAssetChanged;

        Changed?.Invoke();
    }

    /// <summary>
    /// Returns the items for the requested section, projected from the current asset.
    /// Returns an empty list when there is no active asset.
    /// </summary>
    public IReadOnlyList<MyBlueprintItem> GetItems(string sectionId)
    {
        if (_asset == null)
            return Array.Empty<MyBlueprintItem>();

        return sectionId switch
        {
            // BP-224: the three graph sections are a three-way split on GraphKind, not two
            // booleans. Graphs keeps everything that is not a Function and not a Macro — Event
            // bodies and Construction.
            SectionGraphs       => BuildGraphItems(SectionGraphs,    g => g.Kind is not (GraphKind.Function or GraphKind.Macro)),
            // BP-24: real — lists the asset's Function graphs (was faked/empty while nothing
            // could create one and the canvas could not switch to one anyway).
            SectionFunctions    => BuildGraphItems(SectionFunctions, g => g.Kind == GraphKind.Function),
            // BP-77 / BP-224: real, now that collapse and editor.create-macro both produce macros.
            SectionMacros       => BuildGraphItems(SectionMacros,    g => g.Kind == GraphKind.Macro),
            SectionCustomEvents => BuildCustomEventItems(),
            SectionVariables    => BuildVariableItems(),
            _                   => Array.Empty<MyBlueprintItem>(),
        };
    }

    // ── Private builders ──────────────────────────────────────────────────────

    /// <summary>
    /// Graph rows, split Unreal-style: Functions carries the Function graphs, Macros the Macro
    /// graphs, and Graphs everything else — Event bodies and Construction. All three share the
    /// <c>graph:{id}</c> item-id form, which <c>editor.go-to-graph</c> resolves on double-click.
    ///
    /// <para>
    /// ⚠ <b>BP-224.</b> This took a <c>bool functionGraphs</c> and filtered
    /// <c>(g.Kind == GraphKind.Function) != functionGraphs</c> — so the Graphs section kept every
    /// graph that was not a Function, <b>including Macros</b>, while the Macros section was
    /// hardcoded empty. A boolean cannot express a three-way choice, and the moment collapse
    /// shipped (Batches 33-34) the third kind started appearing in ordinary assets and landing in
    /// the wrong section. The predicate is passed in so adding a fourth kind cannot silently fall
    /// into a catch-all again.
    /// </para>
    /// </summary>
    /// <summary>
    /// BP-127 — whether <c>BlueprintDocumentFactory.RenameItem</c> would accept a rename for this
    /// graph. ⚠ Kept in agreement with that method deliberately: offering a Rename menu item that
    /// silently does nothing is worse than not offering it, and an Event graph paired with a
    /// declaration is exactly that case.
    /// </summary>
    private bool IsRenamableGraph(Graph graph)
        => graph.Kind != GraphKind.Event
           || !_asset!.CustomEvents.Any(e =>
                  string.Equals(e.Name, graph.Name, StringComparison.Ordinal));

    private IReadOnlyList<MyBlueprintItem> BuildGraphItems(string sectionId, Func<Graph, bool> belongsHere)
    {
        var result = new List<MyBlueprintItem>(_asset!.Graphs.Count);
        foreach (var g in _asset.Graphs)
        {
            if (!belongsHere(g)) continue;
            result.Add(new MyBlueprintItem(
                ItemId:       $"graph:{g.Id}",
                SectionId:    sectionId,
                DisplayName:  g.Name,
                CategoryPath: null,
                IconKey:      null,
                BadgeText:    null,
                AccentColor:  null,
                Children:     null,
                // BP-127: graphs are renamable from here -- this is where Unreal puts rename, and it
                // is why the item never needed the empty-canvas Details panel it was blocked on.
                // ⚠ An Event graph PAIRED with a declaration is not: the pairing is by name, so
                // renaming that graph alone desyncs it into a BP1407. Rename the event instead.
                IsRenamable:  IsRenamableGraph(g),
                IsDeletable:  false,
                IsHostDefined: true,
                // BP-207: the rows look like buttons but open on DOUBLE-click. ⭐ Unreal uses the same
                // gesture (single-click selects, double-click opens), so the defect is the missing
                // AFFORDANCE, not the gesture -- changing the gesture would break parity to fix a
                // discoverability problem. Third instance of this pattern (BP-75, BP-90).
                Tooltip:      $"Double-click to open '{g.Name}'."));
        }
        return result;
    }

    private IReadOnlyList<MyBlueprintItem> BuildCustomEventItems()
    {
        var result = new List<MyBlueprintItem>(_asset!.CustomEvents.Count);
        foreach (var evt in _asset.CustomEvents)
        {
            result.Add(new MyBlueprintItem(
                ItemId:       $"evt:{evt.Id}",
                SectionId:    SectionCustomEvents,
                DisplayName:  evt.Name,
                CategoryPath: null,
                IconKey:      null,
                BadgeText:    null,
                AccentColor:  null,
                Children:     null,
                IsRenamable:  true,
                IsDeletable:  true,
                IsHostDefined: false,
                Tooltip:      null));
        }
        return result;
    }

    private IReadOnlyList<MyBlueprintItem> BuildVariableItems()
    {
        var result = new List<MyBlueprintItem>(_asset!.Variables.Count);
        foreach (var v in _asset.Variables)
        {
            var accent = GetVariableAccentColor(v.Type?.TypeId ?? "");
            // FC-2/LV-4: a fixed-list variable shows its capacity as a "[N]" badge so lists are
            // recognizable at a glance in the My Blueprint tree.
            var badge = v.Type is { Capacity: > 0 } ? $"[{v.Type.Capacity}]" : null;
            result.Add(new MyBlueprintItem(
                ItemId:       $"var:{v.Id}",
                SectionId:    SectionVariables,
                DisplayName:  v.Name,
                CategoryPath: v.Category,
                IconKey:      null,
                BadgeText:    badge,
                AccentColor:  accent,
                Children:     null,
                IsRenamable:  true,
                IsDeletable:  true,
                IsHostDefined: false,
                Tooltip:      v.Tooltip));
        }
        return result;
    }

    /// <summary>
    /// Returns the accent color for a variable using the same palette as
    /// <see cref="BlueprintTypeSystem"/> so wire colors match panel dots.
    /// </summary>
    private static Vector4? GetVariableAccentColor(string typeId)
    {
        // Reuse the BlueprintTypeSystem palette directly.
        var color = BlueprintTypeSystem.GetAccentColorForTypeId(typeId);
        return color;
    }

    private void OnAssetChanged() => Changed?.Invoke();
}
