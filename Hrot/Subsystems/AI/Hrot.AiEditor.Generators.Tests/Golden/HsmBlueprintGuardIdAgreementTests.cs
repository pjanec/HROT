using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fdp.Toolkit.Blueprints;
using Fhsm.Kernel.Data;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// ⭐⭐⭐ <b>ACCEPTANCE RAIL ④ — the id the BLOB addresses for a blueprint guard EQUALS the id the
/// blueprint's registrar REGISTERS.</b>
/// 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §9 ④, §3.2, §13.7.
///
/// <para>🔴 <b>Why this could not be closed until now.</b> 📐 Measured `2026-09-27` across every
/// tracked <c>.bp.json</c>: <b>34 <c>BTreeAction</c>, 9 <c>BTreeCondition</c>, 2 <c>HsmAction</c>,
/// ZERO <c>HsmGuard</c></b>. The whole chain existed and nothing exercised it. <c>CE-397</c> authors
/// the two assets that do: <c>HsmGuardDemo.bp.json</c> (the first <c>HsmGuard</c> AiPrimitive in the
/// corpus) and <c>HsmPolledGuardDemo.hsm.json</c> (a POLLED transition guarded by it).</para>
///
/// <para>⛔⛔ <b>NEITHER SIDE RECOMPUTES THE KEY, and that is the whole discipline.</b>
/// <c>HsmActionIdAgreementTests</c>' header records why: its first draft recomputed the right side as
/// <c>FNV(FullName)</c>, and a revert probe left the test GREEN because it was asserting its own rule
/// rather than the generator's. ⇒ here the LEFT side is the <b>compiled blob</b> (asset → HSM
/// generator → <c>HsmFlattener</c> → <c>TransitionDef.GuardId</c>) and the RIGHT side is the
/// <b>dispatcher table after the blueprint's own generated registrar has run</b>. Two independent
/// artefacts, no arithmetic in this file.</para>
///
/// <para>⚠ <b>Registration is ADDITIVE</b> — no <c>ClearAll()</c>, mirroring the precedent, so this
/// rail cannot remove another test's registrations.</para>
/// </summary>
public sealed class HsmBlueprintGuardIdAgreementTests
{
    private const string MachineName   = "HsmPolledGuardDemo";
    private const string BlueprintName = "HsmGuardDemo";

    // ── artefact readers ─────────────────────────────────────────────────────

    private static Assembly BehaviorsAssembly
        => typeof(Hrot.AI.Behaviors.Machines.HsmShowcase).Assembly;

    /// <summary>The ONE generated type whose name starts with <paramref name="prefix"/>. ⚠ By prefix,
    /// not by the full <c>{Name}_{BlueprintId:X8}_Bp</c> spelling: hard-coding the hash here would
    /// make a re-hash a COMPILE error in the test rather than the finding it should be.</summary>
    private static Type FindGeneratedType(string prefix)
    {
        var matches = BehaviorsAssembly.GetTypes()
            .Where(t => t.Namespace == "Hrot.AI.Behaviors.Generated"
                     && t.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        matches.Should().ContainSingle(
            $"exactly one generated type should start with '{prefix}' — "
            + "zero means the blueprint generator did not run over the asset");
        return matches[0];
    }

    /// <summary>⭐ The LEFT side: compile the machine the way production does and read the ids the
    /// blob actually addresses. ⚠ <c>GuardId == 0</c> means "no guard" (<c>TransitionDef.cs:19</c>).</summary>
    private static IReadOnlyList<ushort> GuardIdsInTheCompiledBlob()
    {
        var blob = Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo.Compile();
        var ids  = new List<ushort>();
        foreach (var t in blob.Transitions)
            if (t.GuardId != 0) ids.Add(t.GuardId);
        return ids;
    }

    /// <summary>
    /// ⭐⭐⭐ The RIGHT side: RUN the blueprint's own generated <c>[BlueprintRegistrar]</c> — the same
    /// call <c>AiHotReloadCoordinator</c> drives at runtime — then read the keys out of
    /// <c>HsmActionDispatcher</c>'s private <c>GuardTable</c>.
    ///
    /// <para>⛔ Deliberately NOT <c>(ushort)Bp.BlueprintId</c>. That would be the blueprint half of the
    /// contract asserting itself; the <b>dispatcher table is what the kernel actually looks the guard
    /// up in</b>, so it is the only right-hand side that can fail for the real reason.</para>
    /// </summary>
    private static HashSet<ushort> GuardIdsTheRegistrarRegistered()
    {
        var registrar = FindGeneratedType($"BlueprintRegistrar_{BlueprintName}_");
        var register  = registrar.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
        register.Should().NotBeNull("the generated registrar must expose a static Register method");

        register!.Invoke(null, new object[] { new BlueprintRegistry().BeginStaging() });

        var table = typeof(Fhsm.Kernel.HsmActionDispatcher)
            .GetField("GuardTable", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GuardTable is missing from HsmActionDispatcher.");

        var ids = new HashSet<ushort>();
        foreach (var key in ((System.Collections.IDictionary)table.GetValue(null)!).Keys)
            ids.Add((ushort)key);
        return ids;
    }

    // ── ④ the rail itself ────────────────────────────────────────────────────

    /// <summary>
    /// 🔴🔴🔴 <b>RAIL ④.</b> Every guard id the compiled blob addresses is an id the blueprint's
    /// generated registrar actually put in the dispatcher's guard table.
    /// </summary>
    [Fact]
    public void TheBlobsBlueprintGuardId_IsRegisteredByTheBlueprintsOwnRegistrar()
    {
        var addressed  = GuardIdsInTheCompiledBlob();
        var registered = GuardIdsTheRegistrarRegistered();

        addressed.Should().NotBeEmpty(
            "the machine binds a blueprint guard — an empty left side would make this vacuous");
        registered.Should().NotBeEmpty(
            "the registrar must have registered something — an empty right side would make this vacuous");

        foreach (var id in addressed)
            registered.Should().Contain(id,
                $"the blob addresses guard id {id}, which the dispatcher's GuardTable does not hold "
              + "after the blueprint's own registrar ran — the baked id and the registered id "
              + "disagree (see HsmEmitCore's .GuardId(...) and CSharpEmitter's RegisterGuard).");
    }

    /// <summary>
    /// ⭐⭐ <b>The id is the BLUEPRINT's, and the asset addresses it by GUID.</b> ⚠ This is the second
    /// artefact pair — the generated class's own <c>BlueprintId</c> against the blob — and it is what
    /// says WHICH id agreement the rail above proved. ⛔ Still no arithmetic: both numbers are read.
    /// </summary>
    [Fact]
    public void TheAddressedId_IsTheBlueprintsOwnTruncatedBlueprintId()
    {
        var bpType = FindGeneratedType($"{BlueprintName}_");
        int blueprintId = (int)bpType.GetField("BlueprintId", BindingFlags.Public | BindingFlags.Static)!
            .GetRawConstantValue()!;

        GuardIdsInTheCompiledBlob().Should().Contain(unchecked((ushort)blueprintId),
            "CE-384 bakes (ushort)BlueprintId, which is the key RegisterGuard uses");
    }

    /// <summary>
    /// ⛔⛔ <b>No authorable NAME could have produced this id, which is the whole reason the explicit
    /// override exists (§3.2).</b> ⚠ Read from the artefact: the asset's transition names NO guard
    /// function at all, yet the blob carries a non-zero <c>GuardId</c>.
    /// </summary>
    [Fact]
    public void TheTransitionNamesNoGuardFunction_YetTheBlobCarriesAGuardId()
    {
        var dto = Hrot.AiEditor.Persistence.Hsm.HsmJsonServices.Deserialize(
            AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, MachineName))!;

        dto.Transitions.Should().OnlyContain(t => t.Guard == null || string.IsNullOrEmpty(t.Guard.MethodFqn),
            "the guard is hosted by a blueprint, not by a named method");
        dto.Transitions.Should().Contain(t => t.Guard != null && t.Guard.BlueprintAssetId != Guid.Empty,
            "the asset addresses the blueprint by GUID — the only handle that survives a rename");

        GuardIdsInTheCompiledBlob().Should().NotBeEmpty();
    }

    // ── the POLLED half of the user's acceptance description ─────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-381</c>'s normalisation, asserted on the ARTEFACT.</b> The transition is marked
    /// <c>IsPolled</c> and its event id is <c>ReservedEventIds.Polled</c> — ⛔ NOT <c>0</c>, which is
    /// the RTC loop's COMPLETION pass. 📄 §2.3: the two were deliberately never collapsed, and the
    /// asset authors <c>.On(0).Polled()</c>, so this is exactly the case the normalisation exists for.
    /// </summary>
    [Fact]
    public void TheGuardedTransitionIsPolled_AndNormalisedOffTheCompletionEvent()
    {
        var blob = Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo.Compile();

        var guarded = blob.Transitions.ToArray().Where(t => t.GuardId != 0).ToList();
        guarded.Should().ContainSingle();

        var t = guarded[0];
        t.Flags.HasFlag(TransitionFlags.IsPolled).Should().BeTrue();
        t.EventId.Should().Be(ReservedEventIds.Polled,
            "the flattener normalises a polled transition off event 0, or the completion pass would "
          + "select it too — the POLLED/COMPLETION conflation §10 ③ rejected");
        t.EventId.Should().NotBe(ReservedEventIds.Completion);
    }

    /// <summary>⭐ And the DERIVED state bit the kernel's per-tick gate tests, so the polled scan is
    /// reachable at all (<c>CE-381</c> + <c>CE-382</c>).</summary>
    [Fact]
    public void TheSourceStateCarriesTheDerivedPolledBit()
    {
        var blob = Hrot.AI.Behaviors.Machines.HsmPolledGuardDemo.Compile();

        blob.States.ToArray()
            .Should().Contain(s => s.Flags.HasFlag(StateFlags.HasPolledTransition),
                "AnyActiveStateHasAPolledTransition gates the whole polled arm on this bit");
    }
}
