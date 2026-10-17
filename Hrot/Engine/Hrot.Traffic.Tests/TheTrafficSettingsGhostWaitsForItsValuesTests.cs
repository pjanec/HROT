using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Traffic;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ CE-4016 — a ghost of the traffic settings does not promote before the scenario's values arrive, so the traffic node's takeover
/// of the granted <c>dtTrafficDemand</c> never lands ahead of the creator's one in-window sample (F7).
///
/// <para>📌 Measured 2026-10-17 on a five-process cluster: CGF granted <c>dtTrafficDemand</c> to the traffic node, whose ghost promoted on
/// <c>EntityMaster</c> alone; <c>DeferredTakeoverSystem</c> recorded the grant that frame, the late <c>EntityTrafficDemand</c> sample was
/// dropped as the node's own, and SUMO never started — no cars. A Muscle's ghost is safe because it waits for <c>SimTransform</c>.</para>
/// </summary>
public sealed class TheTrafficSettingsGhostWaitsForItsValuesTests
{
    private const long DtTrafficDemand = 160;

    private static TkbTemplate TrafficTemplate()
    {
        var t = new TkbTemplate("Traffic", 8850);
        t.AddDescriptor(new TrafficDemandDto());
        return t;
    }

    [Fact]
    public void TheTrafficTemplate_RequiresTrafficDemand_BeforePromotion()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        var ingress = new DescriptorOwnershipMap();
        ingress.RegisterFromTranslator(DtTrafficDemand, new[] { GlobalComponentIds.TrafficDemand });

        var mandatory = new MandatoryComponentResolver()
            .Resolve(TrafficTemplate(), repo, Hrot.Core.Tkb.TkbTranslatorSet.Base(), ingress);

        Assert.Contains(GlobalComponentIds.TrafficDemand, mandatory);
    }

    [Fact]
    public void TheTemplate_KeepsTheInstancesValues()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<TrafficDemand>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new TrafficDemand { TargetCars = 7, Seed = 9, StepSeconds = 0.25f });

        new TrafficDemandTkbTranslator().Inject(repo, e, TrafficTemplate());

        Assert.Equal(7, repo.GetComponent<TrafficDemand>(e).TargetCars);
    }
}
