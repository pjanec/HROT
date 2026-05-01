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
/// Entity-aware ImGui renderer for <see cref="Blackboard1024"/>.
/// When the active doctrine has a <see cref="DoctrineDefinition.HeavyDtoType"/>,
/// interprets <see cref="Blackboard1024.Memory"/> as that typed struct and renders
/// it via <see cref="ImGuiPropertyTree.Render"/>.  Falls back to a raw-byte summary
/// when no DTO type is registered.
/// </summary>
[ImGuiRenderer(typeof(Blackboard1024))]
public sealed class Blackboard1024Renderer : IEntityAwareImGuiRenderer
{
    /// <summary>
    /// Set once at startup (e.g., in CgfSubsystem or EditorSubsystem initialization).
    /// Required for doctrine lookup.
    /// </summary>
    public static DoctrineRegistry? DoctrineRegistryAccessor { get; set; }

    // ---- IImGuiRenderer ----

    public string? GetSummary(object value) => "Heavy Blackboard (1024 bytes)";

    /// <summary>
    /// Non-entity-aware fallback — cannot look up doctrine without an entity.
    /// Always falls through to default rendering.
    /// </summary>
    public bool RenderValue(object value) => false;

    // ---- IEntityAwareImGuiRenderer ----

    public bool RenderValue(IInspectableSession session, Entity entity, object value, out string? doubleClickedPath)
    {
        doubleClickedPath = null;

        if (value is not Blackboard1024 bb) return false;

        var registry = DoctrineRegistryAccessor;
        if (registry == null) return false;

        if (!session.HasComponent(entity, typeof(DoctrineState))) return false;

        var doctrineStateObj = session.GetComponent(entity, typeof(DoctrineState));
        if (doctrineStateObj is not DoctrineState ds) return false;

        if (!registry.TryGetDefinition(ds.ActiveDoctrineHash, out var def)) return false;

        if (def.HeavyDtoType != null)
        {
            RenderTypedDto(bb, def.HeavyDtoType, out string? childPath);
            // Translate "$.Speed" -> "$.Memory.Speed" to match the actual ECS component layout
            if (childPath != null)
                doubleClickedPath = "$.Memory" + childPath[1..];
        }
        else
        {
            ImGui.TextDisabled("Raw data (no HeavyDtoType registered for this doctrine)");
        }

        return true;
    }

    // ---- Helpers ----

    private static unsafe void RenderTypedDto(Blackboard1024 bb, Type dtoType, out string? doubleClickedPath)
    {
        object boxed = Marshal.PtrToStructure((IntPtr)bb.Memory, dtoType)!;
        ImGuiPropertyTree.Render(boxed, contextType: dtoType, out doubleClickedPath);
    }
}
