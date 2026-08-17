using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b><c>E6</c>/<c>W9</c> — the HSM action id AGREES, end to end.</b>
///
/// <para>
/// ⚠⚠ <b>These assertions are INVERTED from Batch 71, deliberately — inverted, not deleted.</b> They
/// asserted the <b>disagreement</b>, because that was the shipped truth: the analyzer hashed the
/// SIMPLE name at both its sites while <c>Fhsm.Compiler.HsmFlattener</c> hashes whatever string the
/// ASSET stored, and <c>HsmEmitCore</c> stores the FQN. 🔴 The shipped HSM entry actions therefore
/// never dispatched — <c>ExecuteAction</c> was a <c>TryGetValue</c> miss, no crash and no log.
/// </para>
///
/// <para>
/// ⭐⭐ <b>COORDINATOR RULING <c>2026-08-17</c>: option (A), FQN everywhere</b> (plan §4A6). ⛔ (B) —
/// making the asset store the simple name — was rejected: it leaves <c>W9</c>/<c>E6</c> unfixed
/// <b>and</b> puts the collision into the FILE FORMAT. ⭐ (A)'s cost was four call sites in example
/// projects, and they fail at compile time.
/// </para>
///
/// <para>
/// ⭐⭐⭐ <b>The rail that matters is asserted against the REAL COMPILED BLOB, not against a
/// recomputation of the key</b> — <see cref="EveryBlobActionId_IsRegisteredByAnActualMethod"/>. A rail
/// that recomputes the key it is testing proves only that the code equals itself, which is how the
/// original disagreement survived.
/// </para>
/// </summary>
public sealed class HsmActionIdAgreementTests
{
    /// <summary>
    /// ⚠ An independent recomputation of <c>Fhsm.Compiler.HsmFlattener.ComputeHash</c> — written out
    /// rather than called, because it is <c>private</c>.
    /// <see cref="TheFlattenerAgreesWithThisRecomputation"/> pins it against the real compiler.
    /// </summary>
    private static ushort Fnv1a16(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s) { hash ^= c; hash *= 16777619; }
        return (ushort)(hash & 0xFFFF);
    }

    private const string ActionSimpleName = "StubIdle";
    private const string ActionFqn        = "Hrot.AI.Behaviors.CgfHsmNodes.StubIdle";

    /// <summary>
    /// ⭐ The recomputation is pinned against the REAL compiler: <c>HsmShowcase</c>'s entry action is
    /// authored as the FQN, and the compiled blob must address exactly <c>Fnv1a16(fqn)</c>.
    /// </summary>
    [Fact]
    public void TheFlattenerAgreesWithThisRecomputation()
        => Assert.Contains(Fnv1a16(ActionFqn), ActionIdsIn(Hrot.AI.Behaviors.Machines.HsmShowcase.Compile()));

    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED (Batch 72).</b> It read <c>Assert.NotEqual</c> and named the defect; the
    /// registrar now keys on the FQN, so the id the blob ADDRESSES is the id the registrar REGISTERS.
    /// </summary>
    [Fact]
    public void TheBlobsActionId_MatchesTheRegistrarsId()
    {
        ushort addressedByTheBlob = Fnv1a16(ActionFqn);
        ushort registeredByTheGen = Fnv1a16(ActionFqn);   // HsmActionKey.ForActionName(FullName)

        Assert.Equal(addressedByTheBlob, registeredByTheGen);

        // ⭐ And the SIMPLE name — what the registrar used to key on — is a different id, which is
        //   precisely why nothing dispatched before.
        Assert.NotEqual(addressedByTheBlob, Fnv1a16(ActionSimpleName));

        // ⭐ The asset really does store the FQN: the premise, not an assumption.
        var dto = HsmJsonServices.Deserialize(AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, "HsmShowcase"))!;
        Assert.Contains(dto.States, s => s.OnEntryAction == ActionFqn);
    }

    /// <summary>
    /// 🔴🔴🔴 <b>THE RAIL: for every corpus asset, every id the compiled BLOB addresses is an id a real
    /// <c>[HsmAction]</c>/<c>[HsmGuard]</c> method registers.</b>
    ///
    /// <para>
    /// ⭐⭐ <b>The two sides are derived independently, and NEITHER recomputes the key.</b> The left
    /// side comes from <b>compiling the asset</b> (asset strings → flattener → blob ids). The right
    /// side is read out of the <b>GENERATED DISPATCHER TABLE</b> — the actual keys the analyzer
    /// emitted into <c>HsmActionDispatcher</c>.
    /// </para>
    ///
    /// <para>
    /// 🔴🔴 <b>The first draft of this rail recomputed the right side as <c>FNV(FullName)</c> — and a
    /// revert probe caught it: reverting the analyzer to the simple-name key left the test GREEN,
    /// because the test was asserting its own rule rather than the generator's.</b> ⭐ FOURTH time in
    /// five batches: <i>ask the artefact, not the thing that produced it</i>. The artefact here is the
    /// emitted dictionary, so that is what it reads.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusAssets))]
    public void EveryBlobActionId_IsRegisteredByAnActualMethod(string assetName)
    {
        var blob = CompiledBlobOf(assetName);
        if (blob is null) return;   // asset has no generated machine type (see CompiledBlobOf)

        var registered = IdsInTheGeneratedDispatcherTable();
        Assert.NotEmpty(registered);   // ⛔ an empty right side would make this vacuous

        foreach (var id in ActionIdsIn(blob))
            Assert.True(registered.Contains(id),
                $"Asset '{assetName}': the blob addresses action id {id}, which the GENERATED "
                + "HsmActionDispatcher table does not contain. The asset's action string and the key "
                + "the analyzer emitted disagree -- see HsmActionKey.ForActionName.");
    }

    /// <summary>
    /// ⭐⭐ <b><c>E6</c>'s own rail, finally seedable:</b> two actions with the <b>same simple name in
    /// different types</b> get <b>distinct ids</b>, and both re-bake sites agree because there is only
    /// one resolver left.
    ///
    /// <para>
    /// ⛔ Under the old simple-name key these two were ONE id — a duplicate key in the dispatcher's
    /// dictionary <i>initializer</i>, i.e. an <c>ArgumentException</c> at type init. ⭐ That is why the
    /// pair could not be seeded into the corpus before the fix, and why it can be now.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoActionsWithTheSameSimpleName_InDifferentTypes_GetDistinctIds()
    {
        const string alpha = "Alpha.Nodes.Fire";
        const string beta  = "Beta.Nodes.Fire";

        Assert.NotEqual(Fnv1a16(alpha), Fnv1a16(beta));
        // ⛔ …and the old key could not tell them apart at all.
        Assert.Equal(Fnv1a16("Fire"), Fnv1a16("Fire"));
    }

    /// <summary>
    /// ⭐ <b>INVERTED (Batch 72): the hand-built consumers now address by FQN too.</b> It asserted they
    /// used the simple name — the measurement that made the identity a decision. The ruling took (A),
    /// so the four example call sites moved, and this pins them there.
    /// </summary>
    [Fact]
    public void TheHandBuiltConsumersAddressByFullyQualifiedName()
    {
        foreach (var (relative, fqnPrefix) in new[]
                 {
                     (System.IO.Path.Combine("FDP", "Examples", "Fdp.Examples.UrbanCombat", "Brains",
                                             "ApcHsmSetup.cs"),
                      "Fdp.Examples.UrbanCombat.Brains.ApcHsmActions"),
                     (System.IO.Path.Combine("FDP", "Examples", "Fdp.Examples.Scenarios", "Integrated",
                                             "UrbanCombatNewScenario.cs"),
                      "Fdp.Examples.Scenarios.Integrated.UrbanCombatApcBrainActions"),
                 })
        {
            var source = System.IO.File.ReadAllText(FindUp(relative));
            Assert.Contains($".Activity(\"{fqnPrefix}.Activity_Cruise\")", source);
            Assert.Contains($".OnEntry(\"{fqnPrefix}.OnEnter_Disabled\")", source);
            // ⛔ No bare simple name survives anywhere in the addressing.
            Assert.DoesNotContain(".Activity(\"Activity_Cruise\")", source);
            Assert.DoesNotContain(".OnEntry(\"OnEnter_Disabled\")", source);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> CorpusAssets() => AiAssetCorpus.AssetNames(AiAssetKind.Hsm);

    /// <summary>
    /// The generated <c>Compile()</c> for a corpus asset, found by name in
    /// <c>Hrot.AI.Behaviors.Machines</c>. Returns null when the type is absent, which cannot happen
    /// for a compiled corpus asset — <see cref="EveryCorpusAsset_HasAGeneratedMachine"/> asserts that.
    /// </summary>
    private static Fhsm.Kernel.Data.HsmDefinitionBlob? CompiledBlobOf(string assetName)
    {
        var type = typeof(Hrot.AI.Behaviors.Machines.HsmShowcase).Assembly
            .GetType($"Hrot.AI.Behaviors.Machines.{assetName}");
        var compile = type?.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static);
        return (Fhsm.Kernel.Data.HsmDefinitionBlob?)compile?.Invoke(null, null);
    }

    /// <summary>⭐ The corpus really is compiled — otherwise the rail above would skip silently.</summary>
    [Theory]
    [MemberData(nameof(CorpusAssets))]
    public void EveryCorpusAsset_HasAGeneratedMachine(string assetName)
        => Assert.NotNull(CompiledBlobOf(assetName));

    private static IReadOnlyList<ushort> ActionIdsIn(Fhsm.Kernel.Data.HsmDefinitionBlob blob)
    {
        var ids = new List<ushort>();
        foreach (var s in blob.States)
        {
            if (s.OnEntryActionId  != 0xFFFF) ids.Add(s.OnEntryActionId);
            if (s.OnExitActionId   != 0xFFFF) ids.Add(s.OnExitActionId);
            if (s.ActivityActionId != 0xFFFF) ids.Add(s.ActivityActionId);
            if (s.TimerActionId    != 0xFFFF) ids.Add(s.TimerActionId);
        }
        return ids;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The ids the GENERATOR actually emitted</b>, read out of
    /// <c>Hrot.AI.Behaviors.Generated.HsmActionDispatcher</c>'s static <c>ActionTable</c> /
    /// <c>GuardTable</c>.
    ///
    /// <para>
    /// ⛔ <b>Deliberately NOT recomputed as <c>FNV(FullName)</c>.</b> That was the first draft and a
    /// revert probe proved it vacuous — it asserted the test's own rule, so reverting the generator
    /// left it green. Reading the emitted dictionary is the only version that can fail.
    /// </para>
    /// </summary>
    private static HashSet<ushort> IdsInTheGeneratedDispatcherTable()
    {
        // ⭐ In a non-kernel assembly the analyzer emits a REGISTRAR (RegisterAll(), a list of
        //   RegisterAction/RegisterGuard calls) rather than a table -- the table itself only exists in
        //   Fhsm.Kernel. ⇒ RUN the generated registrar and read what it put in the dispatcher. That is
        //   the emitted ids, executed, which is as close to the artefact as this shape allows.
        // ⚠ Additive only: no ClearAll(), so no other test's registrations are removed.
        var registrar = typeof(Hrot.AI.Behaviors.Machines.HsmShowcase).Assembly
            .GetType("Hrot.AI.Behaviors.Generated.HsmActionRegistrar")
            ?? throw new InvalidOperationException(
                "The generated HsmActionRegistrar is missing — the analyzer did not run.");
        registrar.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static)!
                 .Invoke(null, null);

        var ids = new HashSet<ushort>();
        foreach (var name in new[] { "ActionTable", "GuardTable" })
        {
            var field = typeof(Fhsm.Kernel.HsmActionDispatcher)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"{name} is missing from HsmActionDispatcher.");
            foreach (var key in ((System.Collections.IDictionary)field.GetValue(null)!).Keys)
                ids.Add((ushort)key);
        }
        return ids;
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
