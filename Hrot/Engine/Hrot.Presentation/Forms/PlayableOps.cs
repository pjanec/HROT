using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Hrot.Common;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ Player Join D-9 / U-23 part 5 (docs/UX/DESIGN_Details_Forms.md §4.0a) — THE <i>make playable</i> op: on adds <see cref="Playable"/> (the unit's own
/// side and name as defaults), off removes it — the edit framework's owner-side add / remove (F3, docs/UX/DESIGN_Edit_Framework.md §3), with an outcome.
/// ⭐ One implementation: the Unit form's <i>Playable</i> toggle and the (retiring) Playable Details view both call it.
/// </summary>
public static class PlayableOps
{
    public static bool Available(EntityRepository world, Entity unit) => world.IsComponentTypeRegistered<Playable>() && world.IsAlive(unit);

    /// <summary>Toggles the unit's playability. Returns false when not available.</summary>
    public static bool SetPlayable(EntityRepository world, Entity unit, bool playable)
    {
        if (!Available(world, unit)) return false;
        bool has = world.HasComponent<Playable>(unit);
        // ⛔ was a local AddComponent / RemoveComponent, which on a station that does not own the unit changed the replica only.
        if (playable && !has)
        {
            var side = ForceId.Friend; var name = "";
            if (world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(unit))
            {
                var info = world.GetComponentRO<EntityInfo>(unit);
                if (info.ForceId is ForceId.Friend or ForceId.Hostile) side = info.ForceId;
                name = info.Name.ToString();
            }
            Fdp.Toolkit.Replication.Edits.Edits.Submit(world, EditOp.AddComponent(unit, typeof(Playable), "Make playable"));
            Fdp.Toolkit.Replication.Edits.Edits.Submit(world, EditOp.Write(unit, new[] { SideChange(side), SlotChange(name) }, "Set the playable side and slot"));
        }
        else if (!playable && has) Fdp.Toolkit.Replication.Edits.Edits.Submit(world, EditOp.RemoveComponent(unit, typeof(Playable), "Make not playable"));
        return true;
    }

    public static EntityAttributeChange SideChange(ForceId side)
        => new() { AttributeId = AttributeIds.PlayableSide, Value = AttributeValue.FromInt((int)side) };

    public static EntityAttributeChange SlotChange(string name)
        => new() { AttributeId = AttributeIds.PlayableSlotName, Value = AttributeValue.FromString(name) };
}
