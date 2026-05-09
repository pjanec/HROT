using System;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Example;

// ---- Entry point -----------------------------------------------------------

string mode    = "local";
bool headless  = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--mode" && i + 1 < args.Length)
        mode = args[i + 1].ToLowerInvariant();
    if (args[i] == "--headless")
        headless = true;
}

Console.WriteLine($"GizmoMap Example -- mode={mode} headless={headless}");

IGizmoTransport transport = mode switch
{
    "dds"   => new DdsGizmoTransport(),
    "local" => new LocalGizmoTransport(),
    _       => throw new ArgumentException($"Unknown mode: {mode}. Use --mode local or --mode dds."),
};

using (transport)
{
    var producer = new GizmoPrimitiveBuffer();
    var consumer = new GizmoPrimitiveBuffer();
    var gen      = new DemoSceneGenerator();

    if (headless)
    {
        // Headless CI mode: run 30 frames without Raylib.
        const float Dt = 1f / 30f;
        for (int frame = 0; frame < 30; frame++)
        {
            producer.Clear();
            var builder = new LocalDrawBuilder(producer);
            gen.Emit(Dt, builder);

            transport.PublishPrimitives(producer.GetFrame());

            consumer.Clear();

            // Synchronize the string intern map across the simulated network boundary
            foreach (var kvp in producer.InternMap.Entries)
            {
                consumer.InternMap.Intern(kvp.Key, kvp.Value);
            }

            transport.PollAndApply(consumer);

            int count = consumer.GetFrame().Length;
            if (frame == 0)
                Console.WriteLine($"Frame 0: {count} primitives in consumer buffer.");
        }
        Console.WriteLine("Headless run complete (30 frames).");
    }
    else
    {
        // Interactive Raylib window mode.
        Raylib_cs.Raylib.InitWindow(640, 480, $"GizmoMap Example - {mode}");
        Raylib_cs.Raylib.SetTargetFPS(30);

        var camera = new Raylib_cs.Camera2D
        {
            Target   = System.Numerics.Vector2.Zero,
            Offset   = new System.Numerics.Vector2(320f, 240f),
            Rotation = 0f,
            Zoom     = 1f,
        };

        // Wire the StructEdit side-channel so ComponentInspector shows a real property tree.
        var schemaRegistry = new GizmoMap.Presentation.GizmoSchemaRegistry();
        schemaRegistry.Register(0xDEADBEEF, DemoSceneGenerator.BuildMockDocument());

        var propertyAdapter = new GizmoMap.Presentation.ImGuiPropertyTreeAdapter(schemaRegistry);
        var renderer        = new GizmoMap.Presentation.DebugPrimitiveRenderer2D(imGuiAdapter: propertyAdapter);
        var layer           = new GizmoMap.Presentation.DebugGizmoLayer(consumer, renderer);

        // Initialize rlImGui so ImGui-based overlays (context menus, inspectors) work.
        rlImGui_cs.rlImGui.Setup(true);

        float dt = 1f / 30f;

        while (!Raylib_cs.Raylib.WindowShouldClose())
        {
            dt = Raylib_cs.Raylib.GetFrameTime();

            producer.Clear();
            var builder = new LocalDrawBuilder(producer);
            gen.Emit(dt, builder);

            transport.PublishPrimitives(producer.GetFrame());

            consumer.Clear();

            // Synchronize the string intern map across the simulated network boundary
            foreach (var kvp in producer.InternMap.Entries)
            {
                consumer.InternMap.Intern(kvp.Key, kvp.Value);
            }

            transport.PollAndApply(consumer);

            // Route mouse/keyboard input through the gizmo interaction layer.
            // Interaction events are forwarded to DemoSceneGenerator so it can
            // update the interactive box position and dispatch to managed gizmos.
            layer.HandleInput(camera, gen.OnGizmoInteraction);

            // R key: activate the entity rotator gizmo (exclusive-focus mode).
            if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.R))
                gen.TriggerRotator();

            Raylib_cs.Raylib.BeginDrawing();
            Raylib_cs.Raylib.ClearBackground(Raylib_cs.Color.DarkGray);
            Raylib_cs.Raylib.BeginMode2D(camera);

            layer.Render(camera, camera.Zoom);

            Raylib_cs.Raylib.EndMode2D();

            // ImGui pass: context menus and component inspector overlays.
            rlImGui_cs.rlImGui.Begin();
            layer.DrawContextMenu(gen.OnMenuAction);
            propertyAdapter.DrawScheduled();
            rlImGui_cs.rlImGui.End();

            Raylib_cs.Raylib.EndDrawing();
        }

        rlImGui_cs.rlImGui.Shutdown();
        Raylib_cs.Raylib.CloseWindow();
    }
}
