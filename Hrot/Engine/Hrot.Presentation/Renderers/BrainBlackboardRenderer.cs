using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using ImGuiNET;

namespace Hrot.Presentation.Renderers;

/// <summary>
/// Entity-aware ImGui renderer for <see cref="BrainBlackboard"/>.
/// When the active behavior has a <see cref="BehaviorDefinition.ParamsDtoType"/>,
/// interprets <see cref="BrainBlackboard.BehaviorParameters"/> as that typed struct and renders
/// it via <see cref="ImGuiPropertyTree.Render"/>. Falls back to raw hex display otherwise.
/// </summary>
[ImGuiRenderer(typeof(BrainBlackboard))]
public sealed class BrainBlackboardRenderer : IEntityAwareImGuiRenderer
{
    /// <summary>
    /// Set once at startup (e.g., in CgfSubsystem initialization).
    /// Required for behavior lookup.
    /// </summary>
    public static BehaviorRegistry? BehaviorRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----

    public string? GetSummary(object value) => "Blackboard Memory";

    /// <summary>
    /// Non-entity-aware fallback. Cannot look up behavior without an entity.
    /// Always falls through to default rendering.
    /// </summary>
    public bool RenderValue(object value) => false;

    // ---- IEntityAwareImGuiRenderer ----

    public string? GetSummary(IInspectableSession session, Entity entity, object value)
    {
        string baseSummary = GetSummary(value) ?? "Blackboard Memory";

        var registry = BehaviorRegistryAccessor;
        if (registry == null) return baseSummary;
        if (!session.HasComponent(entity, typeof(BehaviorState))) return baseSummary;

        var behaviorStateObj = session.GetComponent(entity, typeof(BehaviorState));
        if (behaviorStateObj is not BehaviorState ds) return baseSummary;
        if (ds.ActiveBehaviorHash == 0) return baseSummary;

        if (registry.TryGetName(ds.ActiveBehaviorHash, out string? name))
            return $"{baseSummary} | {name}";

        return baseSummary;
    }

    public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;

        if (value is not BrainBlackboard bb) return false;

        var registry = BehaviorRegistryAccessor;
        if (registry == null) return false;

        if (!session.HasComponent(entity, typeof(BehaviorState))) return false;

        var behaviorStateObj = session.GetComponent(entity, typeof(BehaviorState));
        if (behaviorStateObj is not BehaviorState ds) return false;

        if (!registry.TryGetDefinition(ds.ActiveBehaviorHash, out var def)) return false;

        if (def.ParamsDtoType != null)
        {
            RenderTypedDto(bb, def.ParamsDtoType, out string? childPath);
            // Translate "$.Speed" -> "$.BehaviorParameters.Speed" to match the actual ECS component layout
            if (childPath != null)
                doubleClickedPath = "$.BehaviorParameters" + childPath[1..];
        }
        else
        {
            RenderRawBytes(bb);
        }

        ImGui.TextUnformatted($"ExpectedThreatLevel: {bb.ExpectedThreatLevel}");
        ImGui.TextUnformatted($"Interrupt_MobilityLost: {bb.Interrupt_MobilityLost}");
        ImGui.TextUnformatted($"Interrupt_Reserved: {bb.Interrupt_Reserved}");

        return true;
    }

    // ---- Helpers ----

    private static unsafe void RenderTypedDto(BrainBlackboard bb, Type dtoType, out string? doubleClickedPath)
    {
        object boxed = Marshal.PtrToStructure((IntPtr)bb.BehaviorParameters, dtoType)!;
        ImGuiPropertyTree.Render(boxed, contextType: dtoType, out doubleClickedPath);
    }

    private static unsafe void RenderRawBytes(BrainBlackboard bb)
    {
        const int BytesPerRow = 16;
        byte* ptr = bb.BehaviorParameters;
        int total = BehaviorConstants.BrainBlackboardByteSize;
        for (int row = 0; row < total; row += BytesPerRow)
        {
            int count = Math.Min(BytesPerRow, total - row);
            var span = new ReadOnlySpan<byte>(ptr + row, count);
            ImGui.Text($"[{row:X3}] {FormatHex(span)}");
        }
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        // Simple hex formatting without allocation beyond the string
        var chars = new char[bytes.Length * 3 - 1];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) chars[i * 3 - 1] = ' ';
            bytes[i].TryFormat(chars.AsSpan(i > 0 ? i * 3 : 0, 2), out _, "X2");
        }
        return new string(chars);
    }
}
