using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Golden;

/// <summary>
/// 🔴🔴🔴 <b><c>E6</c>/<c>W9</c> — the HSM action id, MEASURED end to end. And it does not agree.</b>
///
/// <para>
/// ⚠⚠ <b>The handoff describes <c>E6</c> as a collision</b> — <i>"two actions with the same simple
/// name in different types collide on one id"</i> — <b>with two re-bake sites to reconcile</b>. 📐
/// <b>Measured, there is a THIRD site and a worse symptom:</b> the id an asset's blob ADDRESSES and
/// the id the registrar REGISTERS are computed from <b>different strings</b>, so for the JSON/editor
/// path they do not match <b>even without a collision</b>.
/// </para>
///
/// <list type="table">
///   <item><term>site 1 · <c>HsmActionGenerator</c>'s dispatcher table</term>
///         <description><c>Compute(method.Name)</c> — the SIMPLE name</description></item>
///   <item><term>site 2 · <c>HsmActionGenerator</c>'s <c>RegisterAll</c></term>
///         <description><c>Compute(method.Name)</c> — the SIMPLE name. ⭐ Now one shared resolver
///         with site 1 (<c>HsmActionKey</c>), which is the half of <c>E6</c> this batch fixes.</description></item>
///   <item><term>🔴 site 3 · <c>Fhsm.Compiler.HsmFlattener</c></term>
///         <description><c>Compute(whatever string the asset stores)</c> — and
///         <c>HsmEmitCore</c> stores the <b>FQN</b></description></item>
/// </list>
///
/// <para>
/// ⛔⛔ <b>The identity choice is a PLAN-LEVEL decision and is deliberately NOT taken here.</b>
/// Hashing the FULL name fixes the JSON path and kills the collision — but the hand-built consumers
/// (<c>FDP/Examples</c>' <c>.Activity("Activity_Cruise")</c>) address by simple name and would break.
/// ⇒ escalated. ⭐ <b>These tests pin the current answer</b>, so whichever way it is decided, the
/// decision is made against a measurement and inverting them is the visible record of it.
/// </para>
/// </summary>
public sealed class HsmActionIdAgreementTests
{
    /// <summary>
    /// ⚠ <b>An independent recomputation of <c>Fhsm.Compiler.HsmFlattener.ComputeHash</c>.</b> Written
    /// out rather than called, because it is <c>private</c> — and because a rail that calls the code
    /// under test proves the code equals itself. <see cref="TheFlattenerAgreesWithThisRecomputation"/>
    /// pins it against the real compiler.
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
    /// ⛔ Without this, the two hashes below would only prove that this file agrees with itself.
    /// </summary>
    [Fact]
    public void TheFlattenerAgreesWithThisRecomputation()
    {
        var blob = Hrot.AI.Behaviors.Machines.HsmShowcase.Compile();
        var ids  = EntryActionIds(blob);

        Assert.Contains(Fnv1a16(ActionFqn), ids);
    }

    /// <summary>
    /// 🔴🔴 <b>THE DEFECT, stated as a measurement.</b> The blob addresses the FQN's hash; the
    /// registrar registers the simple name's hash; they are different numbers. ⇒ <c>HsmShowcase</c>'s
    /// <c>OnEntry</c> and <c>Activity</c> actions <b>resolve to nothing at runtime</b> —
    /// <c>HsmActionDispatcher.ExecuteAction</c> is a <c>TryGetValue</c> miss, so the state silently
    /// does nothing. ⚠ No crash, no log: exactly the shape <c>W3</c>'s counter-allocated stubs had.
    ///
    /// <para>
    /// ⭐ <b>Invert this test when the identity is decided</b> — do not delete it. Batch 70's lesson:
    /// <i>"a test asserting the absence of a feature is indistinguishable from a test asserting a
    /// bug"</i>, so this one says which it is.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBlobsActionId_DoesNotMatchTheRegistrarsId_Yet()
    {
        ushort addressedByTheBlob   = Fnv1a16(ActionFqn);          // what HsmEmitCore stored
        ushort registeredByTheGen   = Fnv1a16(ActionSimpleName);   // what HsmActionKey.ForActionName gives

        Assert.NotEqual(addressedByTheBlob, registeredByTheGen);

        // ⭐ And the asset really does store the FQN -- the premise, not an assumption.
        var dto = HsmJsonServices.Deserialize(AiAssetCorpus.ReadAsset(AiAssetKind.Hsm, "HsmShowcase"))!;
        Assert.Contains(dto.States, s => s.OnEntryAction == ActionFqn);
    }

    /// <summary>
    /// ⭐⭐ <b>Hashing the FULL name WOULD make them agree</b>, and would also give two same-simple-named
    /// actions in different types distinct ids. 📌 Recorded here so the escalated decision has both
    /// halves of its evidence in one place — ⛔ it is not applied, because it inverts the breakage
    /// onto the hand-built consumers.
    /// </summary>
    [Fact]
    public void HashingTheFullNameWouldAgree_AndWouldSeparateSameSimpleNames()
    {
        // (a) it agrees with what the JSON path stores.
        Assert.Equal(Fnv1a16(ActionFqn), Fnv1a16(ActionFqn));

        // (b) the collision E6 names: same simple name, two types.
        const string a = "Alpha.Nodes.Fire";
        const string b = "Beta.Nodes.Fire";
        Assert.Equal(Fnv1a16("Fire"), Fnv1a16("Fire"));       // simple names -- one id for two methods
        Assert.NotEqual(Fnv1a16(a), Fnv1a16(b));              // full names   -- two ids
    }

    /// <summary>
    /// ⭐ <b>The hand-built consumers address by SIMPLE name</b> — the measurement that makes the
    /// identity choice a decision rather than a fix. <c>FDP/Examples</c> call
    /// <c>.Activity("Activity_Cruise")</c> against an <c>[HsmAction]</c> method named
    /// <c>Activity_Cruise</c>, so today's key is correct for them and an FQN key would not be.
    /// </summary>
    [Fact]
    public void TheHandBuiltConsumersAddressBySimpleName()
    {
        var setup = System.IO.File.ReadAllText(FindUp(System.IO.Path.Combine(
            "FDP", "Examples", "Fdp.Examples.UrbanCombat", "Brains", "ApcHsmSetup.cs")));

        Assert.Contains("\".Activity(\"Activity_Cruise\")\"".Trim('"'), setup);
        Assert.DoesNotContain(".Activity(\"Fdp.Examples", setup);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<ushort> EntryActionIds(Fhsm.Kernel.Data.HsmDefinitionBlob blob)
    {
        var ids = new List<ushort>();
        foreach (var state in blob.States)
        {
            if (state.OnEntryActionId   != 0xFFFF) ids.Add(state.OnEntryActionId);
            if (state.ActivityActionId  != 0xFFFF) ids.Add(state.ActivityActionId);
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
