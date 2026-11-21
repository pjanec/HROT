using System.Reflection;
using CycloneDDS.Schema;
using Sim.Replication.Dds;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ Every <c>[DdsTopic]</c> type the traffic wire uses carries the generated <c>GetDescriptorOps</c> — the CycloneDDS
/// code generator ran for <c>Sim.Replication.Dds</c>.
///
/// <para>📌 Measured <c>2026-10-17</c> on a five-process cluster: every ECS node crashed in <c>TrafficMirrorSystem</c> with
/// <i>"Type 'DdsWireFrame' does not have a public static GetDescriptorOps() method"</i>. The vendored SumoSharp's
/// <c>Directory.Build.targets</c> gave the single-target project a <c>TargetFrameworks</c>, NuGet restored it as
/// multi-target, and the generator's switch (<c>CycloneDDS.NET.props</c>) was imported under a condition a normal
/// build never meets. ⛔ No unit suite opens a real DDS reader, so nothing caught it — this rail needs no native DDS.</para>
/// </summary>
public sealed class TrafficDdsTopicsAreGeneratedTests
{
    [Fact]
    public void EveryTrafficDdsTopic_HasItsGeneratedDescriptor()
    {
        var topics = typeof(DdsWireFrame).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<DdsTopicAttribute>() != null)
            .ToList();
        Assert.Contains(typeof(DdsWireFrame), topics);

        var missing = topics
            .Where(t => t.GetMethod("GetDescriptorOps", BindingFlags.Public | BindingFlags.Static) == null)
            .Select(t => t.Name)
            .ToList();
        Assert.True(missing.Count == 0,
            "CycloneDDS code generation did not run for Sim.Replication.Dds (check FDP/ExtDeps/SumoSharp/Directory.Build.targets): "
          + string.Join(", ", missing));
    }
}
