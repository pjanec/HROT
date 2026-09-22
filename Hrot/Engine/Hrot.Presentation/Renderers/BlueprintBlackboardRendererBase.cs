using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Partitioning;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// ⭐⭐⭐ <b>The inspector renderer for an occurrence store, written once.</b>
/// <c>O3a</c> / task <c>B3</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §17.
///
/// <para>📐 <c>BlueprintBlackboard{1024,4096,16384}Renderer</c> were <b>three byte-identical ~80-line
/// files</b> — verified by normalising the tier number and diffing, all three matched exactly. Only
/// the <c>[ImGuiRenderer]</c> attribute, the type test and two display strings differed, and those
/// are derived here rather than retyped.</para>
///
/// <para>⚠ <b>The attribute still needs a concrete type</b>, so each tier keeps a subclass — but the
/// subclass is now a declaration, not a copy. A fourth tier is four lines.</para>
/// </summary>
public abstract unsafe class BlueprintBlackboardRendererBase<TTier> : IEntityAwareImGuiRenderer
    where TTier : unmanaged
{
    /// <summary>
    /// Set at startup. Required for blueprint id→name resolution.
    ///
    /// <para>⭐⭐ <b>ONE static for the whole family</b>, on <see cref="BlueprintBlackboardRenderers"/>.
    /// ⛔ It used to be one static PER tier class, so both wiring sites — <c>EditorSubsystem</c> and
    /// <c>CgfSubsystem</c> — set it three times each, and a fourth tier meant a silently-unset
    /// renderer on whichever site someone forgot. ⚠ That is the <c>CE-161</c> shape: a per-instance
    /// call is a per-instance chance to forget.</para>
    /// </summary>
    public static BlueprintRegistry? BlueprintRegistryAccessor
    {
        get => BlueprintBlackboardRenderers.Registry;
        set => BlueprintBlackboardRenderers.Registry = value;
    }

    /// <summary>This tier's row in the ladder. ⚠ Resolved once per closed generic — a renderer for a
    /// component the table does not know is a wiring bug, and it says so rather than rendering
    /// something plausible.</summary>
    private static readonly BlueprintTierSpec Spec = FindSpec();

    private static BlueprintTierSpec FindSpec()
    {
        var tiers = BlueprintTierTable.Ascending;
        for (int i = 0; i < tiers.Count; i++)
            if (tiers[i].ComponentType == typeof(TTier))
                return tiers[i];

        throw new InvalidOperationException(
            $"{typeof(TTier).Name} has a renderer but is not in BlueprintTierTable. "
            + "A tier must be added to the table, not only given a component struct.");
    }

    // ---- IImGuiRenderer ----
    public string? GetSummary(object value) => $"Instance Blueprints ({Spec.TotalSize} bytes)";

    public bool RenderValue(object value) => false; // non-entity-aware fallback — delegate to default

    // ---- IEntityAwareImGuiRenderer ----
    public string? GetSummary(IInspectableSession session, Entity entity, object value)
    {
        var registry = BlueprintBlackboardRenderers.Registry;
        if (registry == null || value is not TTier bb)
            return GetSummary(value);

        byte* mem = Base(ref bb);
        int count = BlueprintBlackboardPartitions.GetSlotCount(mem);
        return $"Instance Blueprints ({count} attached)";
    }

    public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;

        var registry = BlueprintBlackboardRenderers.Registry;
        if (registry == null || value is not TTier bb)
            return false;

        byte* mem = Base(ref bb);
        var summaries = BlueprintTierSummary.Read(mem, registry);
        if (summaries.Count == 0)
        {
            ImGui.TextDisabled("No blueprints attached.");
            return true;
        }

        if (ImGui.BeginTable($"##bp{Spec.TotalSize}", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Blueprint");
            ImGui.TableSetupColumn("Version");
            ImGui.TableSetupColumn("Size");
            ImGui.TableSetupColumn("Id");
            ImGui.TableHeadersRow();

            foreach (var s in summaries)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(s.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(s.InstanceVersion.ToString());
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{s.PayloadSize} B");
                ImGui.TableNextColumn(); ImGui.TextDisabled($"0x{s.BlueprintId:X8}");
            }

            ImGui.EndTable();
        }

        // ⭐ P4-③: the ROOT PARAMS section. It goes FIRST of the two typed sections because it is
        //   the behaviour's inputs; working state is what the tick did with them.
        RootParamsProjection.RenderRootParams(session, entity, mem, out doubleClickedPath);

        // Feature A (BATCH-10): render typed WorkingState section after the summary table.
        StatefulWorkingStateProjection.RenderWorkingState(session, entity, mem);

        // ⭐ P4-③ / R-137 — the entity-fact tail came from BrainBlackboardRenderer, which is deleted.
        //   It was never blackboard data (O2 moved it to BrainInterrupts in 2026-09-20); it was just
        //   rendered on that panel. ⛔ Dropping it with the renderer would cost a feature the
        //   retirement has no business costing, so it rides here — the panel a brain entity now has.
        if (session.HasComponent(entity, typeof(BrainInterrupts))
            && session.GetComponent(entity, typeof(BrainInterrupts)) is BrainInterrupts ints)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"ExpectedThreatLevel: {ints.ExpectedThreatLevel}");
            ImGui.TextUnformatted($"Interrupt_MobilityLost: {ints.Interrupt_MobilityLost}");
            ImGui.TextUnformatted($"Interrupt_Reserved: {ints.Interrupt_Reserved}");
        }

        return true; // suppress default byte-dump
    }

    /// <summary>
    /// The unboxed copy's first byte. ⚠ <paramref name="bb"/> is a LOCAL — the value came in boxed
    /// and was copied out by the type test — so the pointer is a stack address valid for the calling
    /// method. That is exactly what <c>byte* mem = bb.Memory;</c> did in the three files this
    /// replaces; the fixed buffer simply cannot be named from a generic method.
    /// </summary>
    private static byte* Base(ref TTier bb)
        => (byte*)Unsafe.AsPointer(ref Unsafe.As<TTier, byte>(ref bb));
}

/// <summary>Non-generic home for the registry statics the whole renderer family shares.</summary>
public static class BlueprintBlackboardRenderers
{
    /// <summary>Set once at startup by whichever host owns the inspector.</summary>
    public static BlueprintRegistry? Registry { get; set; }

    /// <summary>
    /// ⭐ <c>P4</c>-③ — the BEHAVIOUR registry, for the two typed sections this panel renders
    /// (<see cref="RootParamsProjection"/> and <see cref="StatefulWorkingStateProjection"/>).
    ///
    /// <para>⛔⛔ <b>Why it was consolidated here, and it is not tidiness.</b> 📐 Measured
    /// <c>2026-09-22</c>: the accessor existed on <b>two</b> classes, and of the three hosts that
    /// wire the inspector, <c>EditorSubsystem</c> set both while <c>CgfSubsystem</c> and
    /// <c>ReplayBrowserSubsystem</c> set only <c>BrainBlackboardRenderer</c>'s. ⇒ deleting that
    /// renderer would have left two hosts with a registry-less panel and a silently inert typed
    /// section — <b>the silent-default shape</b>, arriving as the by-product of a deletion.
    /// ⭐ One static cannot be half-set.</para>
    /// </summary>
    public static BehaviorRegistry? BehaviorRegistry { get; set; }
}
