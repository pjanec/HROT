// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: a1b2c3d4-1111-2222-3333-444455556666

using System;
using System.Numerics;
using Fhsm.Compiler;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Layout;

namespace Hrot.AI.Behaviors.Machines;

public static class SimpleMachine
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("SimpleMachine");

        builder.Event("Activate", 1, 0, false, false);
        builder.Event("Deactivate", 2, 0, false, false);

        var s0 = builder.State("Idle", stableId: new Guid("aaaaaaaa-0000-0000-0000-000000000001"));
        s0.On("Activate").GoTo("Active", visualId: new Guid("cccccccc-0000-0000-0000-000000000001"));

        var s1 = builder.State("Active", stableId: new Guid("bbbbbbbb-0000-0000-0000-000000000001"));
        s1.On("Deactivate").GoTo("Idle", visualId: new Guid("dddddddd-0000-0000-0000-000000000001"));

        builder.State("Done", stableId: new Guid("eeeeeeee-0000-0000-0000-000000000001"));

        return builder;
    }

    [HsmDefinition("SimpleMachine", AssetId = "a1b2c3d4-1111-2222-3333-444455556666")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();

    [HsmLayout("a1b2c3d4-1111-2222-3333-444455556666")]
    public static HsmEditorLayout Layout() => new HsmEditorLayoutBuilder()
        .Canvas(new Vector2(0f, 0f), 1.0f)
        .State("aaaaaaaa-0000-0000-0000-000000000001", new Vector2(100f, 100f),
               comment: "waiting for trigger")
        .State("bbbbbbbb-0000-0000-0000-000000000001", new Vector2(300f, 100f),
               comment: "processing the request")
        .State("eeeeeeee-0000-0000-0000-000000000001", new Vector2(500f, 100f))
        .Transition("cccccccc-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(200f, 100f) },
                    comment: "user triggered activation")
        .Transition("dddddddd-0000-0000-0000-000000000001",
                    new Vector2[] { new Vector2(400f, 100f) })
        .Build();
}
