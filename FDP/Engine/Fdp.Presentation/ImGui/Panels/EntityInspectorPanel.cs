using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Serialization;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

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
    /// When <c>true</c>, clicking an entity in the inspector list also triggers
    /// <see cref="OnEntitySelected"/> so the host can propagate the selection to
    /// the map or other subsystems.  Defaults to <c>false</c> (one-directional:
    /// map → inspector only).
    /// </summary>
    public bool ChainToMap { get; set; } = false;

    /// <summary>
    /// Raised when the user explicitly clicks an entity in the inspector list
    /// AND <see cref="ChainToMap"/> is <c>true</c>.
    /// The host can use this to drive map selection from the inspector.
    /// </summary>
    public Action<Entity>? OnEntitySelected { get; set; }

    // ── Context menu (Task 47) ────────────────────────────────────────────────

    private readonly List<IEntityContextMenuHandler> _contextMenuHandlers = new();
    private Entity _contextMenuEntity = Entity.Null;

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
        // 1. Top Bar: Statistics & Filter
        ImGuiApi.TextDisabled($"Total Entities: {session.EntityCount}");

        // Select All button — populates selection with all currently visible entities.
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Select All"))
        {
            _selectedEntities.Clear();
            var visibleEntities = GetFilteredEntities(session, _searchFilter);
            foreach (var e in visibleEntities)
                _selectedEntities.Add(e);
            _lastClickedIndex = -1;
            if (_selectedEntities.Count == 1)
                context.SelectedEntity = _selectedEntities.First();
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
                // Keep single-select compat: if exactly one selected, update context.
                if (_selectedEntities.Count == 1)
                {
                    context.SelectedEntity = _selectedEntities.First();
                    if (ChainToMap) OnEntitySelected?.Invoke(context.SelectedEntity.Value);
                }
            }

            // Context Menu logic.
            if (_contextMenuHandlers.Count > 0 && ImGuiApi.IsItemHovered() &&
                ImGuiApi.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _contextMenuEntity = entity;
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
            int selCount = _selectedEntities.Count;

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
            // TODO(ecs-512): remove when SerializeEntity upgraded to BitMask512
            BitMask256 mask = Unsafe.As<BitMask512, BitMask256>(ref mask512);
            var node = Serializer.SerializeEntity(adapter.Repo, entity, resolver, mask);
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
                // TODO(ecs-512): remove when SerializeEntity upgraded to BitMask512
                BitMask256 mask = Unsafe.As<BitMask512, BitMask256>(ref mask512);

                var componentsNode = Serializer.SerializeEntity(adapter.Repo, entity, resolver, mask);
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
        ImGuiApi.BeginChild("EntityDetails_Scroll");

        int selCount = _selectedEntities.Count;

        if (selCount > 1)
        {
            ImGuiApi.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1),
                $"Multiple entities selected ({selCount}) - details not available.");
        }
        else if (selCount == 0 && context.SelectedEntity == null)
        {
            ImGuiApi.TextDisabled("Select an entity to view components.");
        }
        else
        {
            Entity e = selCount == 1 ? _selectedEntities.First() : context.SelectedEntity!.Value;
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

            // ── Chain-to-map toggle (Task 46) ──────────────────────────────
            bool chain = ChainToMap;
            if (chain)
                ImGuiApi.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.65f, 0.20f, 1f));
            if (ImGuiApi.SmallButton(chain ? "🔗 Linked" : "✔ Unlinked"))
                ChainToMap = !chain;
            if (chain)
                ImGuiApi.PopStyleColor();
            if (ImGuiApi.IsItemHovered())
                ImGuiApi.SetTooltip(chain
                    ? "Inspector → Map propagation ON.  Click to disable."
                    : "Inspector → Map propagation OFF.  Click to enable.");

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
