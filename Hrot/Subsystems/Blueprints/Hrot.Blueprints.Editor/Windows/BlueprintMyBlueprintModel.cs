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
///   <item>
///     Local Variables — from the <b>current graph</b>'s <see cref="Graph.LocalVariables"/> (BP-57).
///     ⭐ The only <b>graph</b>-scoped section: it follows the canvas through the
///     <c>currentGraphId</c> provider rather than the asset.
///   </item>
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
/// Fixed section order per D.6.2 spec: Graphs, Functions, Macros, Custom Events, Variables —
/// then Local Variables (BP-57), appended so the five keep the sort order the spec fixed for them.
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

    /// <summary>
    /// BP-57 — the graph-scoped locals section. ⚠ Deliberately <b>last</b>, immediately below
    /// <see cref="SectionVariables"/>: a local is the graph-scoped narrowing of the same concept, so
    /// it reads as a sub-case of the section above it, and appending keeps every existing section on
    /// the sort order D.6.2 fixed for it.
    /// </summary>
    public const string SectionLocalVariables = "localvariables";

    /// <summary>
    /// The locals section's create command. ⚠ A literal rather than a <c>CommandCatalog</c> constant:
    /// the catalog lives in <c>NodeEditor.Core</c>, and locals are a Blueprint concept no other host
    /// kind has. Adding a member there would move the two NodeEdit gates for a string.
    /// </summary>
    public const string CommandCreateLocalVariable = "editor.create-local-variable";

    /// <summary>
    /// ⭐⭐⭐ <c>C-sections</c> — <b>the asset's INPUTS</b> (<c>DeclarationKind.Parameter</c>).
    ///
    /// <para>
    /// 🔴 <b>Parameters and working state were not shown in My Blueprint AT ALL</b>:
    /// <c>BuildVariableItems()</c> listed only <c>DeclarationKind.Variable</c>, so an AiPrimitive —
    /// 32 of the shipped assets are <c>(Parameter, WorkingState)</c> — presented a designer with an
    /// EMPTY Variables section and no way to see, rename or delete anything it actually declares.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The ruling this implements: a variable's classification is WHERE IT WAS CREATED</b>
    /// (user, <c>2026-08-16</c>; 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §1c). ⛔ So each
    /// section carries its own create command, and NO <c>Role</c>/<c>Scope</c> control is introduced
    /// anywhere — the section IS the control.
    /// </para>
    /// </summary>
    public const string SectionParameters = "parameters";

    /// <summary>⭐ <c>C-sections</c> — the AiPrimitive's working state (<c>DeclarationKind.WorkingState</c>).</summary>
    public const string SectionWorkingState = "workingstate";

    /// <summary>The Inputs section's "+". ⚠ A literal for the same reason as
    /// <see cref="CommandCreateLocalVariable"/>: adding a <c>CommandCatalog</c> member moves the two
    /// NodeEdit gates for a string.</summary>
    public const string CommandCreateParameter = "editor.create-parameter";

    /// <summary>The Working State section's "+".</summary>
    public const string CommandCreateWorkingState = "editor.create-working-state";

    // ── Fixed section descriptors (D.6.2 order) ────────────────────────────

    private static readonly IReadOnlyList<MyBlueprintSectionDescriptor> _sections =
        new List<MyBlueprintSectionDescriptor>
        {
            new(SectionGraphs,       "Graphs",           0, null, false, false, null),
            new(SectionFunctions,    "Functions",        1, null, true,  true,  "editor.create-function"),
            new(SectionMacros,       "Macros",           2, null, true,  true,  "editor.create-macro"),
            new(SectionCustomEvents, "Custom Events",    3, null, true,  true,  "editor.create-custom-event"),
            new(SectionVariables,    "Variables",        4, null, true,  true,  "editor.create-variable"),
            // BP-57. ⭐ CanCreateItems stays TRUE even for a Macro graph — the "+" must not VANISH
            // (Q26-B2). ⚠⚠ What this comment used to add — "the descriptor list is static, so the
            // flag cannot vary per graph" — is NO LONGER TRUE of the REASON: the Sections property
            // projects CreateDisabledReason per read (2026-08-17 user ruling), so the button greys
            // with an explanation on a macro instead of inviting work it will refuse.
            new(SectionLocalVariables, "Local Variables", 5, null, true, true, CommandCreateLocalVariable),
            // ⭐⭐ C-sections. ⚠ APPENDED at 6/7 rather than slotted in above "Variables", for the
            //    same reason BP-57 appended the locals: renumbering would move every existing
            //    section's SortOrder, and the D.6.2 order is asserted position-by-position. ⛔ Reading
            //    order would prefer Inputs FIRST; that is a presentation change worth making on
            //    purpose, with the order test rewritten, and not as a side effect of this item.
            new(SectionParameters,   "Inputs",           6, null, true, true, CommandCreateParameter),
            new(SectionWorkingState, "Working State",    7, null, true, true, CommandCreateWorkingState),
        };

    // ── State ─────────────────────────────────────────────────────────────────

    private BlueprintAsset? _asset;
    private IEditableAsset? _editableAsset;

    /// <summary>
    /// BP-57/BP-72 — the id of the graph the canvas is showing. ⭐ A <b>provider</b>, not a captured
    /// <c>Graph</c>: the locals section must follow the canvas, and a captured graph goes stale on the
    /// first BP-24 switch. Same type and same source as
    /// <c>GraphSignatureWindow.Retarget</c>'s — <c>AiCanvasContext.CurrentGraphId</c>.
    /// </summary>
    private Func<Guid>? _currentGraphId;

    /// <summary>Last id <see cref="SyncCurrentGraph"/> reported. See that method.</summary>
    private Guid _lastSnappedGraphId;

    // ── IMyBlueprintModel ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐⭐ <b>The static list is the TEMPLATE; the reason is projected per read.</b> A section's "+"
    /// can be unusable for reasons that vary with the CURRENT GRAPH, which a
    /// <c>static readonly</c> list cannot express — ⚠ and that staticness is exactly what
    /// <c>SectionLocalVariables</c>'s own comment cites as the reason <c>CanCreateItems</c> could not
    /// vary. ⇒ ⭐ projecting rather than mutating keeps the order and identity fixed (the D.6.2 order
    /// is asserted position-by-position) while letting the REASON follow the canvas.
    /// <para>⛔ Cached on the reason itself, not rebuilt per frame: the panel reads this every frame,
    /// and a fresh list of records each time would allocate for nothing while the canvas sits still.</para>
    /// </remarks>
    public IReadOnlyList<MyBlueprintSectionDescriptor> Sections
    {
        get
        {
            var reason = LocalVariablesCreateDisabledReason();
            if (_projectedSections is null || !string.Equals(_projectedReason, reason, StringComparison.Ordinal))
            {
                _projectedReason   = reason;
                _projectedSections = _sections
                    .Select(d => d.Id == SectionLocalVariables
                        ? d with { CreateDisabledReason = reason }
                        : d)
                    .ToList();
            }
            return _projectedSections;
        }
    }

    private IReadOnlyList<MyBlueprintSectionDescriptor>? _projectedSections;
    private string? _projectedReason;

    /// <summary>
    /// ⭐⭐ Why the Local Variables "+" cannot be used right now, or <c>null</c> when it can.
    ///
    /// <para>📌 <b>User ruling, <c>2026-08-17</c>:</b> greying with an explanatory tooltip beats
    /// letting the click happen and then refusing — <i>"same information value, no false
    /// expectations."</i> ⭐ A <b>refinement</b> of <c>Q26-B2</c> (which forbids the "+" vanishing),
    /// not a reversal: the button stays, and the reason is still taught.</para>
    ///
    /// <para>⛔ The refusal path is NOT removed. A designer can still reach the command by other
    /// routes, and <c>BlueprintLocalVariableSchemaSource</c> remains the authority — this only stops
    /// the button from inviting work that will be refused.</para>
    /// </summary>
    private string? LocalVariablesCreateDisabledReason()
    {
        if (_asset is null) return "Open a blueprint to declare a local variable.";
        var graph = CurrentGraph;
        if (graph is null) return "Open a graph to declare a local variable.";
        // BP1664: a macro is spliced into its caller, so it has no frame of its own to hold a local.
        if (graph.Kind == GraphKind.Macro)
            return $"'{graph.Name}' is a macro — macros are spliced into the caller, so they cannot "
                 + "own local variables. Declare it in the calling graph instead.";
        return null;
    }

    /// <inheritdoc/>
    public event System.Action? Changed;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch the model to project a different asset (or null to show nothing).
    /// Unsubscribes from the previous asset's <c>Changed</c> event and subscribes
    /// to the new one.  Fires <see cref="Changed"/> after retargeting.
    /// </summary>
    /// <param name="currentGraphId">
    /// BP-57/BP-72 — provider for the graph the canvas is showing; pass
    /// <c>AiCanvasContext.CurrentGraphId</c>. Only the Local Variables section reads it; the other
    /// five are asset-scoped. When null that section is simply empty, which is the correct
    /// projection of "no canvas is open".
    /// <para>
    /// ⚠ Refreshed even when the asset instance is unchanged — the provider is per-<i>document</i>,
    /// so the same asset reopened into a new document needs the new one. (Same reasoning, and the
    /// same bug avoided, as <c>GraphSignatureWindow.Retarget</c>.)
    /// </para>
    /// </param>
    public void Retarget(
        IEditableAsset? editableAsset,
        BlueprintAsset? blueprintAsset,
        Func<Guid>?     currentGraphId = null)
    {
        // Unsubscribe old
        if (_editableAsset != null)
            _editableAsset.Changed -= OnAssetChanged;

        _editableAsset  = editableAsset;
        _asset          = blueprintAsset;
        _currentGraphId = currentGraphId;

        // A new document starts unsnapped, so the first SyncCurrentGraph after a retarget reports
        // the switch rather than swallowing it as "same as last time".
        _lastSnappedGraphId = Guid.Empty;

        // Subscribe new
        if (_editableAsset != null)
            _editableAsset.Changed += OnAssetChanged;

        Changed?.Invoke();
    }

    /// <summary>
    /// BP-57/BP-72 — fires <see cref="Changed"/> when the canvas has switched to a different graph
    /// since the last call. Idempotent: repeated calls on the same graph fire nothing.
    ///
    /// <para>
    /// ⭐ <b>Polled, not pushed, and that is deliberate.</b> The switcher
    /// (<c>BlueprintGraphSwitcher</c>) is built per document by <c>BlueprintDocumentFactory</c>; this
    /// model is owned by a perspective-bound window built by the composition root. Neither holds a
    /// reference to the other, and giving the switcher one would be a new document-factory →
    /// perspective-window edge. <c>BP-72</c> met exactly this and chose a provider polled from the
    /// owning window's draw — see <c>GraphSignatureWindow</c>'s snap. Do not invent a second
    /// mechanism.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The section does not depend on this to be correct.</b> <see cref="GetItems"/> resolves the
    /// graph through the provider at call time, and <c>MyBlueprintPanel.DrawSections</c> calls
    /// <c>GetItems</c> every frame — its <c>Changed</c> subscription is an empty lambda whose comment
    /// reads <i>"re-renders automatically next frame"</i>. So the panel follows the canvas because of
    /// the delegate, not because of this event. This exists because <see cref="Changed"/> is part of
    /// <see cref="IMyBlueprintModel"/>'s contract and a consumer that DOES cache would otherwise show
    /// the previous graph's locals.
    /// </para>
    /// </summary>
    /// <returns>True when a switch was observed and <see cref="Changed"/> fired.</returns>
    public bool SyncCurrentGraph()
    {
        var id = _currentGraphId?.Invoke() ?? Guid.Empty;
        if (id == _lastSnappedGraphId) return false;

        _lastSnappedGraphId = id;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The graph the locals section projects — the canvas's, resolved through the provider against
    /// the current asset. Null when there is no provider, no asset, or the id names no graph of it.
    /// </summary>
    public Graph? CurrentGraph
    {
        get
        {
            if (_asset is null || _currentGraphId is null) return null;
            var id = _currentGraphId();
            return _asset.Graphs.FirstOrDefault(g => g.Id == id);
        }
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
            SectionVariables    => BuildDeclarationItems(DeclarationKind.Variable,     SectionVariables),
            SectionParameters   => BuildDeclarationItems(DeclarationKind.Parameter,    SectionParameters),
            SectionWorkingState => BuildDeclarationItems(DeclarationKind.WorkingState, SectionWorkingState),
            // BP-57: the one GRAPH-scoped section. ⭐ Empty rather than absent when the canvas has no
            // graph or the graph has no locals — a section that appears and disappears reads as a
            // broken feature, and BP1664's macro case is a refusal, not a vanishing.
            SectionLocalVariables => BuildLocalVariableItems(),
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

    /// <summary>
    /// ⭐⭐ <c>C-sections</c> — <b>ONE projection over every declaration kind</b>, parameterised by the
    /// kind and the section it lands in.
    ///
    /// <para>
    /// ⛔ This was <c>BuildVariableItems()</c>, hard-wired to <c>DeclarationKind.Variable</c>. ⭐ Three
    /// copies differing only in an enum value is how the three kinds would drift apart in the tree —
    /// which is precisely the defect one level up, where a parameter and a variable already looked
    /// like different concepts because one of them was invisible.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>An empty section stays present.</b> The descriptor list is static, so a section with no
    /// declarations renders EMPTY rather than vanishing — the subtlety <c>SectionLocalVariables</c>
    /// already records: <i>"a section that appears and disappears reads as a broken feature."</i>
    /// </para>
    /// </summary>
    private IReadOnlyList<MyBlueprintItem> BuildDeclarationItems(DeclarationKind kind, string sectionId)
    {
        var result = new List<MyBlueprintItem>(_asset!.Declarations.CountIn(kind));
        foreach (var v in _asset.Declarations.Of(kind))
        {
            var accent = GetVariableAccentColor(v.Type?.TypeId ?? "");
            // FC-2/LV-4: a fixed-list variable shows its capacity as a "[N]" badge so lists are
            // recognizable at a glance in the My Blueprint tree.
            var badge = v.Type is { Capacity: > 0 } ? $"[{v.Type.Capacity}]" : null;
            result.Add(new MyBlueprintItem(
                ItemId:       $"var:{v.Id}",
                SectionId:    sectionId,
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
    /// BP-57 — the current graph's locals, mirroring <see cref="BuildVariableItems"/> row for row so
    /// the two variable kinds look and behave alike in the tree.
    ///
    /// <para>
    /// ⚠ <b>Read from the CURRENT GRAPH, not the asset</b> — that is the whole difference, and the
    /// reason this section needed the provider the other five did not.
    /// </para>
    ///
    /// <para>
    /// 📌 The <c>local:{id}</c> id form is what <c>editor.rename-item</c> / <c>editor.delete-item</c>
    /// dispatch on to route a locals gesture to <c>BlueprintLocalVariableSchemaSource</c> rather than
    /// to the asset-variable path — the declarations live in different lists and have different
    /// delete rules, so one prefix per kind is what keeps them apart.
    /// </para>
    /// </summary>
    private IReadOnlyList<MyBlueprintItem> BuildLocalVariableItems()
    {
        var graph = CurrentGraph;
        if (graph is null) return Array.Empty<MyBlueprintItem>();

        var result = new List<MyBlueprintItem>(graph.LocalVariables.Count);
        foreach (var v in graph.LocalVariables)
        {
            var accent = GetVariableAccentColor(v.Type?.TypeId ?? "");
            var badge  = v.Type is { Capacity: > 0 } ? $"[{v.Type.Capacity}]" : null;
            result.Add(new MyBlueprintItem(
                ItemId:       $"local:{v.Id}",
                SectionId:    SectionLocalVariables,
                DisplayName:  v.Name,
                CategoryPath: v.Category,
                IconKey:      null,
                BadgeText:    badge,
                AccentColor:  accent,
                Children:     null,
                IsRenamable:  true,
                IsDeletable:  true,
                IsHostDefined: false,
                // ⚠ The tooltip carries the scope because the canvas does not: a local `Scratch` and
                // an asset `Scratch` render identically on a node (the badge that would fix THAT is
                // a NodeEditor.Core change, deliberately not in this batch).
                Tooltip:      v.Tooltip ?? $"Local to '{graph.Name}'. Not visible from other graphs."));
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
