using System;
using System.Linq;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ The runner builds every subsystem it discovers by reflection (<c>Program.ScanForSubsystems</c> → <c>Program.FactoryConstructor</c>).
/// 📌 <c>2026-10-30</c>: CE-3347 gave <c>OrchestratorSubsystem</c>'s constructor an optional second parameter; the exact-signature lookup no
/// longer matched and the orchestrator was SILENTLY dropped — <c>--mode orchestrator</c> (and every mode that includes it) failed with
/// "Unknown subsystem name". ⇒ every production subsystem the runner can find must have a constructor it can call.
/// </summary>
public sealed class EverySubsystemTheRunnerFindsCanBeBuiltTests
{
    /// <summary>
    /// Every production <c>ISubsystem</c> the runner discovers (the deployment's Hrot.* / Fdp.* assemblies, test assemblies excluded) has a
    /// constructor whose first parameter is <c>INetworkFactory</c> and whose other parameters are optional — checked without constructing any.
    /// 🔴 Mutation: <c>FactoryConstructor</c> back to the exact <c>(INetworkFactory)</c> lookup ⇒ <c>OrchestratorSubsystem</c> is listed ⇒ red.
    /// </summary>
    [Fact]
    public void EveryDiscoveredSubsystem_HasAConstructorTheRunnerCanCall()
    {
        Hrot.Common.Infrastructure.DeploymentAssemblies.LoadAll();
        var production = Hrot.ClusterRunner.SubsystemFactory.Scan()
            .Where(t => t.Assembly.GetName().Name is { } n
                        && (n.StartsWith("Hrot.", StringComparison.Ordinal) || n.StartsWith("Fdp.", StringComparison.Ordinal))
                        && !n.EndsWith(".Tests", StringComparison.Ordinal) && !n.Contains(".Tests.", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(production, t => t == typeof(Hrot.Orchestrator.OrchestratorSubsystem));
        var unbuildable = production.Where(t => Hrot.ClusterRunner.SubsystemFactory.FactoryConstructor(t) is null).Select(t => t.FullName).ToList();
        Assert.True(unbuildable.Count == 0, "the runner cannot build: " + string.Join(", ", unbuildable));
    }
}
