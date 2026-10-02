using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;
using Fdp.Toolkit.Vis2D.Abstractions;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>⭐ One entity row, projected for the dump. 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c>
/// §Example, U-obs-5. Mirrors <see cref="EntityInspectorPanel.GetEntityDisplayString"/>'s three parts by
/// hand rather than as one formatted string, so a test can assert the network id independently of the
/// label text.</summary>
public sealed record EntityInspectorRowViewModel(string Label, long? NetworkId, string? Name);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="EntityInspectorPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠⚠ <b>The gotcha table forbids reflecting over a model carrying delegates/<c>System.Type</c>.</b>
/// <see cref="ComponentReflector"/> renders each component's fields via runtime reflection over
/// arbitrary component structs — projecting THAT recursively into a dumpable shape would mean
/// reimplementing the reflector as a second pure model-builder, which is out of scope for this sweep.
/// ⇒ ⭐ this VM captures the <b>addressable</b> state — which entities are listed, which is selected,
/// and which component TYPES are attached to it (by name) — and leaves per-field component values to
/// <see cref="ComponentReflector"/>'s own (unconverted) draw. 📌 Same shape of deviation as
/// <c>MessageLogPanelViewModel</c>'s "all tabs, not just the active one" note: a superset of what is
/// addressable, not a full pixel dump.</para>
/// </summary>
public sealed record EntityInspectorPanelViewModel(
    string PanelId,
    string PanelKind,
    int TotalEntityCount,
    string SearchFilter,
    IReadOnlyList<EntityInspectorRowViewModel> Entities,
    int SelectedCount,
    string? SelectedEntityLabel,
    IReadOnlyList<string> SelectedComponentTypeNames) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Entity inspection panel with entity list and component details.
/// Supports search, selection, and hover detection.
/// </summary>
public class EntityInspectorPanel
{
    private string _searchFilter = "";
    private string _componentSearchFilter = "";
    private readonly ComponentReflector _reflector = new();
    private static readonly Vector4 ExConViolet = new Vector4(0.7f, 0.45f, 0.8f, 1f);

    // ── Multi-select state (DD-P3-T02) ────────────────────────────────────────
    internal readonly HashSet<Entity> _selectedEntities = new();
    internal int _lastClickedIndex = -1;

    public IEntityStateExtractionService? ExtractionService { get; set; }

    // ══ UXI-11 S-3 — the panel stops owning a selection ═════════════════════
    // 📄 docs/UX/UX_Feature_Selection.md §2.7.3 rule 4 / §2.7.8.

    /// <summary>
    /// ⭐⭐⭐ <b>The host's GLOBAL selection.</b> When set, <c>_selectedEntities</c> stops being a store
    /// and becomes a PROJECTION of this, refreshed every draw; clicks publish through
    /// <see cref="RequestSelectionChange"/> instead of mutating locally.
    ///
    /// <para>⛔ <b>Leave it null and the panel keeps its own set</b> — that is not a fallback bolted on
    /// for tests, it is the correct behaviour for a host with no global selection (ReplayBrowser
    /// inspects a recording; there is nothing on a map to agree with).</para>
    ///
    /// <para>⚠ <b>A PROJECTION, not a subscription, and the reason is a hazard worth naming.</b>
    /// §2.7.2 draws panels reacting to <c>SelectionChangedNotification</c>. ⛔ An ImGui panel is not a
    /// reliable subscriber: a bus event is readable for exactly one frame, and a panel that is
    /// collapsed, on a hidden tab, or simply not drawn that frame would MISS it and stay stale
    /// forever. ⭐ Re-reading the view each draw cannot miss and cannot drift. 📌 The notification is
    /// still published and still has real consumers — the ones that need an EDGE rather than a
    /// state (§2.7.8).</para>
    /// </summary>
    public ISelectionState? Selection { get; set; }

    /// <summary>
    /// ⭐⭐ <b>How this surface asks for a selection change</b> — §2.7.1's <c>ISelectionRequester</c>,
    /// as a delegate. The host publishes it on its own bus.
    ///
    /// <para>⚠ A delegate rather than an <c>FdpEventBus</c> on purpose: the panel must not know which
    /// world it is looking at, and FDP has no business reaching for a host's bus.</para>
    /// </summary>
    public Action<SelectionChangeRequest>? RequestSelectionChange { get; set; }

    /// <summary>True when this panel defers to a host-owned selection.</summary>
    private bool DefersToHostSelection => Selection != null && RequestSelectionChange != null;

    /// <summary>
    /// Re-reads the host selection into the local view set. ⚠ Called at the top of every draw, so the
    /// panel can never disagree with the map — 📌 it disagreed for months, which is what
    /// <c>UXI-11</c> is about.
    /// </summary>
    internal void ProjectHostSelection()
    {
        if (!DefersToHostSelection) return;
        _selectedEntities.Clear();
        foreach (var e in Selection!.SelectedEntities) _selectedEntities.Add(e);
    }

    private void Request(SelectionChangeMode mode, IReadOnlyList<Entity> entities, string reason)
        => RequestSelectionChange?.Invoke(new SelectionChangeRequest
        {
            Entities = entities,
            Mode     = mode,
            Reason   = reason,
        });

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-11</c> S-4 — RIGHT-CLICK SELECTS.</b> 🔒 §2.3, ruled <c>2026-08-12</c>:
    /// <list type="bullet">
    ///   <item>already selected ⇒ the whole selection SURVIVES and the menu applies to all of it;</item>
    ///   <item>not selected ⇒ the selection becomes just this one and the menu applies to it.</item>
    /// </list>
    /// <para>⭐⭐⭐ <b>THE GESTURE DECIDES THE MENU'S SUBJECT</b> — and that is what dissolves §2.3's
    /// ordering hazard rather than racing it. 🔴 §2.3 warns the selection mutation must land BEFORE the
    /// menu is built, <i>in the same frame</i>, or the menu is wrong exactly once. ⛔ Since S-3e a
    /// selection change is a REQUEST, so it lands NEXT frame and that ordering can no longer be satisfied
    /// by sequencing. ⭐ It does not need to be: <b>which case of §2.3 this click is</b> is knowable NOW,
    /// at the gesture, without reading the store back after the write.</para>
    /// <para>⚠ Extracted from the draw so it can be railed headlessly — the ImGui site only decides
    /// <i>whether</i> a right-click happened.</para>
    /// </summary>
    internal void RightClick(Entity entity)
    {
        _contextMenuEntity      = entity;
        _contextMenuUsesSelection = _selectedEntities.Contains(entity);

        if (_contextMenuUsesSelection) return;

        if (DefersToHostSelection)
            Request(SelectionChangeMode.Replace, new[] { entity }, "Inspector.RightClick");
        else
        {
            _selectedEntities.Clear();
            _selectedEntities.Add(entity);
        }
    }

    /// <summary>
    /// ⭐⭐ How many entities the OPEN context menu is about — <c>1</c> when the right-click landed on an
    /// unselected entity, otherwise the selection's size. ⛔ Never re-derive this from
    /// <c>_selectedEntities.Count</c>: after a deferred right-click the set still holds the OLD selection
    /// for one more frame, which is the "wrong exactly once" bug §2.3 warns about.
    /// </summary>
    internal int ContextMenuSubjectCount => _contextMenuUsesSelection ? _selectedEntities.Count : 1;

    /// <summary>
    /// When set, the "Copy JSON" and "Copy JSON (N items)" buttons use the unified
    /// scenario serialization path instead of the legacy <see cref="EntityJsonDumper"/>.
    /// Wire this up at subsystem initialization alongside the renderer registrations.
    /// </summary>
    public ScenarioSerializer? Serializer { get; set; }

    /// <summary>
    /// Creates an <see cref="EntityInspectorPanel"/>.
    /// </summary>
    /// <param name="extractionService">
    /// Optional service used for multi-entity copy-to-JSON.
    /// When <c>null</c> the "Copy to JSON (N items)" context menu item is omitted.
    /// Existing callers that use the parameterless form remain unaffected.
    /// </param>
    public EntityInspectorPanel(IEntityStateExtractionService? extractionService = null)
    {
        ExtractionService = extractionService;
        _reflector.CopyComponentJsonFunc = (s, e, t, d) => InspectorJsonUtils.BuildComponentJson(s, e, t, d, Serializer);
    }

    /// <summary>
    /// The <see cref="ComponentReflector"/> used to draw component details.
    /// Expose to allow host subsystems to wire up the component editor
    /// (e.g. <c>panel.Reflector.EditWindowManager = ...</c>).
    /// </summary>
    public ComponentReflector Reflector => _reflector;

    private static long? GetNetworkId(IInspectableSession session, Entity entity)
    {
        if (session.HasComponent(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity)))
        {
            var comp = session.GetComponent(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity));
            if (comp is Fdp.Toolkit.Replication.Components.NetworkIdentity ni)
                return ni.Value;
        }
        return null;
    }

    private static string? GetEntityName(IInspectableSession session, Entity entity)
    {
        var infoType = typeof(Fdp.Core.EntityInfo);
        if (session.HasComponent(entity, infoType))
        {
            var comp = session.GetComponent(entity, infoType);
            if (comp is Fdp.Core.EntityInfo info && !info.Name.IsEmpty)
                return info.Name.ToString();
        }
        return null;
    }

    private static string GetEntityDisplayString(IInspectableSession session, Entity entity, out long? netId, out string? name)
    {
        netId = null;
        name = null;
        if (entity == RepositoryAdapter.SingletonEntity)
            return "[Singletons]";

        netId = GetNetworkId(session, entity);
        name = GetEntityName(session, entity);

        string label = $"[{entity.Index}, v{entity.Generation}]";
        if (netId.HasValue) label += $" ({netId.Value})";
        if (!string.IsNullOrEmpty(name)) label += $" {name}";

        return label;
    }

    // ── Chain-to-map toggle (Task 46) ─────────────────────────────────────────

    /// <summary>
    /// Raised when the user clicks an entity in the inspector list <b>on a host with no global
    /// selection</b> (<see cref="Selection"/> is null) — today that is ReplayBrowser.
    ///
    /// <para>⛔⛔ <b><c>ChainToMap</c> IS RETIRED (<c>UXI-11</c> <c>S-3</c>).</b> 🔒 User ruling,
    /// <c>2026-09-10</c>: <i>"inspector selection changes global entity selection state. not just map,
    /// not just editor, everywhere, every host, unified behavior."</i> ⇒ an opt-in toggle for
    /// <i>"should my selection count?"</i> is the question the ruling deletes. 📐 It defaulted to
    /// <c>false</c> and exactly one production host set it true, so on the editor an inspector click
    /// never reached the map at all. ⚠ Its operator toggle button is gone with it.</para>
    /// </summary>
    public Action<Entity>? OnEntitySelected { get; set; }

    // ── Context menu (Task 47) ────────────────────────────────────────────────

    private readonly List<IEntityContextMenuHandler> _contextMenuHandlers = new();
    private Entity _contextMenuEntity = Entity.Null;

    /// <summary>
    /// ⭐⭐⭐ <b>Which case of §2.3 the open menu is:</b> <c>true</c> ⇒ it is about the whole selection
    /// (right-clicked an already-selected entity), <c>false</c> ⇒ about <see cref="_contextMenuEntity"/>
    /// alone. ⚠ Fixed at OPEN time on purpose — see the comment at the right-click site.
    /// </summary>
    private bool _contextMenuUsesSelection;

    /// <summary>
    /// Registers a context-menu handler. The handler's
    /// <see cref="IEntityContextMenuHandler.PopulateMenu"/> is called whenever
    /// the user right-clicks an entity row in the list.
    ///
    /// <para>No handlers registered → no popup shown.</para>
    /// <para>Multiple handlers → items appended in registration order,
    /// separated by a visual divider.</para>
    /// </summary>
    public void RegisterContextMenuHandler(IEntityContextMenuHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuHandlers.Add(handler);
    }

    /// <summary>
    /// Test helper: directly invokes all registered context menu handlers for a given entity.
    /// Not part of the public API — exposed as <c>internal</c> for unit tests.
    /// </summary>
    internal void InvokeContextMenuHandlers(Fdp.Core.Entity entity, IContextMenuBuilder builder)
    {
        for (int i = 0; i < _contextMenuHandlers.Count; i++)
            _contextMenuHandlers[i].PopulateMenu(entity, builder);
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>
    /// ⭐⭐⭐ <b>BUILD — a pure projection of the entity list and current selection. No ImGui.</b>
    /// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example. ⭐ Reuses
    /// <see cref="GetFilteredEntities"/> and <see cref="GetEntityDisplayString"/> — the SAME filter and
    /// label logic <see cref="DrawEntityList"/> uses — so the dump is never a fork of the render logic.
    /// </summary>
    public EntityInspectorPanelViewModel BuildViewModel(IInspectableSession session, IInspectorContext context, string panelId, string panelKind)
    {
        var filtered = GetFilteredEntities(session, _searchFilter);
        var rows = new List<EntityInspectorRowViewModel>(filtered.Count);
        foreach (var entity in filtered)
        {
            string label = GetEntityDisplayString(session, entity, out long? netId, out string? name);
            rows.Add(new EntityInspectorRowViewModel(label, netId, name));
        }

        // ⭐ Mirrors DrawEntityDetails's own arbitration exactly: the multi-select set wins when
        // non-empty; otherwise the single context selection is the fallback (and counts as 1).
        int rawMultiCount = _selectedEntities.Count;
        Entity? soleSelected = rawMultiCount == 1 ? _selectedEntities.First()
            : (rawMultiCount == 0 && context.SelectedEntity != null ? context.SelectedEntity : null);
        int selectedCount = rawMultiCount > 0 ? rawMultiCount : (context.SelectedEntity != null ? 1 : 0);

        string? selectedLabel = null;
        var selectedComponentTypeNames = (IReadOnlyList<string>)System.Array.Empty<string>();
        if (soleSelected != null)
        {
            selectedLabel = GetEntityDisplayString(session, soleSelected.Value, out _, out _);
            var names = new List<string>();
            foreach (var t in session.GetAllComponentTypes())
                if (session.HasComponent(soleSelected.Value, t))
                    names.Add(t.Name);
            names.Sort(System.StringComparer.Ordinal);
            selectedComponentTypeNames = names;
        }

        return new EntityInspectorPanelViewModel(
            panelId, panelKind, session.EntityCount, _searchFilter, rows,
            selectedCount, selectedLabel, selectedComponentTypeNames);
    }

    /// <summary>
    /// Renders the entity inspector window.
    /// </summary>
    /// <param name="session">The ECS session to inspect.</param>
    /// <param name="context">The inspector context (selection state).</param>
    /// <param name="title">Optional window title override. Default: "Entity Inspector".</param>
    public void Draw(IInspectableSession session, IInspectorContext context, string title = "Entity Inspector")
    {
        if (!ImGuiApi.Begin(title)) { ImGuiApi.End(); return; }
        DrawContent(session, context);
        ImGuiApi.End();
    }

    /// <summary>
    /// Renders the inspector content without the outer <c>ImGui.Begin/End</c> wrapper.
    /// Call this from a <see cref="ManagedWindow.DrawClientArea"/> override.
    /// </summary>
    public void DrawContent(IInspectableSession session, IInspectorContext context)
    {
        // ⭐⭐⭐ UXI-11 S-3 — re-read the host selection before anything is drawn from it.
        ProjectHostSelection();

        // 1. Top Bar: Statistics & Filter
        ImGuiApi.TextDisabled($"Total Entities: {session.EntityCount}");

        // Select All button — populates selection with all currently visible entities.
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Select All"))
        {
            var visibleEntities = GetFilteredEntities(session, _searchFilter).ToList();
            _lastClickedIndex = -1;
            if (DefersToHostSelection)
            {
                // ⭐ The request IS the action. ⚠ The list below still shows the OLD selection for one
                //   frame — §2.5 rules that structural, and it is the same frame the map waits.
                Request(SelectionChangeMode.Replace, visibleEntities, "Inspector.SelectAll");
            }
            else
            {
                _selectedEntities.Clear();
                foreach (var e in visibleEntities)
                    _selectedEntities.Add(e);
                if (_selectedEntities.Count == 1)
                    context.SelectedEntity = _selectedEntities.First();
            }
        }
        if (ImGuiApi.IsItemHovered())
            ImGuiApi.SetTooltip("Select all visible entities (respects current search filter)");

        int selCount = _selectedEntities.Count;
        if (selCount > 1)
        {
            if (Serializer != null || ExtractionService != null)
            {
                ImGuiApi.SameLine();
                if (ImGuiApi.Button($"Copy JSON ({selCount} items)"))
                {
                    var json = BuildMultiEntityJson(session, _selectedEntities);
                    ImGuiApi.SetClipboardText(json);
                }
                if (ImGuiApi.IsItemHovered())
                    ImGuiApi.SetTooltip("Copy selected entities to clipboard as a JSON array");
            }
        }
        else if (context.SelectedEntity != null)
        {
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Copy JSON"))
            {
                var json = BuildSingleEntityJson(session, context.SelectedEntity.Value);
                ImGuiApi.SetClipboardText(json);
            }
            if (ImGuiApi.IsItemHovered())
                ImGuiApi.SetTooltip("Dump exact entity state to clipboard as JSON");
        }

        ImGuiApi.Separator();

        // 2. Left Column: Entity List | Right Column: Component Details
        float width = ImGuiApi.GetContentRegionAvail().X;

        if (ImGuiApi.BeginTable("InspectorLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGuiApi.TableSetupColumn("List", ImGuiTableColumnFlags.WidthFixed, width * 0.35f);
            ImGuiApi.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            
            ImGuiApi.TableNextRow();
            
            // --- ENTITY LIST ---
            ImGuiApi.TableSetColumnIndex(0);
            DrawEntityList(session, context);

            // --- COMPONENT DETAILS ---
            ImGuiApi.TableSetColumnIndex(1);
            DrawEntityDetails(session, context);

            ImGuiApi.EndTable();
        }
    }

    
    /// <summary>
    /// Gets filtered entities list. Internal for testing.
    /// </summary>
    internal static List<Entity> GetFilteredEntities(IInspectableSession session, string searchFilter, int limit = 1000)
    {
        var results = new List<Entity>(System.Math.Min(limit, 1000));
        var entities = session.GetEntities();
        int count = 0;

        bool hasFilter = !string.IsNullOrWhiteSpace(searchFilter);

        foreach (var entity in entities)
        {
            bool isSingleton = entity == RepositoryAdapter.SingletonEntity;

            if (!isSingleton)
            {
                if (hasFilter)
                {
                    string displayStr = GetEntityDisplayString(session, entity, out _, out _);
                    if (displayStr.IndexOf(searchFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }
                else
                {
                    if (count >= limit) break;
                }
                count++;
            }

            results.Add(entity);
        }

        return results;
    }

    private void DrawEntityList(IInspectableSession session, IInspectorContext context)
    {
        // Render full-width search bar above the scrolling list
        ImGuiApi.SetNextItemWidth(-float.Epsilon);
        ImGuiApi.InputTextWithHint("##search", "Search...", ref _searchFilter, 40);

        ImGuiApi.BeginChild("EntityList_Scroll");

        var entities = GetFilteredEntities(session, _searchFilter);
        int count = 0;

        bool ctrl  = ImGuiApi.GetIO().KeyCtrl;
        bool shift = ImGuiApi.GetIO().KeyShift;

        for (int vi = 0; vi < entities.Count; vi++)
        {
            var entity = entities[vi];
            count++;
            bool isSingleton = entity == RepositoryAdapter.SingletonEntity;
            GetEntityDisplayString(session, entity, out long? netId, out string? name);
            string baseLabel = isSingleton ? "[Singletons]" : $"[{entity.Index}, v{entity.Generation}]";

            // Single-select backward compat: also highlight from IInspectorContext.
            bool isSelected = _selectedEntities.Contains(entity) || context.SelectedEntity == entity;

            var style = ImGuiApi.GetStyle();
            var drawList = ImGuiApi.GetWindowDrawList();

            // 1. Capture the position BEFORE drawing the selectable.
            Vector2 screenPos = ImGuiApi.GetCursorScreenPos();

            // 2. Draw the Selectable.
            if (ImGuiApi.Selectable($"##sel_{entity.Index}_{entity.Generation}", isSelected))
            {
                HandleRowClick(entities, vi, ctrl, shift);
                // ⭐⭐⭐ UXI-11 S-3 — when the host owns the selection, HandleRowClick already published
                //    the request and the local set is a projection; touching context here would write
                //    a second store. 🔒 Ruling ① (2026-09-10): inspector selection IS the global
                //    selection, on every host — so there is nothing left to "chain".
                if (!DefersToHostSelection && _selectedEntities.Count == 1)
                {
                    context.SelectedEntity = _selectedEntities.First();
                    OnEntitySelected?.Invoke(context.SelectedEntity.Value);
                }
            }

            // ⭐⭐⭐ UXI-11 S-4 — RIGHT-CLICK SELECTS. 🔒 §2.3, ruled 2026-08-12:
            //    · already selected  -> the whole selection SURVIVES, menu applies to all;
            //    · not selected      -> selection becomes just this one, menu applies to it;
            //    (empty space is the canvas menu, which this list has no equivalent of).
            if (_contextMenuHandlers.Count > 0 && ImGuiApi.IsItemHovered() &&
                ImGuiApi.IsMouseClicked(ImGuiMouseButton.Right))
            {
                RightClick(entity);
                ImGuiApi.OpenPopup("##EntityCtxMenu");
            }

            // 3. Draw label text with manual vertical nudge.
            float verticalNudge = 4.0f;
            Vector2 textPos = new Vector2(
                screenPos.X + style.FramePadding.X,
                screenPos.Y + style.FramePadding.Y - verticalNudge
            );

            drawList.AddText(textPos, ImGuiApi.GetColorU32(ImGuiCol.Text), baseLabel);

            float currentX = textPos.X + ImGuiApi.CalcTextSize(baseLabel).X + style.ItemSpacing.X;

            if (netId.HasValue)
            {
                string netIdText = $"({netId.Value})";
                drawList.AddText(new Vector2(currentX, textPos.Y), ImGuiApi.ColorConvertFloat4ToU32(ExConViolet), netIdText);
                currentX += ImGuiApi.CalcTextSize(netIdText).X + style.ItemSpacing.X;
            }

            if (!string.IsNullOrEmpty(name))
            {
                drawList.AddText(new Vector2(currentX, textPos.Y), ImGuiApi.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.8f, 0.9f, 1f)), name);
            }
        }

        bool hasFilter = !string.IsNullOrWhiteSpace(_searchFilter);

        if (count == 0)
        {
            ImGuiApi.TextDisabled(hasFilter ? "No match." : "No entities.");
        }
        else if (count >= 1000 && !hasFilter)
        {
            ImGuiApi.TextDisabled($"... (limit 1000 reached)");
        }

        // Draw the popup (must be called in the same child window as OpenPopup).
        if (_contextMenuHandlers.Count > 0 && !_contextMenuEntity.IsNull &&
            ImGuiApi.BeginPopup("##EntityCtxMenu"))
        {
            var builder = new ContextMenuBuilder();
            // ⚠ The SUBJECT was fixed when the menu opened (§2.3 / S-4), not re-derived here: after a
            //   right-click that changed the selection, _selectedEntities still holds the OLD set this
            //   frame, and branching on its count is exactly the "wrong exactly once" bug.
            int selCount = ContextMenuSubjectCount;

            if (selCount > 1)
            {
                // Multi-select overload.
                for (int i = 0; i < _contextMenuHandlers.Count; i++)
                {
                    if (i > 0) builder.AddSeparator();
                    _contextMenuHandlers[i].PopulateMenu((IReadOnlyCollection<Entity>)_selectedEntities, builder);
                }

                // Add "Copy to JSON (N items)" when extraction service available.
                if (Serializer != null || ExtractionService != null)
                {
                    builder.AddSeparator();
                    builder.AddItem($"Copy to JSON ({selCount} items)", () =>
                    {
                        var json = BuildMultiEntityJson(session, _selectedEntities);
                        ImGuiApi.SetClipboardText(json);
                    });
                }
            }
            else
            {
                // Single-entity overload.
                for (int i = 0; i < _contextMenuHandlers.Count; i++)
                {
                    if (i > 0) builder.AddSeparator();
                    _contextMenuHandlers[i].PopulateMenu(_contextMenuEntity, builder);
                }
            }

            ImGuiApi.EndPopup();
        }

        ImGuiApi.EndChild();
    }

    /// <summary>
    /// Applies multi-select click logic for a row in the entity list.
    /// Exposed as internal for unit testing.
    /// </summary>
    internal void HandleRowClick(List<Entity> viewList, int clickedIndex, bool ctrl, bool shift)
    {
        if (clickedIndex < 0 || clickedIndex >= viewList.Count) return;

        // ⭐⭐⭐ UXI-11 S-3 — when the host owns the selection this method REQUESTS and writes nothing.
        // ⚠ The modifier semantics are unchanged; only who applies them moved. 📌 That is the whole
        //   point of the mode enum: shift-range and ctrl-toggle become Add/Remove/Replace, so the one
        //   writer performs the same three gestures for every surface instead of each re-deriving them.
        if (DefersToHostSelection)
        {
            if (shift && _lastClickedIndex >= 0 && _lastClickedIndex < viewList.Count)
            {
                int lo = Math.Min(_lastClickedIndex, clickedIndex);
                int hi = Math.Max(_lastClickedIndex, clickedIndex);
                var range = new List<Entity>(hi - lo + 1);
                for (int i = lo; i <= hi; i++) range.Add(viewList[i]);
                Request(SelectionChangeMode.Add, range, "Inspector.ShiftClick");
                return;   // ⚠ shift does NOT move the anchor -- same as the local path below
            }

            var clicked = viewList[clickedIndex];
            if (ctrl)
            {
                Request(_selectedEntities.Contains(clicked)
                            ? SelectionChangeMode.Remove
                            : SelectionChangeMode.Add,
                        new[] { clicked }, "Inspector.CtrlClick");
            }
            else
            {
                Request(SelectionChangeMode.Replace, new[] { clicked }, "Inspector.Click");
            }
            _lastClickedIndex = clickedIndex;
            return;
        }

        if (shift && _lastClickedIndex >= 0 && _lastClickedIndex < viewList.Count)
        {
            // Shift+Click: add inclusive range; do NOT update _lastClickedIndex.
            int lo = Math.Min(_lastClickedIndex, clickedIndex);
            int hi = Math.Max(_lastClickedIndex, clickedIndex);
            for (int i = lo; i <= hi; i++)
                _selectedEntities.Add(viewList[i]);
        }
        else if (ctrl)
        {
            // Ctrl+Click: toggle item; update _lastClickedIndex.
            var item = viewList[clickedIndex];
            if (!_selectedEntities.Remove(item))
                _selectedEntities.Add(item);
            _lastClickedIndex = clickedIndex;
        }
        else
        {
            // Plain click: clear selection, add item, update _lastClickedIndex.
            _selectedEntities.Clear();
            _selectedEntities.Add(viewList[clickedIndex]);
            _lastClickedIndex = clickedIndex;
        }
    }

    private string BuildSingleEntityJson(IInspectableSession session, Entity entity)
    {
        // Singletons is a UI pseudo-entity, not a real ECS row in EntityRepository.
        if (entity == RepositoryAdapter.SingletonEntity)
            return EntityJsonDumper.Dump(session, entity);

        if (Serializer != null && session is RepositoryAdapter adapter)
        {
            var resolver = new DiagnosticGuidResolver();
            var mask512  = adapter.Repo.GetComponentMask(entity.Index);
            var snapshotable512 = adapter.Repo.GetSnapshotableMask();
            mask512.BitwiseAnd(snapshotable512);
            var node = Serializer.SerializeEntity(adapter.Repo, entity, resolver, mask512);
            var wrapper = new JsonObject
            {
                ["EntityId"]   = new JsonArray(entity.Index, entity.Generation),
                ["Components"] = node
            };
            string rawJson = wrapper.ToJsonString(FdpJsonOptionsRegistry.Indented);
            return JsonAestheticFormatter.FlattenNumericArrays(rawJson);
        }
        // Fallback to legacy reflection-based dumper when Serializer is not wired.
        return EntityJsonDumper.Dump(session, entity);
    }

    private string BuildMultiEntityJson(IInspectableSession session, IEnumerable<Entity> entities)
    {
        if (Serializer != null && session is RepositoryAdapter adapter)
        {
            var resolver        = new DiagnosticGuidResolver();
            var snapshotable    = adapter.Repo.GetSnapshotableMask();
            var jsonArray       = new JsonArray();

            foreach (var entity in entities)
            {
                // Singletons is a UI pseudo-entity, not a real ECS row in EntityRepository.
                if (entity == RepositoryAdapter.SingletonEntity)
                {
                    var singletonJson = EntityJsonDumper.Dump(session, entity);
                    var singletonNode = JsonNode.Parse(singletonJson);
                    if (singletonNode != null)
                        jsonArray.Add(singletonNode);
                    continue;
                }

                var mask512 = adapter.Repo.GetComponentMask(entity.Index);
                mask512.BitwiseAnd(snapshotable);

                var componentsNode = Serializer.SerializeEntity(adapter.Repo, entity, resolver, mask512);
                var entityWrapper  = new JsonObject
                {
                    ["EntityId"]   = new JsonArray(entity.Index, entity.Generation),
                    ["Components"] = componentsNode
                };
                jsonArray.Add(entityWrapper);
            }

            string rawJson = jsonArray.ToJsonString(FdpJsonOptionsRegistry.Indented);
            return JsonAestheticFormatter.FlattenNumericArrays(rawJson);
        }

        // Fallback to legacy extraction service path.
        if (ExtractionService == null) return "[]";
        var all            = ExtractionService.ExtractEntities(null);
        var matchingIndices = entities.Select(e => e.Index).ToHashSet();
        var filtered       = all.Where(dto => dto.EntityId.Length > 0 && matchingIndices.Contains(dto.EntityId[0])).ToList();
        return JsonSerializer.Serialize(filtered, FdpJsonOptionsRegistry.Indented);
    }

    /// <summary>
    // ClipboardGuidResolver removed: replaced by the shared DiagnosticGuidResolver
    // from Fdp.Toolkit.Diagnostics, which is already imported above.
    /// </summary>

    private void DrawEntityDetails(IInspectableSession session, IInspectorContext context)
    {
        int selCount = _selectedEntities.Count;

        // ── The ARBITRATION half: decide WHICH entity (or that there is none) ──
        //    The rendering half lives in DrawComponentsFor, so a caller that has
        //    already decided the entity (the Scenario Details view) reuses the
        //    renderer without going through this panel's own selection.
        if (selCount > 1)
        {
            ImGuiApi.BeginChild("EntityDetails_Scroll");
            ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1),
                $"Multiple entities selected ({selCount}) - details not available.");
            ImGuiApi.EndChild();
            return;
        }

        if (selCount == 0 && context.SelectedEntity == null)
        {
            ImGuiApi.BeginChild("EntityDetails_Scroll");
            ImGuiApi.TextDisabled("Select an entity to view components.");
            ImGuiApi.EndChild();
            return;
        }

        DrawComponentsFor(session, selCount == 1 ? _selectedEntities.First() : context.SelectedEntity!.Value);
    }

    /// <summary>
    /// Renders the component-details column for one caller-chosen <paramref name="entity"/>,
    /// without the entity list and without consulting this panel's own selection.
    /// </summary>
    /// <remarks>
    /// This is the renderer half of <c>DrawEntityDetails</c>, extracted so a host that already
    /// knows which entity it is about — the Scenario Details panel, whose entity comes from the
    /// World's <c>SelectionState</c> — reuses it rather than growing a second component renderer.
    /// The panel's own multi-select state is deliberately not read here: the caller decided.
    /// </remarks>
    public void DrawComponentsFor(IInspectableSession session, Entity entity)
    {
        ImGuiApi.BeginChild("EntityDetails_Scroll");
        {
            Entity e = entity;
            bool isSingleton = e == RepositoryAdapter.SingletonEntity;

            if (isSingleton)
            {
                ImGuiApi.TextUnformatted("[Singletons]");
            }
            else
            {
                GetEntityDisplayString(session, e, out long? netId, out string? name);
                ImGuiApi.TextUnformatted($"[{e.Index}, v{e.Generation}]");
                if (netId.HasValue)
                {
                    ImGuiApi.SameLine();
                    ImGuiApi.TextColored(ExConViolet, $"({netId.Value})");
                }
                if (!string.IsNullOrEmpty(name))
                {
                    ImGuiApi.SameLine();
                    ImGuiApi.TextColored(new Vector4(0.6f, 0.8f, 0.9f, 1f), name);
                }
            }

            //ImGuiApi.SameLine();
            //if (ImGuiApi.SmallButton("Copy JSON"))
            //{
            //    var json = BuildSingleEntityJson(session, e);
            //    ImGuiApi.SetClipboardText(json);
            //}

            // ⛔ The chain-to-map toggle (Task 46) is GONE -- UXI-11 S-3.
            //    🔒 Ruling ① (user, 2026-09-10): selection is GLOBAL on every host, so there is no
            //    longer a question for the operator to answer. 📐 It defaulted to OFF and exactly one
            //    production host set it on, which is why an inspector click never moved the editor's
            //    map. 📄 UX_Feature_Selection.md §2.6 ruling ① / §2.7.4.
            ImGuiApi.SameLine();
            // ── Expand / Collapse all toolbar ──────────────────────────
            if (ImGuiApi.SmallButton("▶▶ Expand All"))
                _reflector.ForceExpandAll = true;
            ImGuiApi.SameLine();
            if (ImGuiApi.SmallButton("◀◀ Collapse All"))
                _reflector.ForceCollapseAll = true;

            ImGuiApi.Separator();

            // Render full-width component search filter
            ImGuiApi.SetNextItemWidth(-float.Epsilon);
            ImGuiApi.InputTextWithHint("##comp_search", "Search...", ref _componentSearchFilter, 40);
            ImGuiApi.Spacing();

            _reflector.ComponentFilter = _componentSearchFilter;
            _reflector.DrawComponents(session, e);
        }
        ImGuiApi.EndChild();
    }
}
