using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Fdp.Interfaces;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐ <c>R-4007</c> (Q91 §4-M) — every production translator that declares <see cref="TranslatorClass.WorldEvent"/> can
/// DISCARD what a joining node may not use (<see cref="IDiscardableIngress"/>). ⛔ Without it the join gate would cast-fail
/// every frame — and a no-op default would let the KeepAll queue burst stale shots at the opening. Also pins the five
/// event translators the design classified, so a reclassification is a deliberate edit, not a drift.
/// </summary>
public sealed class EveryWorldEventTranslatorCanDiscardTests
{
    [Fact]
    public void EveryWorldEventTranslator_ImplementsDiscard_AndTheClassifiedFiveAreEvents()
    {
        var assembly = typeof(Hrot.Map.Common.Replication.Ingress.EntityMasterIngressTranslator).Assembly;
        var events = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(INetworkTranslator).IsAssignableFrom(t))
            .Where(t => CategoryOf(t) == TranslatorClass.WorldEvent)
            .ToList();

        Assert.All(events, t => Assert.True(typeof(IDiscardableIngress).IsAssignableFrom(t),
            $"{t.Name} declares WorldEvent but cannot discard (IDiscardableIngress)."));
        Assert.Equal(
            new[] { "EntityHitDamageIngressTranslator", "MissionControlIngressTranslator", "MunitionDetonationIngressTranslator",
                    "WeaponFireIngressTranslator", "WeaponFireRequestIngressTranslator" },
            events.Select(t => t.Name).OrderBy(n => n).ToArray());
    }

    /// <summary>Reads the expression-bodied <c>Category</c> without constructing the translator (no DDS participant).</summary>
    private static TranslatorClass CategoryOf(Type t)
    {
        try { return ((INetworkTranslator)RuntimeHelpers.GetUninitializedObject(t)).Category; }
        catch { return TranslatorClass.WorldState; }
    }
}
