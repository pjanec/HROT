using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests;

/// <summary>
/// ⭐ MEASUREMENT (CE-275 / DataPolicy) — which SAVE CONTEXT each ownership component lands in. Settles the
/// "does scenario save write NetworkAuthority, and does NoSave affect the checkpoint?" question the corpus
/// (R-140) flagged as unmeasured. Scenario save = <c>GetSaveableMask</c> (excludes <c>NoSave</c>); the .fdp
/// checkpoint recording = <c>GetRecordableMask</c> (excludes <c>NoRecord</c>) — they are INDEPENDENT bits.
/// </summary>
public sealed class DataPolicySaveContextMeasurement
{
    [ComponentId(231)]
    private struct PlainPos { public float X; }

    [Fact]
    public void NetworkAuthority_vs_NetworkOwnership_LandInDifferentSaveContexts()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkAuthority>();   // no DataPolicy attribute
        repo.RegisterComponent<NetworkOwnership>();    // [DataPolicy(NoSave)]
        repo.RegisterComponent<PlainPos>();

        int authId  = ComponentTypeRegistry.GetId(typeof(NetworkAuthority));
        int ownId   = ComponentTypeRegistry.GetId(typeof(NetworkOwnership));

        var saveable   = repo.GetSaveableMask();     // SCENARIO persist (NoSave excluded)
        var recordable = repo.GetRecordableMask();   // .fdp CHECKPOINT recording (NoRecord excluded)

        // ── Scenario save context (GetSaveableMask / NoSave) ──────────────────────
        Assert.True(saveable.IsSet(authId),
            "MEASURED: NetworkAuthority IS in the scenario saveable mask (no NoSave) → scenario save WRITES it.");
        Assert.False(saveable.IsSet(ownId),
            "MEASURED: NetworkOwnership is NOT in the scenario saveable mask (it has [DataPolicy(NoSave)]).");

        // ── .fdp checkpoint context (GetRecordableMask / NoRecord) ────────────────
        Assert.True(recordable.IsSet(authId),
            "MEASURED: NetworkAuthority IS in the .fdp recordable mask (no NoRecord).");
        Assert.True(recordable.IsSet(ownId),
            "MEASURED: NetworkOwnership IS in the .fdp recordable mask too — NoSave does NOT remove it from the "
          + "checkpoint recording. ⇒ scenario (NoSave) and checkpoint (.fdp/NoRecord) are INDEPENDENT flags.");
    }

    [Fact]
    public void ScenarioSave_RoundTrip_WritesNetworkAuthority_ButNotNetworkOwnership()
    {
        ComponentTypeRegistry.Clear();
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkAuthority>();
        repo.RegisterComponent<NetworkOwnership>();
        repo.RegisterComponent<PlainPos>();

        var e = repo.CreateEntity();
        repo.SetComponent(e, new PlainPos { X = 1f });
        repo.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
        repo.AddComponent(e, new NetworkOwnership { PrimaryOwnerId = 1, LocalNodeId = 1 });

        var dom  = new ScenarioSerializerBuilder("Hrot.Scenario").Build()
                        .Serialize(repo, new ScenarioHeader("Hrot.Scenario"));
        var json = dom.ToJsonString();

        // MEASURED end-to-end: the live scenario save path (ScenarioSerializer) DOES emit NetworkAuthority,
        // and DOES exclude NetworkOwnership (NoSave). So today's scenario.json genuinely carries NetworkAuthority.
        Assert.Contains("NetworkAuthority", json);
        Assert.DoesNotContain("NetworkOwnership", json);
    }
}
