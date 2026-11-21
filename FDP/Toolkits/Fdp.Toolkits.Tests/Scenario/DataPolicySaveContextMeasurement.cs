using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests;

/// <summary>
/// ⭐ CE-277(e) POLICY RAIL (was the CE-275/DataPolicy measurement) — asserts the APPLIED policy: the
/// network-managed identity/ownership components carry <c>[DataPolicy(NoScenario)]</c> so scenario save
/// never writes them, while remaining independent of the .fdp checkpoint (<c>NoReplay</c>) axis.
///
/// Scenario save  = <c>GetSaveableMask</c>  (excludes <c>NoScenario</c>).
/// .fdp checkpoint = <c>GetRecordableMask</c> (excludes <c>NoReplay</c>).
/// The two bits are INDEPENDENT — a component may be excluded from one and kept in the other.
/// </summary>
public sealed class DataPolicySaveContextMeasurement
{
    [ComponentId(231)]
    private struct PlainPos { public float X; }

    [Fact]
    public void OwnershipComponents_AreExcludedFromScenario_ButKeptForCheckpoint()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkAuthority>();   // [DataPolicy(NoScenario)]  (CE-277(e))
        repo.RegisterComponent<PlainPos>();            // no policy → plain saveable

        int authId  = ComponentTypeRegistry.GetId(typeof(NetworkAuthority));
        int plainId = ComponentTypeRegistry.GetId(typeof(PlainPos));

        var saveable   = repo.GetSaveableMask();     // SCENARIO persist (NoScenario excluded)
        var recordable = repo.GetRecordableMask();   // .fdp CHECKPOINT recording (NoReplay excluded)

        // ── Scenario save context (GetSaveableMask / NoScenario) ──────────────────────
        Assert.False(saveable.IsSet(authId),
            "CE-277(e): NetworkAuthority now carries [DataPolicy(NoScenario)] → excluded from scenario save.");
        Assert.True(saveable.IsSet(plainId),
            "A component with no policy is saveable — proving the exclusion above is the flag, not a blanket drop.");

        // ── .fdp checkpoint context (GetRecordableMask / NoReplay) — INDEPENDENT bit ──
        Assert.True(recordable.IsSet(authId),
            "NetworkAuthority has NoScenario but NOT NoReplay → still recorded in the .fdp checkpoint. "
          + "⇒ scenario (NoScenario) and checkpoint (NoReplay) are INDEPENDENT flags.");
    }

    [Fact]
    public void ScenarioSave_RoundTrip_ExcludesOwnershipComponents()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<PlainPos>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new PlainPos { X = 1f });
        repo.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));

        var dom  = new ScenarioSerializerBuilder("Hrot.Scenario").Build()
                        .Serialize(repo, new ScenarioHeader("Hrot.Scenario"));
        var json = dom.ToJsonString();

        // MEASURED end-to-end: after CE-277(e) the live scenario save path (ScenarioSerializer) emits
        // NO NetworkAuthority — it is [DataPolicy(NoScenario)] — while the plain component still
        // round-trips.
        Assert.Contains("PlainPos", json);
        Assert.DoesNotContain("NetworkAuthority", json);
    }
}
