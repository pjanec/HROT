// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: f0e1d2c3-aaaa-bbbb-cccc-ddddeeee0001

using System;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Layout;
using Hrot.Game.Combat;

namespace Hrot.AI.Behaviors.Machines;

public static class ParallelMachine
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("ParallelMachine");

        builder.Event("Start", 1, 0, false, false);
        builder.Event("Stop", 2, 0, false, false);
        builder.Event("Reset", 3, 0, false, false);

        var idle = builder.State("Idle", stableId: new Guid("10000000-0000-0000-0000-000000000001"));
        idle.On("Start").GoTo("Running", visualId: new Guid("20000000-0000-0000-0000-000000000001"));

        var running = builder.State("Running", stableId: new Guid("30000000-0000-0000-0000-000000000001"));
        running.Child("MotionTrack", sb2 =>
        {
            sb2.Initial();
        }, stableId: new Guid("40000000-0000-0000-0000-000000000001"));
        running.Child("AnimTrack", sb2 =>
        {
            sb2.Initial();
        }, stableId: new Guid("50000000-0000-0000-0000-000000000001"));
        running.On("Stop").GoTo("Idle", visualId: new Guid("60000000-0000-0000-0000-000000000001"));

        builder.GlobalTransition("Reset", "Idle", visualId: new Guid("70000000-0000-0000-0000-000000000001"));

        return builder;
    }

    [HsmDefinition("ParallelMachine", AssetId = "f0e1d2c3-aaaa-bbbb-cccc-ddddeeee0001")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout("f0e1d2c3-aaaa-bbbb-cccc-ddddeeee0001")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .Canvas(new Vector2(0f, 0f), 1.0f)
        .State("10000000-0000-0000-0000-000000000001", new Vector2(50f, 200f),
               comment: "system not yet started")
        .State("30000000-0000-0000-0000-000000000001", new Vector2(300f, 200f),
               comment: "parallel execution active")
        .Region("40000000-0000-0000-0000-000000000001", 0, new Vector2(310f, 250f),
                comment: "handles movement updates")
        .Region("50000000-0000-0000-0000-000000000001", 1, new Vector2(450f, 250f),
                comment: "handles animation blending")
        .Transition("20000000-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(175f, 200f) },
                    comment: "kick off both tracks simultaneously")
        .Transition("60000000-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(175f, 300f) })
        .Transition("70000000-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(200f, 50f) },
                    comment: "emergency stop from any state")
        .Build();
}
