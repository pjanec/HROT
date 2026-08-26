using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-017</c> — THE TWO ATTRIBUTE-UPDATE PATHS MUST AGREE.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §16. 🔒 <b>User, <c>2026-08-26</c>:</b>
/// *"we need consistency between json and binary attribute update path."*</para>
///
/// <para>⭐⭐ <b>What "consistent" has to MEAN to be checkable.</b> The two paths are deliberately different
/// mechanisms — JSON walks a routing table keyed by an FNV path hash, binary dispatches on an
/// <c>AttributeId</c>. ⛔ So "consistent" cannot mean "the same code". ⭐ It means <b>the same observable
/// effect for the same logical attribute</b>, and that is exactly three claims:</para>
///
/// <list type="number">
///   <item>⭐⭐ the same COMPONENT STATE — a heading applied either way lands on the same field;</item>
///   <item>⭐⭐⭐ the same DIRTY DESCRIPTOR — or the change lands locally and never republishes
///   *(the <c>AX-015</c> defect)*;</item>
///   <item>⭐⭐ the same DELIVERY GUARANTEE — neither path may depend on its caller remembering to flush.</item>
/// </list>
///
/// <para>🔴🔴 <b>Claim ③ is the one that was FALSE until <c>AX-017</c>, and it is the interesting one.</b>
/// 📐 Measured <c>2026-08-26</c>: <c>BinaryInterpreter.Apply</c> ends with
/// <c>ctx.PatchContext.FlushDirtyMarks()</c> — a binary caller cannot forget. ⛔ The JSON path left the flush
/// to its caller, and <b>three production callers each remembered it on a separate line</b>. ⇒ a fourth that
/// forgot would reproduce <c>AX-015</c> exactly: applied locally, never republished, no exception anywhere.
/// ⭐ <c>JsonAttributeCompiler.Compile</c> now flushes itself — 📌 the same fix shape as <c>UXI-30</c>/
/// <c>AX-001</c>, where the authority gate moved to REGISTRATION so it could not be forgotten.</para>
///
/// <para>⚠ <b>The asymmetry that REMAINS, stated rather than glossed.</b> The JSON path learns its ordinal
/// from the routing table *(implicitly, keyed by component TYPE, on component access)*; the binary path is
/// told by the installer *(explicitly, per apply)*. ⭐ Both converge on ONE sink —
/// <c>EcsPatchContext</c>'s ordinal <c>HashSet</c> — and both read the SAME constants from
/// <see cref="DescriptorOrdinal"/> in <see cref="AttributeCompilerFactory"/>. ⛔ But nothing makes a
/// divergence impossible by construction, which is why this file rails the effect rather than trusting the
/// shared constant.</para>
/// </summary>
public class TheJsonAndBinaryPathsAgreeTests
{
    private const long EntityInfoOrdinal = (long)DescriptorOrdinal.EntityInfo;
    private const long WorldPosOrdinal   = (long)DescriptorOrdinal.WorldPos;

    /// <summary>⭐ An owner-shaped entity: the components registered and authority granted, as UXI-30 needs.</summary>
    private static (EntityRepository repo, Entity entity) OwnedEntity()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<Fdp.Core.EntityInfo>();
        repo.RegisterComponent<EgressPublicationState>();

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(Fdp.Core.EntityInfo));
        repo.AddComponent(e, default(SimTransform));
        repo.SetAuthority<Fdp.Core.EntityInfo>(e, true);
        repo.SetAuthority<SimTransform>(e, true);
        return (repo, e);
    }

    private static long[] DirtyDescriptors(EntityRepository repo, Entity e)
        => repo.HasManagedComponent<EgressPublicationState>(e)
            ? repo.GetComponent<EgressPublicationState>(e).DirtyDescriptors.OrderBy(o => o).ToArray()
            : Array.Empty<long>();

    // ══ ① + ② — the same attribute, both paths, compared ══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A <c>Name</c> change marks the SAME descriptor whichever path applied it.</b>
    ///
    /// <para>⭐ Asserted as set EQUALITY between the two runs, not against a literal. ⚠ A literal
    /// assertion would let both paths drift to the same wrong ordinal and stay green; comparing the
    /// paths to EACH OTHER is the claim the user asked for, and comparing to
    /// <see cref="DescriptorOrdinal.EntityInfo"/> as well is what pins it to the right one.</para>
    /// </summary>
    [Fact]
    public void ANameChangeMarksTheSameDescriptorOnBothPaths()
    {
        // ── JSON ──
        var (jsonRepo, jsonEntity) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        var jsonCtx  = compiler.CreatePatchContext(jsonRepo, jsonEntity);
        compiler.Compile("{\"Name\":\"Renamed\"}", jsonCtx);
        // ⛔ NO explicit FlushDirtyMarks — claim ③. Compile flushes itself.

        // ── binary ──
        var (binRepo, binEntity) = OwnedEntity();
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var binPatchCtx = EcsPatchContext.Create(binRepo, binEntity);
        var binCtx      = interpreter.CreateContext(binPatchCtx);
        binCtx.Repo = binRepo; binCtx.Entity = binEntity;
        interpreter.Apply(binCtx, new[]
        {
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Name,
                Value       = AttributeValue.FromString("Renamed"),
            }
        });

        // ① the same component state
        Assert.Equal("Renamed", jsonRepo.GetComponent<Fdp.Core.EntityInfo>(jsonEntity).Name);
        Assert.Equal("Renamed", binRepo.GetComponent<Fdp.Core.EntityInfo>(binEntity).Name);

        // ② the same dirty descriptor — path against path, then both against the vocabulary
        var jsonDirty = DirtyDescriptors(jsonRepo, jsonEntity);
        var binDirty  = DirtyDescriptors(binRepo, binEntity);

        Assert.Equal(jsonDirty, binDirty);
        Assert.Equal(new[] { EntityInfoOrdinal }, jsonDirty);
    }

    /// <summary>
    /// ⭐⭐ <b>And the same for <c>Heading</c> → <c>SimTransform</c>, whose descriptor is a different one.</b>
    ///
    /// <para>📌 Worth its own rail because <c>SmartEgressUtil</c>'s split strategy means the <c>WorldPos</c>
    /// descriptor is republished by state comparison ANYWAY — which is precisely what masked <c>AX-015</c>
    /// for so long. ⚠ So the mark being correct here is not observable end-to-end; it is only observable
    /// exactly here.</para>
    /// </summary>
    [Fact]
    public void AHeadingChangeMarksTheSameDescriptorOnBothPaths()
    {
        var (jsonRepo, jsonEntity) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        var jsonCtx  = compiler.CreatePatchContext(jsonRepo, jsonEntity);
        compiler.Compile("{\"Heading\":90.0}", jsonCtx);

        var (binRepo, binEntity) = OwnedEntity();
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var binPatchCtx = EcsPatchContext.Create(binRepo, binEntity);
        var binCtx      = interpreter.CreateContext(binPatchCtx);
        binCtx.Repo = binRepo; binCtx.Entity = binEntity;
        interpreter.Apply(binCtx, new[]
        {
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Heading,
                Value       = AttributeValue.FromDouble(90.0),
            }
        });

        // ① the same rotation, to float tolerance — both go through the same compass→yaw convention.
        var jsonRot = jsonRepo.GetComponent<SimTransform>(jsonEntity).Rotation;
        var binRot  = binRepo.GetComponent<SimTransform>(binEntity).Rotation;
        Assert.Equal(jsonRot.X, binRot.X, 5);
        Assert.Equal(jsonRot.Y, binRot.Y, 5);
        Assert.Equal(jsonRot.Z, binRot.Z, 5);
        Assert.Equal(jsonRot.W, binRot.W, 5);

        // ② the same dirty descriptor
        var jsonDirty = DirtyDescriptors(jsonRepo, jsonEntity);
        var binDirty  = DirtyDescriptors(binRepo, binEntity);

        Assert.Equal(jsonDirty, binDirty);
        Assert.Equal(new[] { WorldPosOrdinal }, jsonDirty);
    }

    // ══ ③ — the delivery guarantee, railed on its own ══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL FOR CLAIM ③: a JSON caller that never calls <c>FlushDirtyMarks</c> still gets its
    /// descriptor marked.</b>
    ///
    /// <para>⭐ ①/② already apply without an explicit flush, so this is nearly the same assertion — ⚠ but
    /// stated separately ON PURPOSE, because it is the one a future refactor could silently undo while ①/②
    /// still pass *(a caller re-added inside the test, a helper that flushes)*. ⭐ Here there is nothing
    /// between <c>Compile</c> and the assertion.</para>
    /// </summary>
    [Fact]
    public void TheJsonPathDoesNotNeedItsCallerToRememberToFlush()
    {
        var (repo, e) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);

        compiler.Compile("{\"Name\":\"Flushless\"}", compiler.CreatePatchContext(repo, e));

        Assert.True(repo.HasManagedComponent<EgressPublicationState>(e),
            "AX-017: JsonAttributeCompiler.Compile must flush its own dirty marks, as " +
            "BinaryInterpreter.Apply already does. A caller that forgets reproduces AX-015: the change " +
            "lands in local ECS and is never republished, with no exception anywhere.");

        Assert.Equal(new[] { EntityInfoOrdinal }, DirtyDescriptors(repo, e));
    }

    /// <summary>
    /// ⭐⭐ <b>And flushing TWICE marks once — so the three existing production callers that still flush
    /// explicitly stay correct.</b>
    ///
    /// <para>⚠ This is the rail that makes the change SAFE rather than merely nice: making <c>Compile</c>
    /// flush would be a defect if a second flush double-counted. 📐 It cannot —
    /// <c>SmartEgressUtil.MarkDirty</c> adds to a <c>HashSet</c> — ⭐ and this pins that rather than
    /// trusting it.</para>
    /// </summary>
    [Fact]
    public void AnExplicitFlushAfterCompileIsHarmless()
    {
        var (repo, e) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        var ctx = compiler.CreatePatchContext(repo, e);

        compiler.Compile("{\"Name\":\"Twice\"}", ctx);
        ctx.FlushDirtyMarks();      // ⭐ what UpdateEntityAttributeRequestSystem / DebugApiService still do
        ctx.FlushDirtyMarks();

        Assert.Equal(new[] { EntityInfoOrdinal }, DirtyDescriptors(repo, e));
    }
}
