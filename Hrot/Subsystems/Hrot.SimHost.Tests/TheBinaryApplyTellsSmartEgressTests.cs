using Fdp.Toolkit.Replication.Attributes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using EDescriptorType = Hrot.NED.Descriptors.EDescriptorType;
using Hrot.SimHost;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-015</c> — a binary attribute apply must tell SmartEgress, or the change never leaves the
/// owner.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §15.</para>
///
/// <para>🔴🔴 <b>THE DEFECT, measured `2026-08-26`.</b> The binary path builds its context with
/// <c>EcsPatchContext.Create(repo, entity)</c> — the standalone factory whose ordinal map is EMPTY — so
/// <c>FlushDirtyMarks</c> had nothing to flush. The installers announced their descriptor through
/// <c>BinaryPatchContext.MarkDescriptorDirty</c>, which set only a local <c>ulong</c> mask that
/// <b>nothing in production ever read</b>. ⇒ the binary path told SmartEgress <b>nothing</b>.</para>
///
/// <para>⚠⚠ <b>Why it went unnoticed for so long, and why this rail is about <c>EntityInfo</c> and not
/// <c>SimTransform</c>.</b> 📌 <c>SmartEgressUtil</c>'s own remarks prescribe a SPLIT strategy: reliable
/// low-frequency descriptors *(<c>EntityInfo</c>, <c>EntityMaster</c>, <c>EntityMission</c>)* use
/// <c>MarkDirty</c>; high-frequency <c>GeoSpatial</c> uses **state comparison against
/// <c>NetworkTransform</c>** instead. ⇒ ⭐ the one attribute exercised end-to-end — <c>GeoHeading</c> →
/// <c>SimTransform</c> — republished anyway because its translator DIFFS every tick, which masked the bug
/// completely. ⛔ <c>EntityInfoEgressTranslator</c> does not diff: it gates on
/// <c>SmartEgressUtil.ShouldPublish(view, entity, DescriptorOrdinal, …)</c>. ⇒ 🔴 <b>an entity RENAME applied
/// on the owner landed in local ECS and was never republished to any node.</b></para>
/// </summary>
public class TheBinaryApplyTellsSmartEgressTests
{
    private const long EntityInfoOrdinal = (long)EDescriptorType.dtEntityInfo;

    private readonly ITestOutputHelper _out;
    public TheBinaryApplyTellsSmartEgressTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL: after a binary <c>Name</c> apply, the <c>dtEntityInfo</c> descriptor is DIRTY.</b>
    ///
    /// <para>⭐ Asserted on <c>EgressPublicationState.DirtyDescriptors</c> — the state
    /// <c>SmartEgressUtil.MarkDirty</c> writes and <c>ShouldPublish</c> reads — rather than on
    /// <c>ShouldPublish</c> itself, because that returns <see langword="true"/> for an entity with NO
    /// publication state at all *(a deliberate fail-safe)*. ⛔ Railing the fail-safe would have passed before
    /// the fix and proved nothing.</para>
    /// </summary>
    [Fact]
    public void ABinaryNameApplyLeavesTheEntityInfoDescriptorDirty()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<EntityInfo>();
        repo.RegisterComponent<EgressPublicationState>();

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(EntityInfo));
        repo.SetAuthority<EntityInfo>(e, true);   // ⭐ the owner shape — UXI-30's gate needs it

        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var patchCtx    = EcsPatchContext.Create(repo, e);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = e;

        interpreter.Apply(ctx, new[]
        {
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Name,
                Value       = AttributeValue.FromString("Renamed"),
            }
        });

        Assert.True(patchCtx.HasAppliedAny, "precondition: the owner must have applied the change");

        _out.WriteLine($"has EgressPublicationState = {repo.HasManagedComponent<EgressPublicationState>(e)}");

        Assert.True(repo.HasManagedComponent<EgressPublicationState>(e),
            "AX-015: the apply must have created publication state via SmartEgressUtil.MarkDirty. " +
            "Absent means the binary path never told SmartEgress anything — an EntityInfo change would " +
            "land locally and never be republished.");

        var state = repo.GetComponent<EgressPublicationState>(e);
        Assert.Contains(EntityInfoOrdinal, state.DirtyDescriptors);
    }

    /// <summary>
    /// ⭐⭐ <b>The dedup promise still holds: two attributes on ONE descriptor mark it once.</b>
    ///
    /// <para>📌 <c>EcsPatchContext</c>'s class remarks promise that patching both <c>Name</c> and
    /// <c>Affiliation</c> — both mapped to <c>dtEntityInfo</c> — emits a single <c>MarkDirty</c>.
    /// ⭐ <c>AX-015</c> routes the binary path into the same <c>HashSet</c>, so the promise is kept rather
    /// than re-implemented. ⚠ Railed because a <c>List</c> would have satisfied the rail above and quietly
    /// broken this one.</para>
    /// </summary>
    [Fact]
    public void TwoAttributesOnOneDescriptorMarkItOnce()
    {
        using var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<EntityInfo>();
        repo.RegisterComponent<EgressPublicationState>();

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(EntityInfo));
        repo.SetAuthority<EntityInfo>(e, true);

        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var patchCtx    = EcsPatchContext.Create(repo, e);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = e;

        interpreter.Apply(ctx, new[]
        {
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Name,
                Value       = AttributeValue.FromString("Renamed"),
            },
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Affiliation,
                Value       = AttributeValue.FromInt(2),
            },
        });

        var state = repo.GetComponent<EgressPublicationState>(e);
        int occurrences = 0;
        foreach (var o in state.DirtyDescriptors) if (o == EntityInfoOrdinal) occurrences++;

        Assert.Equal(1, occurrences);
    }
}
