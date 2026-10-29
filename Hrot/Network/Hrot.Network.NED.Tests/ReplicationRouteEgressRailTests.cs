using Fdp.Testing;
using System;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐ AQ93 EM-4 (R-4045) — the default (NED) egress publishes only entities on the DEFAULT replication route. Two rails:
/// the helper itself excludes a routed entity, and no NED publisher builds its query around the helper.
/// </summary>
public sealed class ReplicationRouteEgressRailTests
{
    [Fact]
    public void DefaultRoutePublishable_KeepsAPlainNetworkedEntity_AndDropsARoutedOne()
    {
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<ReplicationRoute>();
        var plain = repo.CreateEntity();
        repo.AddComponent(plain, new NetworkIdentity(1));
        var routed = repo.CreateEntity();
        repo.AddComponent(routed, new NetworkIdentity(2));
        repo.AddComponent(routed, new ReplicationRoute(ReplicationRoute.SumoSharp));
        var local = repo.CreateEntity();   // no network id at all

        var found = new System.Collections.Generic.List<Entity>();
        repo.Query().DefaultRoutePublishable().Build().ForEach(found.Add);

        Assert.Equal(new[] { plain }, found);
    }

    /// <summary>
    /// A NED file that PUBLISHES (it gates on <c>HasAuthority</c>) must select its entities through
    /// <c>DefaultRoutePublishable()</c>, never a raw <c>.With&lt;NetworkIdentity&gt;()</c> — otherwise an entity another
    /// mechanism publishes (a SUMO car) would also go out on NED.
    /// </summary>
    [Fact]
    public void EveryNedPublisher_SelectsThroughTheDefaultRoute()
    {
        var ned = Path.Combine(TestRepo.Root, "Hrot", "Network", "Hrot.Network.NED");

        var offenders = Directory.EnumerateFiles(ned, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => { var t = File.ReadAllText(f); return t.Contains("HasAuthority(") && t.Contains(".With<NetworkIdentity>()"); })
            .Select(f => Path.GetRelativePath(ned, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These NED publishers select with a raw .With<NetworkIdentity>() — use .DefaultRoutePublishable() (AQ93 EM-4): "
          + string.Join(", ", offenders));
    }
}
