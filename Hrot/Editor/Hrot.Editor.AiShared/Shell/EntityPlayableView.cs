using System;
using NodeEditor.Core.Widgets;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>What the Playable section shows of ONE unit: whether it carries <see cref="Playable"/>, and its side and slot name.</summary>
public readonly record struct PlayableState(bool IsPlayable, ForceId Side, string SlotName);

/// <summary>
/// ⭐ Player Join D-9 (<c>docs/DESIGN_Player_Join.md</c> §5, §9) — the testable half of the <b>Playable</b> section of the Details panel: READ whether a unit is one a Player may deploy into, and
/// SET it (toggle on adds <see cref="Playable"/> with the unit's own side as default, off removes it; side and slot name edit the component). Written to the authoring world the way the
/// Brain tab's settings are (<see cref="BrainSettingRegistry"/>); the component is recorded and saved with the scenario (<c>[DataPolicy(Default)]</c>).
/// </summary>
public static class EntityPlayableModel
{
    public static bool Available(ISimulationView view, Entity unit)
        => view is EntityRepository r && r.IsComponentTypeRegistered<Playable>() && r.IsAlive(unit);

    public static PlayableState Read(ISimulationView view, Entity unit)
    {
        if (!Available(view, unit) || !view.HasComponent<Playable>(unit)) return new PlayableState(false, ForceId.Friend, "");
        var p = view.GetComponentRO<Playable>(unit);
        return new PlayableState(true, p.Side, p.SlotName.ToString());
    }

    /// <summary>Toggles the unit's playability. On: the side defaults to the unit's <c>EntityInfo.ForceId</c> (Friend when it has none) and the slot name to its name. Returns false when not available.</summary>
    public static bool SetPlayable(EntityRepository world, Entity unit, bool playable)
    {
        if (!Available(world, unit)) return false;
        bool has = world.HasComponent<Playable>(unit);
        // ⭐ Edit framework F3 (docs/UX/DESIGN_Edit_Framework.md §3) — the toggle is an owner-side add / remove (then the two attributes), with an
        //   outcome; ⛔ was a local AddComponent / RemoveComponent, which on a station that does not own the unit changed the replica only.
        if (playable && !has)
        {
            var side = ForceId.Friend; var name = "";
            if (world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(unit))
            {
                var info = world.GetComponentRO<EntityInfo>(unit);
                if (info.ForceId is ForceId.Friend or ForceId.Hostile) side = info.ForceId;
                name = info.Name.ToString();
            }
            Edits.Submit(world, EditOp.AddComponent(unit, typeof(Playable), "Make playable"));
            Edits.Submit(world, EditOp.Write(unit, new[] { SideChange(side), SlotChange(name) }, "Set the playable side and slot"));
        }
        else if (!playable && has) Edits.Submit(world, EditOp.RemoveComponent(unit, typeof(Playable), "Make not playable"));
        return true;
    }

    public static bool SetSide(EntityRepository world, Entity unit, ForceId side)
    {
        if (!Available(world, unit) || !world.HasComponent<Playable>(unit)) return false;
        Edits.Submit(world, EditOp.Write(unit, SideChange(side), "Set the playable side"));   // ⭐ F1 — the Playable.Side attribute (B-4)
        return true;
    }

    public static bool SetSlotName(EntityRepository world, Entity unit, string name)
    {
        if (!Available(world, unit) || !world.HasComponent<Playable>(unit)) return false;
        Edits.Submit(world, EditOp.Write(unit, SlotChange(name ?? ""), "Name the playable slot"));   // ⭐ F1 — the Playable.SlotName attribute (B-4)
        return true;
    }

    private static EntityAttributeChange SideChange(ForceId side)
        => new() { AttributeId = AttributeIds.PlayableSide, Value = AttributeValue.FromInt((int)side) };

    private static EntityAttributeChange SlotChange(string name)
        => new() { AttributeId = AttributeIds.PlayableSlotName, Value = AttributeValue.FromString(name) };
}

/// <summary>The Playable view's dump — what a frame showed.</summary>
public sealed record EntityPlayableViewPanelViewModel(string PanelId, string PanelKind, bool HasTarget, int EntityIndex, bool IsPlayable, string Side, string SlotName) : IPanelViewModel
{
    public JsonNode Dump() => PanelDump.Of(this);
}

/// <summary>⭐ Player Join D-9 — the <b>Playable</b> Details view: a toggle, a side and a slot name.</summary>
public sealed class EntityPlayableView : IDetailsViewInstance
{
    private readonly Func<EntityRepository?> _world;
    private string _nameBuffer = "";
    private int _nameFor = -1;

    public EntityPlayableView(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    internal EntityPlayableViewPanelViewModel SimulateDraw(DetailsContext context, string idScope)
    {
        var panelId = $"{idScope}/{EntityPlayableViewDescriptor.ViewId}";
        PanelSnapshot.DeclareInstrumented(panelId);
        bool hasTarget = context.Entities is { Count: 1 };
        var entity = hasTarget ? context.Entities[0] : Entity.Null;
        var st = hasTarget && _world() is { } w ? EntityPlayableModel.Read(w, entity) : new PlayableState(false, ForceId.Friend, "");
        var vm = new EntityPlayableViewPanelViewModel(panelId, EntityPlayableViewDescriptor.ViewId, hasTarget, hasTarget ? entity.Index : -1, st.IsPlayable, st.Side.ToString(), st.SlotName);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    public void Draw(DetailsContext context, string idScope)
    {
        var vm = SimulateDraw(context, idScope);
        if (!vm.HasTarget || _world() is not { } world) return;
        var entity = context.Entities[0];
        bool playable = vm.IsPlayable;
        if (!FormGrid.Begin("playable.form", PlayableLabels)) return;
        FormGrid.Row("Playable");
        if (ImGui.Checkbox("##playable", ref playable)) EntityPlayableModel.SetPlayable(world, entity, playable);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A Player may deploy into this unit.");
        if (playable)
        {
            var st = EntityPlayableModel.Read(world, entity);
            int side = st.Side == ForceId.Hostile ? 1 : 0;
            FormGrid.Row("Side");
            if (ImGui.Combo("##side", ref side, "Friend\0Hostile\0")) EntityPlayableModel.SetSide(world, entity, side == 1 ? ForceId.Hostile : ForceId.Friend);
            if (_nameFor != entity.Index) { _nameBuffer = st.SlotName; _nameFor = entity.Index; }
            FormGrid.Row("Slot name");
            if (ImGui.InputText("##slotname", ref _nameBuffer, 31)) EntityPlayableModel.SetSlotName(world, entity, _nameBuffer);
        }
        FormGrid.End();
    }

    private static readonly string[] PlayableLabels = { "Playable", "Side", "Slot name" };

    public void Dispose() { }
}

/// <summary>The Playable view's descriptor (D-9).</summary>
public static class EntityPlayableViewDescriptor
{
    public const string ViewId = "details.playable";

    /// <summary>Just below Summary (35), above the Editor's Mission / AI / Runtime (40 / 45 / 50).</summary>
    public const int Rank = 36;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var instance = new EntityPlayableView(world);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Playable",
            Rank:      Rank,
            AppliesTo: ctx => DetailsViewPredicates.ExactlyOneEntity(ctx) && world() is { } w && w.IsComponentTypeRegistered<Playable>() && w.IsAlive(ctx.Entities[0]),
            Create:    () => instance);
    }
}
