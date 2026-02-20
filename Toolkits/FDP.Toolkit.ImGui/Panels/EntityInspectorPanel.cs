using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using FDP.Toolkit.ImGui.Utils;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Panels;

/// <summary>
/// Entity inspection panel with entity list and component details.
/// Supports search, selection, and hover detection.
/// </summary>
public class EntityInspectorPanel
{
    private string _searchFilter = "";
    private readonly ComponentReflector _reflector = new();

    /// <summary>
    /// Renders the entity inspector window.
    /// </summary>
    public void Draw(IInspectableSession session, IInspectorContext context)
    {
        if (!ImGuiApi.Begin("Entity Inspector"))
        {
            ImGuiApi.End();
            return;
        }

        // 1. Top Bar: Statistics & Filter
        ImGuiApi.TextDisabled($"Total Entities: {session.EntityCount}");
        ImGuiApi.SameLine();
        ImGuiApi.InputTextWithHint("##search", "Search ID...", ref _searchFilter, 20);
        
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

        ImGuiApi.End();
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
            }
            
            if (ImGuiApi.IsItemHovered())
            {
                context.HoveredEntity = entity;
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
            ImGuiApi.Separator();
            
            if (session.IsReadOnly)
            {
                ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), "[READ-ONLY MODE]");
            }
                
            // Use helper to iterate components (with edit support)
            _reflector.DrawComponents(session, e);
        }
        ImGuiApi.EndChild();
    }
}
