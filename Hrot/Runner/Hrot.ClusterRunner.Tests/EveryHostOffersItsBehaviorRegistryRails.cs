using System.Text.RegularExpressions;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ <c>CE-11032</c> (the silent-default rule) — <b>a node bootstrapper that OWNS a behavior registry hands that instance to the context</b> through the one hook the shared bootstrapper reads
/// (<c>GetBehaviorRegistry()</c>): the shared bootstrapper puts it on <c>HrotNodeContext.BehaviorRegistry</c>, so a node extension (the gateway) reads names through the node's own registry. A source scan of the composition
/// roots (pattern: <c>EveryHostWithAMapPassesItToItsLoadChainRails</c>); the behaviour itself is <c>SharedApplicationBootstrapperTests.CE11032_*</c>.
/// 🔴 Red-proof: one bootstrapper's <c>GetBehaviorRegistry</c> returning null / not overridden, or the shared <c>BehaviorRegistry = …</c> line removed ⇒ that file is listed.
/// </summary>
public sealed class EveryHostOffersItsBehaviorRegistryRails
{
    private static string Source(string rel)
        => File.ReadAllText(Path.Combine(ContentLayout.TryFindSourceTree() ?? throw new DirectoryNotFoundException(AppContext.BaseDirectory), rel));

    public static IEnumerable<object[]> Owners() => new[]
    {
        new object[] { "Hrot/Subsystems/Hrot.CGF/CgfNodeBootstrapper.cs" },
        new object[] { "Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs" },
        new object[] { "Hrot/Subsystems/Hrot.Editor/EditorNodeBootstrapper.cs" },
    };

    [Theory]
    [MemberData(nameof(Owners))]
    public void ABootstrapperThatOwnsARegistry_ReturnsItFromTheOneHook(string file)
        => Assert.True(Regex.IsMatch(Source(file), @"override\s+BehaviorRegistry\?\s+GetBehaviorRegistry\(\)[\s\S]{0,900}return\s+(behaviorRegistry|BehaviorRegistry|_behaviorRegistry)\s*;"),
            $"{file} does not return its behavior registry from GetBehaviorRegistry()");

    [Fact]
    public void TheSharedBootstrapperPutsItOnTheContext()
        => Assert.Matches(@"context\s*=\s*context\s+with\s*\{\s*BehaviorRegistry\s*=", Source("Hrot/Engine/Hrot.Common/Infrastructure/SharedApplicationBootstrapper.cs"));
}
