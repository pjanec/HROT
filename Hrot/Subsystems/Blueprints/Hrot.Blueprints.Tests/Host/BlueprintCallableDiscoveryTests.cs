using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Q#12: the editor discovers CLR helpers marked <c>[BlueprintCallable]</c> and turns each into a curated
/// palette descriptor that drops a pre-configured <see cref="FunctionCallNode"/>. Verified against a probe
/// method in this (Hrot*) test assembly, which the discovery scan includes.
/// </summary>
public sealed class BlueprintCallableDiscoveryTests
{
    /// <summary>Nested (not a test class) so xUnit doesn't treat the probe as a test.</summary>
    public static class Probe
    {
        [BlueprintCallable("ProbeCategory", DisplayName = "Probe Add")]
        public static int Add(int a, int b) => a + b;
    }

    [Fact]
    public void Discover_FindsTaggedMethod_AsPreConfiguredFunctionCall()
    {
        var entries = BlueprintCallablePaletteEntries.Discover().ToList();

        var entry = entries.FirstOrDefault(d => d.Category == "ProbeCategory" && d.DisplayName == "Probe Add");
        Assert.NotNull(entry);

        var node = Assert.IsType<FunctionCallNode>(entry!.CreateInstance());
        Assert.Contains("Probe", node.TargetTypeId);   // declaring type surfaced
        Assert.Equal("Add", node.MethodName);
        Assert.True(node.IsPure);                       // default IsPure
    }

    [Fact]
    public void Discover_IgnoresUntaggedMethods()
    {
        var entries = BlueprintCallablePaletteEntries.Discover().ToList();
        // The untagged sibling must not appear.
        Assert.DoesNotContain(entries, d => d.DisplayName == "NotExposed");
    }

    public static class Probe2
    {
        public static int NotExposed(int a) => a; // no attribute → never discovered
    }
}
