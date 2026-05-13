using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Utils;

/// <summary>
/// Shared utility for drawing entity headers consistently across multiple panels
/// (EntityInspectorPanel, EntityWatchPanel, etc.).
/// Handles entity ID display, network ID, Copy JSON button, and read-only badge.
/// </summary>
public static class EntityHeaderDrawer
{
    private static readonly Vector4 ExConViolet = new Vector4(0.7f, 0.45f, 0.8f, 1f);

    /// <summary>
    /// Draws the entity header (ID, network ID, Copy JSON button, read-only badge).
    /// Called once per entity in the details pane.
    /// </summary>
    /// <param name="session">The inspectable session.</param>
    /// <param name="entity">The entity to display.</param>
    /// <param name="copyJsonAction">Delegate called when user clicks "Copy JSON" button.</param>
    public static void DrawEntityHeader(
        IInspectableSession session,
        Entity entity,
        Action copyJsonAction)
    {
        bool isSingleton = entity == RepositoryAdapter.SingletonEntity;

        if (isSingleton)
        {
            ImGuiApi.TextUnformatted("[Singletons]");
        }
        else
        {
            long? netId = GetNetworkId(session, entity);
            ImGuiApi.TextUnformatted($"[{entity.Index}, v{entity.Generation}]");
            if (netId.HasValue)
            {
                ImGuiApi.SameLine();
                ImGuiApi.TextColored(ExConViolet, $"({netId.Value})");
            }
        }

        // Add the Copy JSON button next to the Entity ID
        ImGuiApi.SameLine();
        if (ImGuiApi.Button("Copy JSON##Header"))
        {
            copyJsonAction();
        }
        if (ImGuiApi.IsItemHovered())
            ImGuiApi.SetTooltip("Dump exact entity state to clipboard as JSON");

        if (session.IsReadOnly)
            ImGuiApi.TextColored(new Vector4(1, 1, 0, 1), "[READ-ONLY]");
    }

    /// <summary>
    /// Gets the network ID component value if present, otherwise returns null.
    /// </summary>
    private static long? GetNetworkId(IInspectableSession session, Entity entity)
    {
        if (session.HasComponent(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity)))
        {
            var comp = session.GetComponent(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity));
            if (comp is Fdp.Toolkit.Replication.Components.NetworkIdentity ni)
                return ni.Value;
        }
        return null;
    }
}
