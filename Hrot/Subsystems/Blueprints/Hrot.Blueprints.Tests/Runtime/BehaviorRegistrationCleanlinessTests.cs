using System.Reflection;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// ⭐⭐⭐ <b><c>G4</c> — the shipped behaviour set registers CLEANLY, under both halves of the guard.</b>
///
/// <para>
/// ⛔ <b>A guard that throws is only half a deliverable.</b> <c>BehaviorRegistry.Register</c> now
/// refuses a duplicate NAME (Phase 1e, already shipped) and — since <c>G4</c> — a duplicate ID. ⚠ If
/// the real corpus tripped either one, the guard would turn a silent wrong-behaviour bug into a
/// startup crash and the batch would have shipped an outage. ⇒ <b>this is the test that says it does
/// not.</b>
/// </para>
///
/// <para>
/// ⭐⭐ <b>Driven through the PRODUCTION scanner</b>, <c>BlueprintRegistrarScanner.Scan</c> — the same
/// entry point the host uses, invoking every <c>[BlueprintRegistrar]</c> in the assembly. ⛔ A test
/// that hand-registered a list of names would prove something about the list, not about the corpus.
/// </para>
///
/// <para>
/// ⚠ <b>Vacuity is guarded explicitly.</b> An assembly with zero registrars also fails to collide, so
/// the count is asserted non-zero — otherwise this test would stay green if registration stopped
/// happening at all, which is the <c>BP-223</c> shape.
/// </para>
/// </summary>
public sealed class BehaviorRegistrationCleanlinessTests
{
    /// <summary>⚠ The registrars live in <c>Hrot.AI.Behaviors</c>; nothing in a bare test host has
    /// loaded it. Touching a type forces the load, as the golden corpus does for <c>BP1602</c>.</summary>
    private static Assembly BehaviorAssembly => typeof(Hrot.AI.Behaviors.BpComponentDemo).Assembly;

    [Fact]
    public void TheShippedRegistrarsProduceNoNameOrIdCollision()
    {
        var blueprints = new BlueprintRegistryStaging();
        var behaviors  = new BehaviorRegistry();

        // ⭐ skipOnUnknownParam: this assembly carries registrars whose parameters the scanner does
        //   not resolve (the host passes the same flag). ⛔ It skips a REGISTRAR, never a collision.
        var ex = Record.Exception(() =>
            BlueprintRegistrarScanner.Scan(BehaviorAssembly, blueprints, behaviors,
                skipOnUnknownParam: true));

        Assert.True(ex is null,
            "the shipped behaviour registrars collide with each other — G4's guard turns this into a "
            + "startup failure, so it must be resolved before the guard ships:\n" + ex?.Message);

        Assert.True(blueprints.StagedBlueprintIds.Count > 0,
            "no registrar ran at all — this test would pass for an assembly that registers nothing, "
            + "which is the failure mode it exists to exclude.");
    }

    /// <summary>
    /// ⭐ <b>The id half, stated directly over the names rather than inferred from "it did not
    /// throw".</b> ⚠ The scan above only reaches names that a registrar actually registers; this
    /// asserts <see cref="BehaviorHash.FromName"/> is injective over <b>every</b> registered name, so
    /// the claim is about the id space and not about one code path through it.
    /// </summary>
    [Fact]
    public void EveryRegisteredBehaviorNameHashesToADistinctId()
    {
        var blueprints = new BlueprintRegistryStaging();
        var behaviors  = new BehaviorRegistry();
        BlueprintRegistrarScanner.Scan(BehaviorAssembly, blueprints, behaviors, skipOnUnknownParam: true);

        var byId = new Dictionary<int, string>();
        var collisions = new List<string>();

        foreach (var name in behaviors.GetRegisteredNames())
        {
            int id = BehaviorHash.FromName(name);
            if (byId.TryGetValue(id, out var other))
                collisions.Add($"0x{id:X8}: '{name}' and '{other}'");
            else
                byId[id] = name;
        }

        Assert.True(collisions.Count == 0,
            "behaviour names whose FNV-1a-32 ids collide:\n  " + string.Join("\n  ", collisions));
    }
}
