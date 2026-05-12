using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Network;
using Raylib_cs;
using rlImGui_cs;

namespace GizmoMap.Presentation
{
    // Shared Raylib + ImGui frontend loop for gizmo viewing and interaction.
    public static class GizmoViewerFrontend
    {
        public static void Run(
            string windowTitle,
            GizmoPrimitiveBuffer renderBuffer,
            GizmoSchemaRegistry schemaRegistry,
            Action<float> onUpdateTick,
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte, string?> onInteraction,
            Action<GizmoPickToken, int> onMenuAction,
            Action? onCustomInput = null,
            ImGuiPropertyTreeAdapter? externalAdapter = null)
        {
            Raylib.InitWindow(640, 480, windowTitle);
            Raylib.SetTargetFPS(30);

            var camera = new Camera2D
            {
                Target = Vector2.Zero,
                Offset = new Vector2(320f, 240f),
                Rotation = 0f,
                Zoom = 1f,
            };

            var propertyAdapter = externalAdapter ?? new ImGuiPropertyTreeAdapter(schemaRegistry);
            var renderer = new DebugPrimitiveRenderer2D(imGuiAdapter: propertyAdapter);
            var layer = new DebugGizmoLayer(renderer); // buffer data passed per-call

            rlImGui.Setup(true);

            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                onUpdateTick(dt);
                // Wrap the 6-param callback into the 5-param HandleInput signature.
                layer.HandleInput(
                    renderBuffer.GetFrame(),
                    renderBuffer.InternMap,
                    camera,
                    (token, kind, pos, actionId, flags) => onInteraction(token, kind, pos, actionId, flags, null));
                onCustomInput?.Invoke();

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.DarkGray);
                Raylib.BeginMode2D(camera);

                layer.Render(renderBuffer.GetFrame(), camera, camera.Zoom);

                Raylib.EndMode2D();

                // Evaluate meta-primitives before drawing menus
                layer.ExtractMetaPrimitives(renderBuffer.GetFrame(), renderBuffer.InternMap);

                rlImGui.Begin();
                layer.DrawMainMenu(actionId =>
                    onMenuAction?.Invoke(new GizmoPickToken { AnchorId = 0 }, actionId));
                layer.DrawContextMenu(onMenuAction);
                propertyAdapter.DrawScheduled((networkId, gizmoTypeId, json) =>
                    onInteraction(new GizmoPickToken { AnchorId = networkId, GizmoTypeId = gizmoTypeId },
                        GizmoInteractionEventKind.StructUpdate, Vector3.Zero, 0, 0, json));
                rlImGui.End();

                Raylib.EndDrawing();
            }

            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }
}
