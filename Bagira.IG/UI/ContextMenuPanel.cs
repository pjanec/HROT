using System;
using System.Numerics;
using Bagira.IG.Components;
using Bagira.IG.Systems;
using Fdp.Kernel;
using ImGuiNET;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.UI;

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

        var state = view.GetManagedComponentRO<ContextMenuState>(activeEntity);
        if (!state.IsOpen)
        {
            _menuSystem.RequestClose(activeEntity);
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

        if (_menuSystem.OpenSequence != _lastOpenSequence)
        {
            _lastOpenSequence = _menuSystem.OpenSequence;
            ImGui.OpenPopup(popupId);
        }

        ImGui.SetNextWindowPos(new Vector2(state.ScreenX, state.ScreenY), ImGuiCond.Always);
        bool popupOpen = ImGui.BeginPopup(popupId);
        if (!popupOpen)
        {
            ImGui.End();
            _menuSystem.RequestClose(activeEntity);
            return;
        }

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
