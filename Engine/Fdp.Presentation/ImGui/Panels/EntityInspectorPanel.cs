using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
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
    private readonly ComponentReflector _reflector = new();

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
        ImGuiApi.SameLine();
        ImGuiApi.InputTextWithHint("##search", "Search ID...", ref _searchFilter, 20);
        
        if (context.SelectedEntity != null)
        {
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("Copy JSON"))
            {
                var json = EntityJsonDumper.Dump(session, context.SelectedEntity.Value);
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
        int filterId = -1;
        
        if (hasFilter && int.TryParse(searchFilter, out int parsedId))
        {
            filterId = parsedId;
        }

        foreach (var entity in entities)
        {
            if (hasFilter)
            {
                if (filterId != -1 && entity.Index != filterId) continue;
            }
            else
            {
                if (count >= limit) break;
            }

            count++;
            results.Add(entity);
        }
        
        return results;
    }

    private void DrawEntityList(IInspectableSession session, IInspectorContext context)
    {
        ImGuiApi.BeginChild("EntityList_Scroll");
        
        var entities = GetFilteredEntities(session, _searchFilter);
        int count = 0;
        
        foreach (var entity in entities)
        {
            count++;
            string label = $"Entity {entity.Index} (v{entity.Generation})";
            bool isSelected = context.SelectedEntity == entity;
            
            if (ImGuiApi.Selectable(label, isSelected))
            {
                context.SelectedEntity = entity;
                if (ChainToMap)
                    OnEntitySelected?.Invoke(entity);
            }

            // Right-click context menu (Task 47).
            if (_contextMenuHandlers.Count > 0 && ImGuiApi.IsItemHovered() &&
                ImGuiApi.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _contextMenuEntity = entity;
                ImGuiApi.OpenPopup("##EntityCtxMenu");
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
            for (int i = 0; i < _contextMenuHandlers.Count; i++)
            {
                if (i > 0) builder.AddSeparator();
                _contextMenuHandlers[i].PopulateMenu(_contextMenuEntity, builder);
            }
            ImGuiApi.EndPopup();
        }
        
        ImGuiApi.EndChild();
    }

    private void DrawEntityDetails(IInspectableSession session, IInspectorContext context)
    {
        ImGuiApi.BeginChild("EntityDetails_Scroll");

        if (context.SelectedEntity == null)
        {
            ImGuiApi.TextDisabled("Select an entity to view components.");
        }
        else
        {
            Entity e = context.SelectedEntity.Value;

            ImGuiApi.Text($"ID: {e.Index} | Gen: {e.Generation}");

            if (session.IsReadOnly)
                ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), "[READ-ONLY]");
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

            _reflector.DrawComponents(session, e);
        }
        ImGuiApi.EndChild();
    }
}
