using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Weather;

namespace Fdp.Toolkit.Replication.Attributes;

/// <summary>
/// ⭐ CE-3216 S1-0 (R-3012, W-8; 📄 docs/designs/weather/DESIGN_Weather_And_Time.md) — the binary arm of the scenario clock's two
/// attributes, the twin of the JSON paths in <see cref="AttributeCompilerFactory.Build"/>. The value is applied AS SENT: a runtime
/// edit sets a new scenario start time (⛔ never re-anchored to a target UTC — C-2 was superseded).
/// </summary>
public sealed class ScenarioClockAttributeInstaller : IBinaryAttributeInstaller<EntityAttributeChange>
{
    public void Install(BinaryInterpreterBuilder<EntityAttributeChange> builder)
    {
        // ⭐ UXI-30: the typed overload gates on CanWrite<ScenarioClock>() — only the clock's owner applies it.
        builder.RegisterHandler<ScenarioClock>(AttributeIds.ScenarioStartUtcTicks, HandleStart);
        builder.RegisterHandler<ScenarioClock>(AttributeIds.ScenarioUtcOffsetMinutes, HandleOffset);
    }

    private static void HandleStart(BinaryPatchContext ctx, EntityAttributeChange record)
    {
        ref var clock = ref ctx.PatchContext.GetUnmanagedComponent<ScenarioClock>();
        clock.StartUtcTicks = record.Value.Kind == AttributeValueKind.CsInt64 ? record.Value.LongValue : record.Value.IntValue;
    }

    private static void HandleOffset(BinaryPatchContext ctx, EntityAttributeChange record)
    {
        ref var clock = ref ctx.PatchContext.GetUnmanagedComponent<ScenarioClock>();
        clock.DisplayUtcOffsetMinutes = (short)(record.Value.Kind == AttributeValueKind.CsInt64 ? record.Value.LongValue : record.Value.IntValue);
    }
}
