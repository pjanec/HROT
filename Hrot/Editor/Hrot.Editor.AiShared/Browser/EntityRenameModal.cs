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
/// <para>⭐ <b>The commit is an edit-framework Write of the <c>"Name"</c> attribute</b> (CE-4274): ⛔ never a direct component write, which on a
/// host that does not own the entity would rename the replica only *(the `AX-005b` lesson)*.</para>
/// </summary>
public sealed class EntityRenameModal
{
    private const string PopupId = "Rename Entity";

    private readonly Func<EntityRepository, Fdp.Toolkit.Replication.Edits.EditOp, Fdp.Toolkit.Replication.Edits.EditTicket> _submit;

    private int   _targetNetworkId;
    private string _buffer = string.Empty;
    private bool   _openThisFrame;

    /// <param name="submit">
    /// ⭐ CE-4274 (edit framework F1, 📄 docs/UX/DESIGN_Edit_Framework.md §3) — submits the rename as the <c>"Name"</c> attribute write. Production passes
    /// nothing: <c>Edits.Submit</c> (the owner writes it; a non-owner asks the owner, with an outcome). A rail injects a recorder.
    /// ⛔ HISTORY: <c>IEditorLogic.CommitPropertyEdit</c> → <c>UpdateEntityCommand</c>, applied locally with no authority check — on a station that
    /// did not own the unit it renamed the REPLICA and was lost.
    /// </param>
    public EntityRenameModal(Func<EntityRepository, Fdp.Toolkit.Replication.Edits.EditOp, Fdp.Toolkit.Replication.Edits.EditTicket>? submit = null)
        => _submit = submit ?? Fdp.Toolkit.Replication.Edits.Edits.Submit;

    /// <summary>True while the modal is waiting to be opened or is open.</summary>
    public bool IsPending => _openThisFrame;

    /// <summary>The network id the pending rename targets; <c>0</c> when nothing is pending.</summary>
    public int TargetNetworkId => _targetNetworkId;

    /// <summary>The current edit buffer — exposed so a headless rail can assert the pre-fill.</summary>
    public string Buffer => _buffer;

    /// <summary>
    /// ⭐⭐ Drains <see cref="OpenRenameDialogCommand"/> and pre-fills the buffer from the entity's current
    /// name. ⭐ <b>Headless-safe: no ImGui here</b>, so a rail can drive it without a context.
    /// </summary>
    public void Drain(EntityRepository world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        foreach (ref readonly var cmd in world.ReadEvents<OpenRenameDialogCommand>())
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
        var target = NetworkIdResolver.FindEntityByNetworkId(world, _targetNetworkId);
        string name = _buffer.Trim();
        _submit(world, Fdp.Toolkit.Replication.Edits.EditOp.Write(target,
            new Fdp.Toolkit.Replication.Patching.EntityAttributeChange
            {
                AttributeId = Fdp.Toolkit.Replication.Patching.AttributeIds.Name,
                Value = Fdp.Toolkit.Replication.Patching.AttributeValue.FromString(name),
            },
            $"Rename to {name}"));
    }
}
