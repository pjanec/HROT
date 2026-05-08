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
    var producer = new DebugPrimitiveBuffer();
    var consumer = new DebugPrimitiveBuffer();
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

        var renderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D();
        float dt = 1f / 30f;

        while (!Raylib_cs.Raylib.WindowShouldClose())
        {
            dt = Raylib_cs.Raylib.GetFrameTime();

            producer.Clear();
            var builder = new LocalDrawBuilder(producer);
            gen.Emit(dt, builder);

            transport.PublishPrimitives(producer.GetFrame());

            consumer.Clear();
            transport.PollAndApply(consumer);

            Raylib_cs.Raylib.BeginDrawing();
            Raylib_cs.Raylib.ClearBackground(Raylib_cs.Color.DarkGray);
            Raylib_cs.Raylib.BeginMode2D(camera);

            renderer.Render(consumer.GetFrame(), camera, camera.Zoom);

            Raylib_cs.Raylib.EndMode2D();
            Raylib_cs.Raylib.EndDrawing();
        }

        Raylib_cs.Raylib.CloseWindow();
    }
}
