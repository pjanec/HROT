using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Interfaces;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐⭐ CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — EVERY translator that takes ingress and is not control plane can empty
/// its readers (<see cref="IDiscardableIngress"/>): a join drops the events that arrived while it could not use them, and a replay's or
/// review's exit drops the replay era's state and events before ingress resumes. 🔴 One without it keeps its replay-era samples and
/// writes them over the world the node just put back. ⭐ Swept over the assemblies that declare translators, so a translator added
/// later cannot forget it.
/// </summary>
public sealed class EveryIngressTranslatorCanDiscardRail
{
    /// <summary>Every assembly that declares a production translator (measured `2026-10-27`).</summary>
    private static readonly string[] TranslatorAssemblies =
        { "Fdp.Toolkits", "Fdp.Network.Cyclone", "Hrot.Network.NED", "Hrot.Network.BDC", "Hrot.Animation.Replication" };

    [Fact]
    public void CE3329_W5_Every_ingress_translator_that_is_not_control_plane_implements_IDiscardableIngress()
    {
        var swept = TranslatorAssemblies.Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && typeof(INetworkTranslator).IsAssignableFrom(t))
            .ToList();
        Assert.True(swept.Count > 100, $"swept only {swept.Count} translator types — an assembly is missing from the list");

        var missing = swept.Where(t => TakesWorldIngress(t) && !typeof(IDiscardableIngress).IsAssignableFrom(t))
                           .Select(t => t.FullName).OrderBy(n => n).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} ingress translator(s) cannot discard:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>Its direction includes ingress (or cannot be read without construction) and it is not control plane. ⚠ Read from an
    /// UNINITIALISED instance: every translator declares both as constants; a field-backed one reads its default — Ingress-less
    /// <c>None</c> is treated as ingress, <c>WorldState</c> as world: the fail-safe side.</summary>
    private static bool TakesWorldIngress(Type t)
    {
        var instance = (INetworkTranslator)RuntimeHelpers.GetUninitializedObject(t);
        TranslatorDirection direction;
        TranslatorClass category;
        try { direction = instance.Direction; } catch { direction = TranslatorDirection.None; }
        try { category = instance.Category; } catch { category = TranslatorClass.WorldState; }
        bool ingress = direction == TranslatorDirection.None || (direction & TranslatorDirection.Ingress) != 0;
        return ingress && category != TranslatorClass.ControlPlane;
    }
}
