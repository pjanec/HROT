using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Events;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-051</c> (Axis-C <b>E3</b>) — the entity-rename modal, SHARED.</b>
/// 📄 <b><c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c></b> §3 ③. Sits beside
/// <c>AssetRenameModal</c>, which renames an ASSET; this renames an ENTITY.
///
/// <para>🔴 <b>What it replaces: ~35 lines of ImGui welded into <c>EditorSubsystem.DrawUI</c></b>, driven
/// by two host fields *(<c>_openRenameModalThisFrame</c>, <c>_renameTargetNetworkId</c>)* that the drain
/// set. ⭐ CGF had **no rename affordance at all** — its context menu offered Center/Select/Delete/Rotate.
/// ⇒ this is a lift for the editor and NEW capability for CGF, from one implementation.</para>
///
/// <para>⭐⭐ <b>The command drain lives HERE, not in the shared system module, and that is deliberate.</b>
/// The other three E3 behaviours are pure state writes and belong in ECS systems; ⛔ this one must reach
/// an ImGui popup, which only a windowed host has. ⇒ <see cref="Drain"/> is called once per frame from the
/// host's draw pass, so a headless node simply never constructs the modal — ⭐ ruling 49's *"absent, not
/// broken"*, the same rule the E2 picker follows.</para>
///
/// <para>⚠ <b>The commit goes through an injected seam, not through <c>EntityRepository</c> directly.</b>
/// 📐 The editor's inline version called <c>IEditorLogic.CommitPropertyEdit</c>, which publishes an
/// <c>UpdateEntityCommand</c> — ⛔ NOT a direct component write. That distinction is load-bearing on a host
/// that does not own the entity *(the `AX-005b` lesson)*, so the seam is preserved rather than shortcut.</para>
/// </summary>
public sealed class EntityRenameModal
{
    private const string PopupId = "Rename Entity";

    private readonly Action<long, IReadOnlyList<object>> _commitPropertyEdit;

    private long   _targetNetworkId;
    private string _buffer = string.Empty;
    private bool   _openThisFrame;

    /// <param name="commitPropertyEdit">
    /// Commits the edited components for a network id. ⭐ Production wires
    /// <c>IEditorLogic.CommitPropertyEdit</c> on the editor and the equivalent publish on CGF; a rail
    /// injects a recorder. ⛔ Never <see langword="null"/> — a modal that cannot commit is a control that
    /// looks like it works.
    /// </param>
    public EntityRenameModal(Action<long, IReadOnlyList<object>> commitPropertyEdit)
        => _commitPropertyEdit = commitPropertyEdit ?? throw new ArgumentNullException(nameof(commitPropertyEdit));

    /// <summary>True while the modal is waiting to be opened or is open.</summary>
    public bool IsPending => _openThisFrame;

    /// <summary>The network id the pending rename targets; <c>0</c> when nothing is pending.</summary>
    public long TargetNetworkId => _targetNetworkId;

    /// <summary>The current edit buffer — exposed so a headless rail can assert the pre-fill.</summary>
    public string Buffer => _buffer;

    /// <summary>
    /// ⭐⭐ Drains <see cref="OpenRenameDialogCommand"/> and pre-fills the buffer from the entity's current
    /// name. ⭐ <b>Headless-safe: no ImGui here</b>, so a rail can drive it without a context.
    /// </summary>
    public void Drain(EntityRepository world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        foreach (ref readonly var cmd in world.Bus.Read<OpenRenameDialogCommand>())
        {
            _targetNetworkId = cmd.NetworkId;
            _openThisFrame   = true;
            _buffer          = string.Empty;

            // ⭐ BP-508 — the ONE resolver (R-77); the editor's copy was an inline lookup loop.
            var named = NetworkIdResolver.FindEntityByNetworkId(world, cmd.NetworkId);
            if (!named.IsNull && world.HasComponent<EntityInfo>(named))
                _buffer = world.GetComponent<EntityInfo>(named).Name.ToString();
        }
    }

    /// <summary>
    /// Renders the modal. ⚠ Must be called from inside an active ImGui frame; ⛔ a host without one simply
    /// never constructs this type.
    /// </summary>
    public void DrawFrame(EntityRepository world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        if (_openThisFrame)
        {
            ImGui.OpenPopup(PopupId);
            _openThisFrame = false;
        }

        bool isOpen = true;
        if (!ImGui.BeginPopupModal(PopupId, ref isOpen, ImGuiWindowFlags.AlwaysAutoResize)) return;

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();

        ImGui.InputText("New Name", ref _buffer, 64);
        ImGui.Separator();

        bool canSave = !string.IsNullOrWhiteSpace(_buffer);
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button("Save") && canSave)
        {
            Commit(world);
            ImGui.CloseCurrentPopup();
        }
        if (!canSave) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    /// <summary>
    /// ⭐ The commit, separated from the drawing so a headless rail can exercise it.
    /// ⚠ It reads the CURRENT <c>EntityInfo</c> and replaces only the name — ⛔ committing a
    /// <c>default</c> struct would silently clear every other field, which is why the read is not skipped
    /// when the entity is missing.
    /// </summary>
    public void Commit(EntityRepository world)
    {
        EntityInfo updated = default;
        var target = NetworkIdResolver.FindEntityByNetworkId(world, _targetNetworkId);
        if (!target.IsNull && world.HasComponent<EntityInfo>(target))
            updated = world.GetComponent<EntityInfo>(target);

        updated.Name = new FixedString64(_buffer.Trim());
        _commitPropertyEdit(_targetNetworkId, new List<object> { updated });
    }
}
