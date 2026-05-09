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
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte> onInteraction,
            Action<GizmoPickToken, int> onMenuAction,
            Action? onCustomInput = null)
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

            var propertyAdapter = new ImGuiPropertyTreeAdapter(schemaRegistry);
            var renderer = new DebugPrimitiveRenderer2D(imGuiAdapter: propertyAdapter);
            var layer = new DebugGizmoLayer(renderBuffer, renderer);

            rlImGui.Setup(true);

            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                onUpdateTick(dt);
                layer.HandleInput(camera, onInteraction);
                onCustomInput?.Invoke();

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.DarkGray);
                Raylib.BeginMode2D(camera);

                layer.Render(camera, camera.Zoom);

                Raylib.EndMode2D();

                rlImGui.Begin();
                layer.DrawContextMenu(onMenuAction);
                propertyAdapter.DrawScheduled();
                rlImGui.End();

                Raylib.EndDrawing();
            }

            rlImGui.Shutdown();
            Raylib.CloseWindow();
        }
    }
}
