using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Fdp.Toolkit.Replication.Patching;

namespace Hrot.SimHost.Installers;

/// <summary>
/// <see cref="IBinaryAttributeInstaller"/> that routes <c>Name</c> and
/// <c>Affiliation</c> binary attribute records to <see cref="Fdp.Core.EntityInfo"/> ECS
/// component writes.
///
/// <para>
/// Does not require a scratchpad: both attributes are independent and can be applied
/// immediately on record receipt without deferred grouping.
/// </para>
///
/// <para>
/// Reuses <see cref="AttributeCompilerFactory.MapAffiliationString"/> and
/// <see cref="AttributeCompilerFactory.MapAffiliationInt"/> so that the binary pipeline
/// stays in sync with the JSON pipeline's affiliation mapping logic.
/// </para>
/// </summary>
public sealed class EntityDataAttributeInstaller : IBinaryAttributeInstaller<AttributeRecord>
{
    private const long EntityInfoOrdinal = (long)EDescriptorType.dtEntityInfo;

    /// <inheritdoc/>
    public void Install(BinaryInterpreterBuilder<AttributeRecord> builder)
    {
        builder.RegisterHandler(AttributeIds.Name, HandleName);
        builder.RegisterHandler(AttributeIds.Affiliation, HandleAffiliation);
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static void HandleName(BinaryPatchContext ctx, AttributeRecord record)
    {
        if (!ctx.PatchContext.CanWrite<Fdp.Core.EntityInfo>())
            return;

		ref var data = ref ctx.PatchContext.GetUnmanagedComponent<Fdp.Core.EntityInfo>();
        data.Name = record.Value.StringValue ?? string.Empty;

        ctx.MarkDescriptorDirty(EntityInfoOrdinal);
    }

    private static void HandleAffiliation(BinaryPatchContext ctx, AttributeRecord record)
    {
        if (!ctx.PatchContext.CanWrite<Fdp.Core.EntityInfo>())
            return;

		ref var data = ref ctx.PatchContext.GetUnmanagedComponent<Fdp.Core.EntityInfo>();

        data.ForceId = record.Value.ValueType == AttributeValueType.KindInt32
            ? AttributeCompilerFactory.MapAffiliationInt(record.Value.IntValue)
            : AttributeCompilerFactory.MapAffiliationString(record.Value.StringValue);

        ctx.MarkDescriptorDirty(EntityInfoOrdinal);
    }
}
