using System;
using System.Numerics;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Fdp.Kernel;
using ImGuiNET;
using Fdp.ModuleHost_Core.Abstractions;

namespace Hrot.IG.UI;

/// <summary>
/// ImGui renderer for IG context menus (Task IG.4.3).
/// </summary>
public sealed class ContextMenuPanel
{
    private readonly EntityRepository _world;
    private readonly ContextMenuSystem _menuSystem;
    private readonly Action<Entity, ContextAction> _actionHandler;
    private int _lastOpenSequence;

    public ContextMenuPanel(
        EntityRepository world,
        ContextMenuSystem menuSystem,
        Action<Entity, ContextAction> actionHandler)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _menuSystem = menuSystem ?? throw new ArgumentNullException(nameof(menuSystem));
        _actionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
    }

    /// <summary>
    /// Draws the context menu popup if one is active.
    /// Must be called within a <c>rlImGui.Begin() / rlImGui.End()</c> block.
    /// </summary>
    public void Draw()
    {
        var activeEntity = _menuSystem.ActiveMenuEntity;
        if (activeEntity == Entity.Null)
            return;

        var view = (ISimulationView)_world;
        if (!view.IsAlive(activeEntity))
        {
            _menuSystem.RequestClose(activeEntity);
            return;
        }

        // Component may not yet be visible if it was added via command buffer
        // in the same frame (buffers are flushed in BeforeSync, not PostSimulation).
        // Skip drawing this frame and wait for the next.
        if (!view.HasManagedComponent<ContextMenuState>(activeEntity))
            return;

        // ── Timing guard: detect a pending open whose cmd has not been flushed yet ──
        // ContextMenuSystem runs in PostSimulation and uses the command buffer.
        // The buffer is flushed in BeforeSync, but Draw() is called BEFORE that flush.
        // When Execute processes a right-click open it: (a) sets ActiveMenuEntity,
        // (b) increments OpenSequence, and (c) queues SetManagedComponent(IsOpen=true)
        // — but (c) is not yet visible in the ECS view.  Without this guard the stale
        // IsOpen=false below would fire RequestClose and cancel the fresh open, making
        // the context menu a one-shot feature.
        bool freshOpen = _menuSystem.OpenSequence != _lastOpenSequence;

        var state = view.GetManagedComponentRO<ContextMenuState>(activeEntity);
        if (!state.IsOpen)
        {
            if (!freshOpen)
            {
                // Safe to close: no pending open is in-flight.
                _menuSystem.RequestClose(activeEntity);
            }
            // Either way, stop drawing this frame — IsOpen reflects stale state.
            return;
        }

        string popupId = $"ContextMenu_{activeEntity.Index}";

        var hostFlags = ImGuiWindowFlags.NoDecoration
                      | ImGuiWindowFlags.NoMove
                      | ImGuiWindowFlags.NoSavedSettings
                      | ImGuiWindowFlags.NoFocusOnAppearing
                      | ImGuiWindowFlags.NoBringToFrontOnFocus
                      | ImGuiWindowFlags.NoNav
                      | ImGuiWindowFlags.NoInputs
                      | ImGuiWindowFlags.NoBackground;

        var io = ImGui.GetIO();
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(io.DisplaySize, ImGuiCond.Always);
        ImGui.Begin("##ContextMenuHost", hostFlags);

        if (freshOpen)
        {
            ImGui.OpenPopup(popupId);
        }

        ImGui.SetNextWindowPos(new Vector2(state.ScreenX, state.ScreenY), ImGuiCond.Always);
        bool popupOpen = ImGui.BeginPopup(popupId);
        if (!popupOpen)
        {
            ImGui.End();
            // Only close the ECS state if we are NOT in the middle of a fresh-open
            // attempt.  When freshOpen=true, the popup was just (re)requested and
            // BeginPopup returning false means ImGui didn't accept the OpenPopup yet
            // (same-frame dismiss + reopen race).  We leave the ECS menu open and
            // retry calling OpenPopup next frame.
            if (!freshOpen)
            {
                _menuSystem.RequestClose(activeEntity);
            }
            return;
        }

        // Popup is visually open — record the sequence so we don't call OpenPopup again.
        _lastOpenSequence = _menuSystem.OpenSequence;

        if (state.Actions.Count == 0)
        {
            ImGui.MenuItem("No actions available", string.Empty, false, false);
        }
        else
        {
            foreach (var action in state.Actions)
            {
                if (ImGui.MenuItem(action.Label))
                {
                    _actionHandler(activeEntity, action);
                    ImGui.CloseCurrentPopup();
                    _menuSystem.RequestClose(activeEntity);
                    break;
                }
            }
        }

        ImGui.EndPopup();
        ImGui.End();
    }
}
