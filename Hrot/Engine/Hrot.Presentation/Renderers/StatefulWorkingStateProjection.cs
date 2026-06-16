using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Shared helper for the three <c>BlueprintBlackboard{1024,4096,16384}Renderer</c> classes
/// that renders a "Working state (BTree)" typed section after the existing slot-summary table.
///
/// Reads the entity's active <see cref="BehaviorState.ActiveBehaviorHash"/>, resolves the
/// <see cref="BehaviorDefinition"/> from <see cref="BehaviorRegistryAccessor"/>, and for each
/// <see cref="StatefulSlotInfo"/> with a non-null <see cref="StatefulSlotInfo.WorkingStateType"/>,
/// projects the typed working state from the partition slot and renders it via
/// <see cref="ImGuiPropertyTree.Render"/>.
///
/// <para>Robust: any slot that cannot be projected (missing, size 0, Marshal failure) is
/// silently skipped — the renderer never throws inside a frame.</para>
/// </summary>
public static class StatefulWorkingStateProjection
{
    /// <summary>
    /// Set once at startup (e.g., in <c>EditorSubsystem</c>).
    /// Required for behavior lookup by hash.
    /// </summary>
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    // ── Public render entry point ─────────────────────────────────────────────

    /// <summary>
    /// Renders a "Working state (BTree)" section for the entity, reading slot data from
    /// <paramref name="memory"/> (the BlueprintBlackboard* component's raw byte pointer).
    /// Does nothing when no registry is set, the entity has no <see cref="BehaviorState"/>,
    /// the behavior is unregistered, or the behavior has no stateful slots.
    /// </summary>
    public static unsafe void RenderWorkingState(
        IInspectableSession session,
        Entity entity,
        byte* memory)
    {
        var registry = BehaviorRegistryAccessor;
        if (registry == null) return;

        if (!session.HasComponent(entity, typeof(BehaviorState))) return;
        var behaviorStateObj = session.GetComponent(entity, typeof(BehaviorState));
        if (behaviorStateObj is not BehaviorState bs) return;
        if (bs.ActiveBehaviorHash == 0) return;

        if (!registry.TryGetDefinition(bs.ActiveBehaviorHash, out var def)) return;
        if (def.StatefulWorkingSlots is not { Count: > 0 } slots) return;

        bool headerPrinted = false;
        foreach (var s in slots)
        {
            if (s.WorkingStateType == null) continue;

            var result = TryProjectSlot(memory, s, out object? boxed);
            if (result != SlotProjectionResult.Ok) continue;

            // Defer header until we know at least one slot resolved.
            if (!headerPrinted)
            {
                ImGui.Separator();
                ImGui.TextDisabled("Working state (BTree)");
                headerPrinted = true;
            }

            string label = s.NodeLabel ?? $"slot 0x{s.SlotKey:X8}";
            if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGuiPropertyTree.Render(boxed, contextType: s.WorkingStateType, out _);
                ImGui.TreePop();
            }
        }
    }

    // ── Testable decode seam ─────────────────────────────────────────────────

    /// <summary>
    /// Attempts to project (decode) a single stateful slot from <paramref name="memory"/>
    /// into a boxed managed struct of <see cref="StatefulSlotInfo.WorkingStateType"/>.
    /// Exposed as a <c>internal</c> seam so unit tests can verify decode correctness
    /// without going through ImGui.
    /// </summary>
    /// <param name="memory">Pointer to the BlueprintBlackboard* component memory.</param>
    /// <param name="slot">Manifest entry describing the slot to decode.</param>
    /// <param name="boxed">
    /// The decoded struct boxed as <c>object</c>; null when projection fails.
    /// </param>
    /// <returns>A <see cref="SlotProjectionResult"/> indicating success or the failure reason.</returns>
    internal static unsafe SlotProjectionResult TryProjectSlot(
        byte* memory,
        StatefulSlotInfo slot,
        out object? boxed)
    {
        boxed = null;

        if (slot.WorkingStateType == null)
            return SlotProjectionResult.NoType;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, slot.SlotKey, out int payloadOffset))
            return SlotProjectionResult.SlotNotFound;

        if (payloadOffset <= 0)
            return SlotProjectionResult.InvalidOffset;

        try
        {
            boxed = Marshal.PtrToStructure((IntPtr)(memory + payloadOffset), slot.WorkingStateType);
            return boxed != null ? SlotProjectionResult.Ok : SlotProjectionResult.MarshalReturnedNull;
        }
        catch
        {
            // Marshal.PtrToStructure can fail for reference types or unblittable structs.
            // Silently skip — renderer must never throw in a frame.
            return SlotProjectionResult.MarshalException;
        }
    }

    /// <summary>Result codes for <see cref="TryProjectSlot"/>.</summary>
    internal enum SlotProjectionResult
    {
        Ok,
        NoType,
        SlotNotFound,
        InvalidOffset,
        MarshalReturnedNull,
        MarshalException,
    }
}
