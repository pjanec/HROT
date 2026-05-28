// HROT_EDITOR_GENERATED - managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: face0001-0000-0000-aaaa-000000000001

using System;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.AI.Behaviors.Machines;

public static class ParallelHsm
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("ParallelHsm");

        builder.Event("Start", 1, 0, false, false);
        builder.Event("Stop", 2, 0, false, false);
        builder.Event("Reset", 3, 0, false, false);

        var idle = builder.State("Idle", stableId: new Guid("a1000000-0000-0000-0000-000000000001"));
        idle.On("Start").GoTo("Running", visualId: new Guid("a2000000-0000-0000-0000-000000000001"));

        var running = builder.State("Running", stableId: new Guid("a3000000-0000-0000-0000-000000000001"));
        running.Child("MotionTrack", sb2 =>
        {
            sb2.Initial();
        }, stableId: new Guid("a4000000-0000-0000-0000-000000000001"));
        running.Child("AnimTrack", sb2 =>
        {
            sb2.Initial();
        }, stableId: new Guid("a5000000-0000-0000-0000-000000000001"));
        running.On("Stop").GoTo("Idle", visualId: new Guid("a6000000-0000-0000-0000-000000000001"));

        builder.GlobalTransition("Reset", "Idle", visualId: new Guid("a7000000-0000-0000-0000-000000000001"));

        return builder;
    }

    [HsmDefinition("ParallelHsm", AssetId = "face0001-0000-0000-aaaa-000000000001")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout("face0001-0000-0000-aaaa-000000000001")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .Canvas(new Vector2(0f, 0f), 1.0f)
        .State("a1000000-0000-0000-0000-000000000001", new Vector2(50f, 200f),
               comment: "idle before activation")
        .State("a3000000-0000-0000-0000-000000000001", new Vector2(300f, 200f),
               comment: "parallel motion and animation tracks active")
        .Region("a4000000-0000-0000-0000-000000000001", 0, new Vector2(310f, 250f),
                comment: "motion track region")
        .Region("a5000000-0000-0000-0000-000000000001", 1, new Vector2(450f, 250f),
                comment: "animation track region")
        .Transition("a2000000-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(175f, 200f) },
                    comment: "start both tracks simultaneously")
        .Transition("a6000000-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(175f, 300f) })
        .Transition("a7000000-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(200f, 50f) },
                    comment: "emergency stop from any state")
        .Build();
}
