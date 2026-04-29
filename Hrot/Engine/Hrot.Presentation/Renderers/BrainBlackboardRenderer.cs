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
/// When the active doctrine has a <see cref="DoctrineDefinition.ParamsDtoType"/>,
/// interprets <see cref="BrainBlackboard.Memory"/> as that typed struct and renders
/// it via <see cref="ImGuiPropertyTree.Render"/>. Falls back to raw hex display otherwise.
/// </summary>
[ImGuiRenderer(typeof(BrainBlackboard))]
public sealed class BrainBlackboardRenderer : IEntityAwareImGuiRenderer
{
    /// <summary>
    /// Set once at startup (e.g., in CgfSubsystem initialization).
    /// Required for doctrine lookup.
    /// </summary>
    public static DoctrineRegistry? DoctrineRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----

    public string? GetSummary(object value) => "Blackboard Memory";

    /// <summary>
    /// Non-entity-aware fallback. Cannot look up doctrine without an entity.
    /// Always falls through to default rendering.
    /// </summary>
    public bool RenderValue(object value) => false;

    // ---- IEntityAwareImGuiRenderer ----

    public bool RenderValue(IInspectableSession session, Entity entity, object value)
    {
        if (value is not BrainBlackboard bb) return false;

        var registry = DoctrineRegistryAccessor;
        if (registry == null) return false;

        if (!session.HasComponent(entity, typeof(DoctrineState))) return false;

        var doctrineStateObj = session.GetComponent(entity, typeof(DoctrineState));
        if (doctrineStateObj is not DoctrineState ds) return false;

        if (!registry.TryGetDefinition(ds.ActiveDoctrineHash, out var def)) return false;

        if (def.ParamsDtoType != null)
        {
            RenderTypedDto(bb, def.ParamsDtoType);
        }
        else
        {
            RenderRawBytes(bb);
        }

        return true;
    }

    // ---- Helpers ----

    private static unsafe void RenderTypedDto(BrainBlackboard bb, Type dtoType)
    {
        int size = Marshal.SizeOf(dtoType);
        object boxed = Marshal.PtrToStructure((IntPtr)bb.Memory, dtoType)!;
        ImGuiPropertyTree.Render(boxed, contextType: dtoType);
    }

    private static unsafe void RenderRawBytes(BrainBlackboard bb)
    {
        const int BytesPerRow = 16;
        byte* ptr = bb.Memory;
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
