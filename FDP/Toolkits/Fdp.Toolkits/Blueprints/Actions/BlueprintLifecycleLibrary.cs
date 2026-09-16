using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Events;

namespace Fdp.Toolkit.Blueprints.Actions;

// ── DTOs (become data-IN pins) ─────────────────────────────────────────

/// <summary>Params for the AttachInstanceBlueprint action node.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AttachInstanceBlueprintParams
{
    /// <summary>The runtime BlueprintId to attach. Use BlueprintIdHash.Compute(assetId).</summary>
    public int BlueprintId;

    /// <summary>Optional target entity. Defaults to self (0 = target self).</summary>
    public ulong TargetEntityPacked; // 0 means self
}

/// <summary>Params for the RemoveInstanceBlueprint action node.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RemoveInstanceBlueprintParams
{
    public int BlueprintId;
    public ulong TargetEntityPacked;
}

/// <summary>Params for the ReplaceInstanceBlueprint action node.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ReplaceInstanceBlueprintParams
{
    public int OldBlueprintId;
    public int NewBlueprintId;
    public ulong TargetEntityPacked;
}

// ── BlackboardSlots (required by [SharedAiAction] attribute) ───────────
// Each [SharedAiAction] method needs a slot struct whose Params field type
// exactly matches the method's ref DTO param. The BHU_001 analyzer enforces this.

[StructLayout(LayoutKind.Sequential)]
public struct AttachInstanceBlueprintSlot
{
    public AttachInstanceBlueprintParams Params;
}

[StructLayout(LayoutKind.Sequential)]
public struct RemoveInstanceBlueprintSlot
{
    public RemoveInstanceBlueprintParams Params;
}

[StructLayout(LayoutKind.Sequential)]
public struct ReplaceInstanceBlueprintSlot
{
    public ReplaceInstanceBlueprintParams Params;
}

// ── Action library ─────────────────────────────────────────────────────

/// <summary>
/// <c>[SharedAiAction]</c> methods for runtime blueprint lifecycle operations.
/// Each publishes a BSA-301 event to <c>world.Bus</c>; the actual attach/detach
/// happens in the next frame's Input phase via <c>BlueprintEventIngressSystem</c>.
/// </summary>
public static class BlueprintLifecycleLibrary
{
    [SharedAiAction(typeof(AttachInstanceBlueprintSlot), nameof(AttachInstanceBlueprintSlot.Params))]
    public static NodeStatus AttachInstanceBlueprint(
        ref AttachInstanceBlueprintParams dto, Entity self, EntityRepository world)
    {
        // ⭐ Batch 70 — PublishManaged, because the event now carries params JSON (a managed string).
        //   ⚠ This node passes none: its DTO is a blittable action-params struct and cannot hold a
        //   string, so an attach from a behaviour graph gets the blueprint's declared defaults. That
        //   is the designed answer for "a caller with nothing to pass", not a gap.
        world.Bus.PublishManaged(new AttachInstanceBlueprintEvent
        {
            Entity = ResolveTarget(dto.TargetEntityPacked, self),
            BlueprintId = dto.BlueprintId,
        });
        return NodeStatus.Success;
    }

    [SharedAiAction(typeof(RemoveInstanceBlueprintSlot), nameof(RemoveInstanceBlueprintSlot.Params))]
    public static NodeStatus RemoveInstanceBlueprint(
        ref RemoveInstanceBlueprintParams dto, Entity self, EntityRepository world)
    {
        world.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = ResolveTarget(dto.TargetEntityPacked, self),
            BlueprintId = dto.BlueprintId,
        });
        return NodeStatus.Success;
    }

    [SharedAiAction(typeof(ReplaceInstanceBlueprintSlot), nameof(ReplaceInstanceBlueprintSlot.Params))]
    public static NodeStatus ReplaceInstanceBlueprint(
        ref ReplaceInstanceBlueprintParams dto, Entity self, EntityRepository world)
    {
        var target = ResolveTarget(dto.TargetEntityPacked, self);
        world.Bus.PublishManaged(new ReplaceInstanceBlueprintEvent
        {
            Entity = target,
            OldBlueprintId = dto.OldBlueprintId,
            NewBlueprintId = dto.NewBlueprintId,
        });
        return NodeStatus.Success;
    }

    private static Entity ResolveTarget(ulong packed, Entity self)
        => packed == 0 ? self : new Entity(packed);
}
