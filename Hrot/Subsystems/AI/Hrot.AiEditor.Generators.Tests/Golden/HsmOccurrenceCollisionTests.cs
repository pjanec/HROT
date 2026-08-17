using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// 🔴🔴🔴 <b><c>E3</c> — two orthogonal regions running one action DO write the same bytes, and the
/// seam is not where the plan says it is.</b>
///
/// <para>
/// 📄 <c>Architect_Question_34</c> §7 names this as the one occurrence case that <b>silently
/// corrupts</b> — <i>"<c>hash(method @ fieldOffset)</c> has no region or state in it ⇒ both write the
/// same bytes"</i> — and it is right about the symptom.
/// </para>
///
/// <para>
/// ⛔⛔ <b>But the handoff's premise is that this is <i>"a signature widening, not a data-flow
/// redesign"</i>, because <c>r</c> (region) and <c>current</c> (state) are already in scope at the
/// <c>ExecuteAction</c> call site. 📐 Measured, that is not sufficient — and the reason is the
/// STORAGE, not the signature.</b>
/// </para>
///
/// <list type="number">
///   <item>⭐ The occurrence <b>is</b> in scope in <c>HsmKernelCore</c> (<c>slotIndex</c>,
///   <c>stateId</c>) — that half of the premise holds.</item>
///   <item>🔴 <b>But the thunk cannot receive it</b>: <c>HsmActionDispatcher</c> dispatches through
///   <c>delegate* &lt;void*, void*, HsmCommandWriter*, void&gt;</c>, and every registered id is a
///   <b>static</b> function pointer chosen at build time. Regions are a runtime notion.</item>
///   <item>🔴🔴 <b>And even with the occurrence passed in, there is nowhere for a second occurrence's
///   bytes to LIVE.</b> The generated thunk resolves its DTO as
///   <c>bb.BehaviorParameters[0] + &lt;baked offset&gt;</c> — a fixed offset into the entity's
///   <b>single</b> <c>BrainBlackboard</c> (<c>MaxBehaviorParamByteSize</c> = 100 B, one region per
///   entity). ⇒ two occurrences have one home by construction.</item>
/// </list>
///
/// <para>
/// ⇒ ⭐⭐ <b>`E3` is a STORAGE move</b> — the per-occurrence bytes have to come from the partition
/// allocator under <c>ComputeStatefulSlotKey(assetId, Scope.Node, occurrence, variableId)</c>, which
/// is exactly the route <c>Q34</c> §7 recommends for <c>E5</c> — <b>plus</b> the delegate widening.
/// ⛔ That is a data-flow redesign across <c>Fhsm.Kernel</c>, the analyzer's thunk emission and the
/// blackboard allocator, and it reaches <c>ExtDeps</c>. <b>Escalated rather than half-built.</b>
/// </para>
///
/// <para>
/// ⭐ <b>These tests are the handoff's own rail, in the only form that can be committed:</b> they
/// assert the GAP, with the mechanism named. ⚠ <b>Invert them when <c>E3</c> lands</b> — Batch 70's
/// rule — and <c>HsmOrthogonalRegions</c> is already in the corpus to carry the positive version.
/// </para>
/// </summary>
public sealed class HsmOccurrenceCollisionTests
{
    /// <summary>
    /// 🔴 <b>The dispatch signature carries no occurrence.</b> An action thunk is handed the instance,
    /// the context bridge and the command writer — nothing that says <i>which region</i> is running it.
    /// ⛔ Until this widens, no amount of key arithmetic can separate two regions.
    /// </summary>
    [Fact]
    public void TheActionDispatchSignature_CarriesNoOccurrence_Yet()
    {
        var execute = typeof(Fhsm.Kernel.HsmActionDispatcher)
            .GetMethod(nameof(Fhsm.Kernel.HsmActionDispatcher.ExecuteAction),
                       BindingFlags.Public | BindingFlags.Static)!;

        var names = execute.GetParameters().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "actionId", "instance", "context", "writer" }, names);

        // ⛔ No region, no state, no occurrence of any spelling.
        Assert.DoesNotContain(names, n =>
            n!.Contains("region", StringComparison.OrdinalIgnoreCase)
            || n.Contains("occurrence", StringComparison.OrdinalIgnoreCase)
            || n.Contains("state", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 🔴🔴 <b>The generated thunk resolves its state at a FIXED offset into the entity's single
    /// <c>BrainBlackboard</c>.</b> That is the collision, stated as a measurement: the offset is baked
    /// at build time and the component is one per entity, so two concurrently-active regions running
    /// the same action address <b>the same bytes</b>.
    ///
    /// <para>
    /// ⭐ Read out of the ANALYZER'S SOURCE — the thing that emits the thunk — rather than recomputed.
    /// A rail that restated the rule would pass whatever the emitter did.
    /// </para>
    /// </summary>
    [Fact]
    public void TheGeneratedThunk_ResolvesStateAtAFixedPerEntityOffset_Yet()
    {
        var generator = System.IO.File.ReadAllText(FindUp(System.IO.Path.Combine(
            "FDP", "Toolkits", "Fdp.Toolkits.Analyzers", "HsmActionGenerator.cs")));

        // ⚠ BP-306 (Batch 78) moved this expression out of the four emitters that each spelled it
        //   themselves and into ONE home, because one of the four spellings was wrong. The claim is
        //   unchanged — a baked, build-time constant offset — so the rail follows the expression to
        //   where it now lives rather than being deleted.
        var expression = System.IO.File.ReadAllText(FindUp(System.IO.Path.Combine(
            "FDP", "Toolkits", "Fdp.Toolkits.Analyzers", "Shared", "BlackboardParamsExpression.cs")));

        // The emitted body: ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)<offset>)
        Assert.Contains(".BehaviorParameters[0]", expression);
        Assert.Contains("(nint)\" + byteOffset", expression);

        // ⭐ And the generator still reaches it through that one home — if it stopped, the offset
        //   could drift back to a second spelling without this rail noticing.
        Assert.Contains("BlackboardParamsExpression.At(\"bb\", entry.Offset)", generator);

        // ⛔ And nothing in the thunk consults the partition allocator, which is where per-occurrence
        //    bytes would have to come from.
        Assert.DoesNotContain("ComputeStatefulSlotKey", generator);
        Assert.DoesNotContain("TryGetSlotOffset", generator);
    }

    /// <summary>
    /// ⭐ <b>The corpus asset that will carry the positive rail already exists.</b>
    /// <c>HsmOrthogonalRegions</c> was seeded in Batch 71 <i>for this</i> — two regions both reaching
    /// one shared-scope slot — so when <c>E3</c> lands the gate is already in place and the baseline
    /// will move to prove it.
    /// </summary>
    [Fact]
    public void TheCorpusAlreadyCarriesTheTwoRegionFixture()
    {
        var names = AiAssetCorpus.AssetNames(AiAssetKind.Hsm).Select(o => (string)o[0]).ToList();
        Assert.Contains("HsmOrthogonalRegions", names);

        var dto = Hrot.AiEditor.Persistence.Hsm.HsmJsonServices
            .Deserialize(AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, "HsmOrthogonalRegions"))!;

        Assert.Contains(dto.States, s => s.IsParallel);
        // Two children in DIFFERENT regions, both running the same action.
        var workers = dto.States.Where(s => s.OnEntryAction != null).ToList();
        Assert.True(workers.Count >= 2);
        Assert.Single(workers.Select(w => w.OnEntryAction).Distinct());
        Assert.Equal(2, workers.Select(w => w.RegionIndex).Distinct().Count());
    }

    private static string FindUp(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, relative);
            if (System.IO.File.Exists(candidate)) return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.FileNotFoundException($"Not found on any ancestor: {relative}");
    }
}
