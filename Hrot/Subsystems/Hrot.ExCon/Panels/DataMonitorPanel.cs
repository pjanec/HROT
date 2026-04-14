using System.Numerics;
using System.Reflection;
using Hrot.Core.Network;
using Fdp.Toolkit.DER;
using Fdp.Presentation.Utils;
using ImGuiNET;

namespace Hrot.ExCon.Panels;

/// <summary>
/// ExCon Data Monitor panel — shows all DER entities in a left-list and a rich,
/// collapsible descriptor tree on the right (Task 44).
///
/// <para><b>Deprecated.</b> This panel has been superseded by
/// <see cref="Fdp.Presentation.Panels.DerEntityInspectorPanel"/> which
/// lives in the generic toolkit layer and provides live descriptor updates,
/// search, and context-menu customisation.  This class is retained only for
/// reference and will be removed in a future clean-up pass.</para>
///
/// <para>The details sub-pane uses <see cref="ImGuiPropertyTree"/> for a
/// hierarchical Name│Value table, matching the same rendering used by the
/// Event Browser and Entity Inspector in IG / SimHost.</para>
///
/// <para><b>Testing:</b> <see cref="GetEntityListRows"/> and
/// <see cref="GetDescriptorObjects"/> are public static helpers callable without
/// an ImGui context.</para>
/// </summary>
[Obsolete("Use FDP.Toolkit_ImGui.Panels.DerEntityInspectorPanel instead.")]
public sealed class DataMonitorPanel
{
    // ── Per-frame state ───────────────────────────────────────────────────────

    private int _selectedEntityId = PanelConstants.InspectorNoSelection;

    // Cached descriptor objects for the selected entity, rebuilt on selection change.
    private int _cachedEntityId = PanelConstants.InspectorNoSelection;
    private List<(string Name, object Data)> _cachedDescriptors = new();

    // Reflection helpers for generic IDerEntity methods (same approach as InspectorPanel).
    private static readonly MethodInfo s_getDescMethodDef =
        typeof(IDerEntity).GetMethod("GetDescriptor")!;
    private static readonly MethodInfo s_hasDescMethodDef =
        typeof(IDerEntity).GetMethod("HasDescriptor")!;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the Data Monitor panel inside an existing rlImGui context.
    /// </summary>
    /// <param name="logic">ExCon logic providing the DER repository.</param>
    public void Draw(IExConLogic logic)
    {
        if (!ImGui.Begin("ExCon Entity Inspector"))
        {
            ImGui.End();
            return;
        }

        var repo = logic.Repo;

        // ── Sync selection ────────────────────────────────────────────────
        if (_selectedEntityId != _cachedEntityId)
        {
            _cachedEntityId   = _selectedEntityId;
            _cachedDescriptors = BuildDescriptors(
                _selectedEntityId != PanelConstants.InspectorNoSelection
                    ? repo.GetEntity(_selectedEntityId)
                    : null);
        }

        // ── Layout: two resizable columns ─────────────────────────────────
        float width = ImGui.GetContentRegionAvail().X;
        if (!ImGui.BeginTable("##DmLayout", 2,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.End();
            return;
        }

        ImGui.TableSetupColumn("Entities", ImGuiTableColumnFlags.WidthFixed, width * 0.30f);
        ImGui.TableSetupColumn("Details",  ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();

        // ── Left pane: entity list ─────────────────────────────────────────
        ImGui.TableSetColumnIndex(0);
        DrawEntityList(repo);

        // ── Right pane: descriptor tree ───────────────────────────────────
        ImGui.TableSetColumnIndex(1);
        DrawDetails(logic);

        ImGui.EndTable();
        ImGui.End();
    }

    // ── Private rendering ─────────────────────────────────────────────────────

    private void DrawEntityList(IDerRepo repo)
    {
        ImGui.BeginChild("##DmEntityList");

        var entities = repo.GetAllEntities().ToList();
        ImGui.TextDisabled($"{entities.Count} entities");
        ImGui.Separator();

        foreach (var entity in entities)
        {
            bool selected = entity.EntityId == _selectedEntityId;
            string label  = $"Entity {entity.EntityId}##dm{entity.EntityId}";

            if (ImGui.Selectable(label, selected))
                _selectedEntityId = entity.EntityId;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"ID: {entity.EntityId}");
        }

        ImGui.EndChild();
    }

    private void DrawDetails(IExConLogic logic)
    {
        ImGui.BeginChild("##DmDetails");

        if (_selectedEntityId == PanelConstants.InspectorNoSelection)
        {
            ImGui.TextDisabled("Select an entity to view its descriptors.");
            ImGui.EndChild();
            return;
        }

        ImGui.Text($"Entity {_selectedEntityId}");

        // "Edit Overlay" action button — shown for area entities whose overlay is editable.
        var derEntity = logic.Repo.GetEntity(_selectedEntityId);
        if (derEntity != null && derEntity.HasDescriptor<MapOverlayDescriptor>())
        {
            var overlay = derEntity.GetDescriptor<MapOverlayDescriptor>()!;
            if (overlay.IsEditable)
            {
                ImGui.SameLine();
                if (ImGui.Button("Edit Overlay"))
                    logic.StartEditingMode(_selectedEntityId);
            }
        }

        ImGui.Separator();

        if (_cachedDescriptors.Count == 0)
        {
            ImGui.TextDisabled("No descriptors found.");
        }
        else
        {
            for (int i = 0; i < _cachedDescriptors.Count; i++)
            {
                var (name, data) = _cachedDescriptors[i];
                // Push stable ID scope so each "##ptree" table gets a unique ImGui ID.
                ImGui.PushID(i);

                // Each descriptor gets its own collapsible header + property tree.
                if (ImGui.CollapsingHeader(name))
                {
                    ImGui.Indent();
                    ImGuiPropertyTree.Render(data);
                    ImGui.Unindent();
                }

                ImGui.PopID();
            }
        }

        ImGui.EndChild();
    }

    // ── Helpers (public for testing) ──────────────────────────────────────────

    /// <summary>
    /// Returns the list of entity-ID integers visible in the left panel.
    /// Testable without an ImGui context.
    /// </summary>
    public static List<int> GetEntityListRows(IDerRepo repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return repo.GetAllEntities().Select(e => e.EntityId).ToList();
    }

    /// <summary>
    /// Returns all descriptor objects attached to <paramref name="entity"/>.
    /// Testable without an ImGui context.
    /// </summary>
    public static List<(string Name, object Data)> GetDescriptorObjects(IDerEntity? entity)
        => BuildDescriptors(entity);

    private static List<(string Name, object Data)> BuildDescriptors(IDerEntity? entity)
    {
        var result = new List<(string, object)>();
        if (entity == null) return result;

        foreach (var descType in entity.GetAllDescriptorTypes())
        {
            // Reflection does not apply default parameter values automatically;
            // explicitly pass partId=0 matching HasDescriptor<T>(int partId = 0).
            var hasMethod = s_hasDescMethodDef.MakeGenericMethod(descType);
            bool has      = (bool)hasMethod.Invoke(entity, new object[] { 0 })!;
            if (!has) continue;

            var getMethod = s_getDescMethodDef.MakeGenericMethod(descType);
            object? data  = getMethod.Invoke(entity, new object[] { 0 });
            if (data == null) continue;

            result.Add((descType.Name, data));
        }

        return result;
    }
}
