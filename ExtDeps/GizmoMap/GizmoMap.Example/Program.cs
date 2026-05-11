using System;
using Fdp.Toolkit.Diagnostics.Gizmos;
using GizmoMap.Example;
using GizmoMap.Presentation;
using Raylib_cs;

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

            transport.PublishPrimitives(producer.GetFrame(), producer.InternMap);

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
        // Wire the StructEdit side-channel so StructInspector shows a real property tree.
        var schemaRegistry = new GizmoSchemaRegistry();
        schemaRegistry.Register(0xDEADBEEF, DemoSceneGenerator.BuildMockDocument());
        schemaRegistry.Register(LayerControlGizmo.SchemaHash, DemoSceneGenerator.BuildLayerControlDocument());

        GizmoViewerFrontend.Run(
            $"GizmoMap Example - {mode}",
            consumer,
            schemaRegistry,
            onUpdateTick: dt =>
            {
                producer.Clear();
                var builder = new LocalDrawBuilder(producer);
                gen.Emit(dt, builder);
                transport.PublishPrimitives(producer.GetFrame(), producer.InternMap);
                consumer.Clear();
                transport.PollAndApply(consumer);
            },
            onInteraction: (token, kind, pos, actionId, flags, payloadJson) =>
                gen.OnGizmoInteraction(token, kind, pos, actionId, flags, payloadJson),
            onMenuAction: (token, actionId) => gen.OnMenuAction(token, actionId),
            onCustomInput: () =>
            {
                // R key: activate the entity rotator gizmo (exclusive-focus mode).
                if (Raylib.IsKeyPressed(KeyboardKey.R))
                    gen.TriggerRotator();
            });
    }
}
