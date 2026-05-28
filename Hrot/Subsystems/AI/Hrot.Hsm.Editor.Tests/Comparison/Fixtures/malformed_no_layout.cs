// HROT_EDITOR_GENERATED — managed by AI editor; manual edits to this file will be overwritten on next save.
// AssetId: deadbeef-cafe-babe-f00d-111122223333

using System;
using Fhsm.Compiler;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;

namespace Hrot.AI.Behaviors.Machines;

public static class MalformedNoLayout
{
    public static HsmBuilder CreateBuilder()
    {
        var builder = new HsmBuilder("MalformedNoLayout");

        builder.State("Idle", stableId: new Guid("ffffffff-0000-0000-0000-000000000001"));

        return builder;
    }

    [HsmDefinition("MalformedNoLayout", AssetId = "deadbeef-cafe-babe-f00d-111122223333")]
    public static HsmDefinitionBlob Compile() => CreateBuilder().Build().Compile();
}
