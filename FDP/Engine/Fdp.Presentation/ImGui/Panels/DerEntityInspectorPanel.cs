using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.DER;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — the whole of what <see cref="DerEntityInspectorPanel"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⚠ <b>This panel has no window host inside <c>Fdp.Presentation</c></b> — its only production
/// caller (measured) is <c>Hrot.ExCon.ExConMock</c>, a non-<c>ManagedWindow</c> root class in the
/// group-6 assembly. ⇒ per the queue's caller-registers rule, <c>BuildViewModel</c> lives here (the
/// panel's own assembly); the <c>DeclareInstrumented</c>/<c>Register</c> call sites are wired at the
/// caller when that group is converted.</para>
/// </summary>
public sealed record DerEntityInspectorPanelViewModel(
    string PanelId,
    string PanelKind,
    int TotalEntityCount,
    string SearchFilter,
    IReadOnlyList<int> EntityIds,
    int SelectedEntityId,
    IReadOnlyList<string> SelectedDescriptorHeaders) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>
/// Generic DER (Dynamic Entity Repository) entity inspector panel.
///
/// <para>Renders all entities from an <see cref="IDerRepo"/> in a left-hand list
/// and shows a live, collapsible descriptor tree on the right for the selected
/// entity.  Descriptor values are read every frame via
/// <see cref="IDerEntity.GetAllRawDescriptors"/> so that the UI always reflects
/// the latest network data without any manual invalidation.</para>
///
/// <para><b>Live updates:</b> Because <see cref="IDerEntity.SetDescriptor{T}"/>
/// boxes each struct into a brand-new heap object, the right-hand tree is
/// automatically up-to-date: <see cref="IDerEntity.GetAllRawDescriptors"/>
/// returns the latest boxed reference each frame and
/// <see cref="ImGuiPropertyTree.Render"/> reads its public fields directly.
/// No string-building or reflection occurs on idle frames (CODE-STANDARDS §4).
/// </para>
///
/// <para><b>Context menus:</b> register one or more
/// <see cref="IDerContextMenuHandler"/> implementations via
/// <see cref="RegisterContextMenuHandler"/>.  Handlers are called in
/// registration order, separated by a visual divider.  If no handlers are
/// registered, right-clicking an entity does nothing.</para>
///
/// <para><b>Multi-part descriptors:</b> descriptors stored with a non-zero
/// <c>partId</c> are shown with the header <c>TypeName[partId]</c>, preserving
/// their identity.</para>
///
/// <para><b>Testing:</b> <see cref="GetEntityListRows"/> and
/// <see cref="InvokeContextMenuHandlers"/> are exposed as
/// <c>internal</c> helpers callable without an active ImGui context.</para>
/// </summary>
public sealed class DerEntityInspectorPanel
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Entity ID value that represents "no entity selected".</summary>
    public const int NoSelection = 0;

    // ── Per-instance state ────────────────────────────────────────────────────

    private int    _selectedEntityId = NoSelection;
    private string _searchFilter     = "";

    // Context-menu support.
    private readonly List<IDerContextMenuHandler> _contextMenuHandlers = new();
    private IDerEntity? _contextMenuEntity;

    // ── Context menu registration ─────────────────────────────────────────────

    /// <summary>
    /// Registers a context-menu handler.  The handler's
    /// <see cref="IDerContextMenuHandler.PopulateMenu"/> is called whenever
    /// the user right-clicks an entity row.
    ///
    /// <para>No handlers registered → no popup shown.</para>
    /// <para>Multiple handlers → items appended in registration order,
    /// separated by a visual divider.</para>
    /// </summary>
    public void RegisterContextMenuHandler(IDerContextMenuHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _contextMenuHandlers.Add(handler);
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the list of entity IDs that would appear in the left-hand entity
    /// list after applying <paramref name="searchFilter"/>.
    /// Testable without an active ImGui context.
    /// </summary>
    public static List<int> GetEntityListRows(IDerRepo repo, string searchFilter = "")
    {
        ArgumentNullException.ThrowIfNull(repo);
        return FilterEntities(repo, searchFilter).Select(e => e.EntityId).ToList();
    }

    /// <summary>
    /// Directly invokes all registered context menu handlers for a given entity.
    /// Not part of the public API — exposed as <c>internal</c> for unit tests.
    /// </summary>
    internal void InvokeContextMenuHandlers(IDerEntity entity, IContextMenuBuilder builder)
    {
        for (int i = 0; i < _contextMenuHandlers.Count; i++)
        {
            if (i > 0) builder.AddSeparator();
            _contextMenuHandlers[i].PopulateMenu(entity, builder);
        }
    }

    // ── Public BUILD entry point (U-obs-5) ───────────────────────────────
    /// <summary>
    /// ⭐⭐⭐ <b>BUILD — a pure projection of the entity list and selected descriptors. No ImGui.</b>
    /// ⭐ Reuses <see cref="FilterEntities"/> — the SAME filter <see cref="DrawEntityList"/> uses.
    /// </summary>
    public DerEntityInspectorPanelViewModel BuildViewModel(IDerRepo repo, string panelId, string panelKind)
    {
        ArgumentNullException.ThrowIfNull(repo);

        var entities = FilterEntities(repo, _searchFilter);
        var ids = entities.Select(e => e.EntityId).ToList();

        var headers = new List<string>();
        if (_selectedEntityId != NoSelection)
        {
            var selected = repo.GetEntity(_selectedEntityId);
            if (selected != null)
            {
                foreach (var (type, partId, _) in selected.GetAllRawDescriptors())
                    headers.Add(partId == 0 ? type.Name : $"{type.Name}[{partId}]");
            }
        }

        return new DerEntityInspectorPanelViewModel(
            panelId, panelKind, repo.GetAllEntities().Count(), _searchFilter, ids, _selectedEntityId, headers);
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the DER entity inspector window via ImGui.
    /// </summary>
    /// <param name="repo">The DER repository to inspect.</param>
    /// <param name="title">
    /// Optional window title override.  Default: <c>"DER Entity Inspector"</c>.
    /// </param>
    public void Draw(IDerRepo repo, string title = "DER Entity Inspector")
    {
        ArgumentNullException.ThrowIfNull(repo);

        if (!ImGuiApi.Begin(title))
        {
            ImGuiApi.End();
            return;
        }

        // Search / filter input above the two-column layout.
        ImGuiApi.InputTextWithHint("##DerSearch", "Filter by ID...", ref _searchFilter, 64);
        ImGuiApi.Separator();

        float width = ImGuiApi.GetContentRegionAvail().X;
        if (ImGuiApi.BeginTable("##DerLayout", 2,
            ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGuiApi.TableSetupColumn("Entities", ImGuiTableColumnFlags.WidthFixed, width * 0.30f);
            ImGuiApi.TableSetupColumn("Details",  ImGuiTableColumnFlags.WidthStretch);
            ImGuiApi.TableNextRow();

            ImGuiApi.TableSetColumnIndex(0);
            DrawEntityList(repo);

            ImGuiApi.TableSetColumnIndex(1);
            DrawDetails(repo);

            ImGuiApi.EndTable();
        }

        ImGuiApi.End();
    }

    // ── Private rendering ─────────────────────────────────────────────────────

    private void DrawEntityList(IDerRepo repo)
    {
        ImGuiApi.BeginChild("##DerEntityList");

        var entities = FilterEntities(repo, _searchFilter);
        ImGuiApi.TextDisabled($"{entities.Count} entities");
        ImGuiApi.Separator();

        foreach (var entity in entities)
        {
            bool   selected = entity.EntityId == _selectedEntityId;
            string label    = $"Entity {entity.EntityId}##der{entity.EntityId}";

            if (ImGuiApi.Selectable(label, selected))
                _selectedEntityId = entity.EntityId;

            // Right-click context menu.
            if (_contextMenuHandlers.Count > 0 &&
                ImGuiApi.IsItemHovered() &&
                ImGuiApi.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _contextMenuEntity = entity;
                ImGuiApi.OpenPopup("##DerEntityCtxMenu");
            }
        }

        // Context menu popup (must be called in same child as OpenPopup).
        if (_contextMenuHandlers.Count > 0 &&
            _contextMenuEntity != null &&
            ImGuiApi.BeginPopup("##DerEntityCtxMenu"))
        {
            var builder = new ContextMenuBuilder();
            InvokeContextMenuHandlers(_contextMenuEntity, builder);
            ImGuiApi.EndPopup();
        }

        ImGuiApi.EndChild();
    }

    private void DrawDetails(IDerRepo repo)
    {
        ImGuiApi.BeginChild("##DerDetails");

        if (_selectedEntityId == NoSelection)
        {
            ImGuiApi.TextDisabled("Select an entity to view its descriptors.");
            ImGuiApi.EndChild();
            return;
        }

        var entity = repo.GetEntity(_selectedEntityId);
        if (entity == null)
        {
            _selectedEntityId = NoSelection;
            ImGuiApi.TextDisabled("Entity no longer exists.");
            ImGuiApi.EndChild();
            return;
        }

        ImGuiApi.Text($"Entity {_selectedEntityId}");
        ImGuiApi.Separator();

        // Read live descriptor data every frame — no cache, no stale values.
        // Each SetDescriptor<T> call boxes the struct into a new object, so
        // GetAllRawDescriptors always returns the latest reference.
        int  i              = 0;
        bool hasDescriptors = false;

        foreach (var (type, partId, data) in entity.GetAllRawDescriptors())
        {
            hasDescriptors = true;

            // Multi-part descriptors display their partId in the header.
            string header = partId == 0 ? type.Name : $"{type.Name}[{partId}]";

            ImGuiApi.PushID(i++);

            if (ImGuiApi.CollapsingHeader(header))
            {
                ImGuiApi.Indent();
                // ImGuiPropertyTree automatically honours custom type renderers
                // registered in ImGuiRendererRegistry.
                ImGuiPropertyTree.Render(data);
                ImGuiApi.Unindent();
            }

            ImGuiApi.PopID();
        }

        if (!hasDescriptors)
            ImGuiApi.TextDisabled("No descriptors found.");

        ImGuiApi.EndChild();
    }

    // ── Entity list helpers ───────────────────────────────────────────────────

    private static List<IDerEntity> FilterEntities(IDerRepo repo, string filter)
    {
        var all = repo.GetAllEntities();

        if (string.IsNullOrWhiteSpace(filter))
            return all.ToList();

        // Numeric filter: show only the entity whose ID matches.
        if (int.TryParse(filter, out int filterId))
            return all.Where(e => e.EntityId == filterId).ToList();

        // Non-numeric filter: no match (IDs are integers only).
        return new List<IDerEntity>();
    }
}
