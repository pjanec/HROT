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
/// ⭐⭐⭐ <b><c>P4</c>-③ — the inspector's view of a behaviour's ROOT PARAMS, read from the slot
/// that actually holds them.</b>
///
/// <para>🔴 <b>What this replaces, and why it is a FIX rather than a move.</b> <c>BrainBlackboardRenderer</c>
/// projected <c>BrainBlackboard.BehaviorParameters</c>. <c>P3</c> moved the params into the root
/// params occurrence slot and cut the blackboard write, but <b>left the component ATTACHED</b>
/// (<c>BehaviorTkbTranslator</c> still adds an empty one). ⇒ the renderer went on projecting a
/// region nothing had filled since <c>P3</c> and drew a struct of <b>zeros</b> — no exception, no
/// null, just confident wrong numbers. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §30.22
/// (<c>CE-312</c>), the fourth instance of the dead-storage pattern.</para>
///
/// <para>⭐⭐ <b>Why it lives beside <see cref="StatefulWorkingStateProjection"/> and not on its own
/// component renderer.</b> The two sections answer the same question about one store — <i>"what does
/// this entity's occupied memory MEAN"</i> — and both need the tier component's base pointer, which
/// only the tier renderer has. ⇒ one panel, two sections, no second place to wire up. ⛔ A separate
/// <c>[ImGuiRenderer]</c> would need its own component to hang off, and that component is the one
/// being retired.</para>
///
/// <para>⚠ <b>The slot may legitimately be absent</b> — a behaviour with no params never attaches
/// one. ⭐ That renders NOTHING rather than an empty section, because a params header over no params
/// reads as "this behaviour's params failed to load".</para>
/// </summary>
public static class RootParamsProjection
{
    /// <summary>
    /// Renders a "Behaviour parameters" section for <paramref name="entity"/>, reading from
    /// <paramref name="memory"/> — the tier component's raw base pointer.
    /// </summary>
    /// <param name="doubleClickedPath">
    /// The property path the user double-clicked, or <c>null</c>. ⚠ Rooted at the TIER component
    /// (<c>$.Memory[…]</c>), because that is the component the inspector is showing — the old
    /// renderer rooted it at <c>$.BehaviorParameters</c>, a field that no longer holds anything.
    /// </param>
    public static unsafe void RenderRootParams(
        IInspectableSession session,
        Entity entity,
        byte* memory,
        out string? doubleClickedPath)
    {
        doubleClickedPath = null;

        var registry = BlueprintBlackboardRenderers.BehaviorRegistry;
        if (registry == null) return;

        if (!TryResolve(session, entity, registry, memory, out var def, out int payloadOffset))
            return;

        if (def!.ManagedBlackboardVariables is { Count: > 0 } vars)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Behaviour parameters");
            foreach (var v in vars)
            {
                ImGui.TextUnformatted(v.Name + ":");
                ImGui.SameLine();
                RenderTypedDtoAt(memory, payloadOffset + v.ByteOffset, v.Type, out string? childPath);
                if (childPath != null && doubleClickedPath == null)
                    doubleClickedPath = $"$.Memory[{payloadOffset + v.ByteOffset}]" + childPath[1..];
            }
        }
        else if (def.BlackboardLayoutType != null)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Behaviour parameters");
            RenderTypedDtoAt(memory, payloadOffset, def.BlackboardLayoutType, out string? childPath);
            if (childPath != null)
                doubleClickedPath = $"$.Memory[{payloadOffset}]" + childPath[1..];
        }
        // ⛔ No raw-hex arm. The old renderer had one because it owned the whole panel and had to
        //    show SOMETHING; here the slot table above already reports the region's id and size, and
        //    a second hex dump of the same bytes is noise. A behaviour with no layout metadata is
        //    not a fault to display.
    }

    // ── Resolve seam ─────────────────────────────────────────────────────────
    //
    // Split out so a rail can assert the LOOKUP without an ImGui frame — the same split
    // StatefulWorkingStateProjection.TryProjectSlot makes, and for the same reason.

    /// <summary>
    /// Locates the entity's root params slot inside <paramref name="memory"/>.
    /// Returns <c>false</c> — and does not render — when the entity has no behaviour, the behaviour
    /// is unregistered, or no root params slot is attached in this tier.
    /// </summary>
    internal static unsafe bool TryResolve(
        IInspectableSession session,
        Entity entity,
        BehaviorRegistry registry,
        byte* memory,
        out BehaviorDefinition? def,
        out int payloadOffset)
    {
        def = null;
        payloadOffset = 0;

        if (!session.HasComponent(entity, typeof(BehaviorState))) return false;
        if (session.GetComponent(entity, typeof(BehaviorState)) is not BehaviorState bs) return false;
        if (bs.ActiveBehaviorHash == 0) return false;

        if (!registry.TryGetDefinition(bs.ActiveBehaviorHash, out def)) return false;
        if (def == null) return false;

        // ⭐ The PUBLIC form of OccurrenceSlotKey.ComputeRootParamsKey — the one way to name this
        //   slot. ⛔ Never recompute the key here: a second spelling is the shape RootParamsAccess
        //   exists to prevent.
        int key = RootParamsAccess.KeyForBehaviour(bs.ActiveBehaviorHash);
        if (key == 0) return false;

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, key, out payloadOffset))
            return false;

        return payloadOffset > 0;
    }

    // ── The SESSION-shaped accessor ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The entity's root params, copied out, for a reader that has only an
    /// <see cref="IInspectableSession"/>.</b>
    ///
    /// <para>⚠ <b>Why this exists at all.</b> <see cref="BlueprintTierSpec"/> offers a repository axis
    /// and an <c>ISimulationView</c> axis — ⛔ there is no session axis, and adding one would point
    /// <c>Fdp.Toolkits</c> at <c>Fdp.Presentation</c>, the wrong way down the stack. ⇒ the walk lives
    /// here, ONCE, with the inspector readers that need it.</para>
    ///
    /// <para>⛔⛔ <b>Why it COPIES rather than handing back a pointer.</b> A session yields the tier
    /// component BOXED; a pointer into that box is only valid while the caller holds it, which is the
    /// stack-address caveat <c>BlueprintBlackboardRendererBase.Base</c> documents. ⚠ Returning one
    /// across a method boundary would be a dangling read that usually LOOKS fine. ⭐ This is a
    /// UI-rate path — a copy of ≤ a few hundred bytes is the right trade.</para>
    /// </summary>
    /// <returns><c>false</c> — with <paramref name="region"/> empty — when the entity has no
    /// behaviour, no occurrence store, or no root params slot.</returns>
    public static unsafe bool TryCopyRootParams(
        IInspectableSession session,
        Entity entity,
        BehaviorRegistry registry,
        out byte[] region,
        out BehaviorDefinition? def)
    {
        region = Array.Empty<byte>();
        def    = null;

        if (session == null || registry == null) return false;
        if (!session.HasComponent(entity, typeof(BehaviorState))) return false;
        if (session.GetComponent(entity, typeof(BehaviorState)) is not BehaviorState bs) return false;
        if (bs.ActiveBehaviorHash == 0) return false;
        if (!registry.TryGetDefinition(bs.ActiveBehaviorHash, out def) || def == null) return false;

        int key = RootParamsAccess.KeyForBehaviour(bs.ActiveBehaviorHash);
        if (key == 0) return false;

        if (!TryLocate(session, entity, key, out _, out object? boxed, out int offset, out int size))
            return false;

        // ⚠ A local, not the out param: an `out` cannot be captured by the pinning lambda (CS1628).
        var copied = new byte[size];
        if (!TryPin(boxed!, mem =>
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)(mem + offset), copied, 0, size)))
            return false;

        region = copied;
        return true;
    }

    /// <summary>
    /// ⭐⭐ <b>Where the entity's root params SIT — the tier component and the byte offset inside it.</b>
    /// The StructEdit path needs exactly this and no bytes: it binds to live memory rather than to a
    /// copy, so handing it a snapshot would make every edit write into an array nobody reads.
    /// </summary>
    /// <param name="tierComponentType">the occurrence-store component the entity actually carries.</param>
    /// <param name="payloadOffset">the root params slot's offset within that component.</param>
    public static bool TryLocateRootParams(
        IInspectableSession session,
        Entity entity,
        BehaviorRegistry registry,
        out Type? tierComponentType,
        out int payloadOffset,
        out BehaviorDefinition? def)
    {
        tierComponentType = null;
        payloadOffset     = 0;
        def               = null;

        if (session == null || registry == null) return false;
        if (!session.HasComponent(entity, typeof(BehaviorState))) return false;
        if (session.GetComponent(entity, typeof(BehaviorState)) is not BehaviorState bs) return false;
        if (bs.ActiveBehaviorHash == 0) return false;
        if (!registry.TryGetDefinition(bs.ActiveBehaviorHash, out def) || def == null) return false;

        int key = RootParamsAccess.KeyForBehaviour(bs.ActiveBehaviorHash);
        if (key == 0) return false;

        if (!TryLocate(session, entity, key, out tierComponentType, out _, out payloadOffset, out _))
            return false;

        return true;
    }

    // ── The one tier walk ────────────────────────────────────────────────────

    private static unsafe bool TryLocate(
        IInspectableSession session, Entity entity, int key,
        out Type? tierComponentType, out object? boxed, out int payloadOffset, out int payloadSize)
    {
        tierComponentType = null;
        boxed             = null;
        payloadOffset     = 0;
        payloadSize       = 0;

        var tiers = BlueprintTierTable.Ascending;
        for (int i = 0; i < tiers.Count; i++)
        {
            var spec = tiers[i];
            if (!session.HasComponent(entity, spec.ComponentType)) continue;

            object? candidate = session.GetComponent(entity, spec.ComponentType);
            if (candidate == null) continue;

            int offset = 0, size = 0;
            bool found = false;
            bool pinned = TryPin(candidate, mem =>
            {
                if (!BlueprintBlackboardPartitions.TryGetSlotIndex(mem, key, out int slotIndex)) return;
                ref var entry = ref BlueprintBlackboardPartitions.GetSlot(mem, slotIndex);
                offset = entry.PayloadOffset;
                size   = entry.PayloadSize;
                found  = offset > 0 && size > 0 && offset + size <= spec.TotalSize;
            });

            if (!pinned || !found) continue;

            tierComponentType = spec.ComponentType;
            boxed             = candidate;
            payloadOffset     = offset;
            payloadSize       = size;
            return true;
        }

        return false;
    }

    private unsafe delegate void PinnedAction(byte* memory);

    /// <summary>
    /// Pins a boxed tier component and hands its first byte to <paramref name="action"/>.
    /// ⚠ Tier components are <c>fixed byte[]</c> structs — blittable — so a pinned handle is legal.
    /// ⛔ Returns <c>false</c> rather than throwing: an inspector must never throw in a frame, and a
    /// tier that cannot be read is a skip, not a fault.
    /// </summary>
    private static unsafe bool TryPin(object boxed, PinnedAction action)
    {
        System.Runtime.InteropServices.GCHandle handle = default;
        try
        {
            handle = System.Runtime.InteropServices.GCHandle.Alloc(
                boxed, System.Runtime.InteropServices.GCHandleType.Pinned);
            action((byte*)handle.AddrOfPinnedObject());
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    private static unsafe void RenderTypedDtoAt(
        byte* memory, int offset, Type dtoType, out string? doubleClickedPath)
    {
        doubleClickedPath = null;
        object? boxed;
        try
        {
            boxed = Marshal.PtrToStructure((IntPtr)(memory + offset), dtoType);
        }
        catch
        {
            // Marshal can fail on an unblittable type. ⛔ A renderer must never throw in a frame.
            return;
        }
        if (boxed == null) return;
        ImGuiPropertyTree.Render(boxed, contextType: dtoType, out doubleClickedPath);
    }
}
