using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Runner;
using Hrot.Core.Network;
using Hrot.ClusterRunner.Scenarios;
using Hrot.ClusterRunner.Services;

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐ CE-1450 — how the runner finds and builds its subsystems (moved out of <c>Program</c> so a rail can reach it): every non-abstract
/// <c>ISubsystem</c> in the loaded assemblies, built through ONE constructor rule.
/// </summary>
internal static class SubsystemFactory
{
    /// <summary>
    /// Scans all loaded assemblies for non-abstract ISubsystem implementations
    /// (excluding PerspectiveUpdateSubsystem and CiSubsystem, which are
    /// runner-internal or handled separately).
    ///
    /// CE-218: EyesAndMuscleSubsystem used to be excluded here too. It was the
    /// "tracer bullet" that proved the SoD async-module pattern before Stride
    /// existed (docs/designs/eyes-and-muscle/DESIGN.md, "Why EyesAndMuscle first,
    /// Stride later"); the patterns it proved now ship in EqsModule,
    /// CognitiveSpatialModule, NavigationSolverModule and AutonomousPerceptionModule,
    /// and it was never reachable from any --mode token. Retired.
    /// </summary>
    internal static IEnumerable<Type> Scan()
    {
        var subsystemType = typeof(ISubsystem);
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => t.IsClass && !t.IsAbstract
                     && subsystemType.IsAssignableFrom(t)
                     && t != typeof(PerspectiveUpdateSubsystem)
                     && t != typeof(CiSubsystem)
                     // ⭐ CE-1450 — the FDP examples ship with the runner (a project reference) but are no runnable node: their subsystems take no
                     //   INetworkFactory and were always skipped; excluded by name so the runner neither tries nor warns about them
                     && t.Assembly.GetName().Name?.StartsWith("Fdp.Examples.", StringComparison.Ordinal) != true);
    }

    /// <summary>
    /// The constructor the runner builds a subsystem with: first parameter <see cref="INetworkFactory"/>, every other parameter OPTIONAL.
    /// ⭐ An optional parameter added later (CE-3347's <c>ClusterConfiguration?</c> on <c>OrchestratorSubsystem</c>) must not drop the subsystem:
    /// the exact-signature lookup this replaces did, silently — <c>--mode orchestrator</c> answered "Unknown subsystem name".
    /// <see langword="null"/> when the type has none (rail: <c>EverySubsystemTheRunnerFindsCanBeBuiltTests</c>).
    /// </summary>
    internal static System.Reflection.ConstructorInfo? FactoryConstructor(Type type)
        => type.GetConstructors()
            .Where(c => c.GetParameters() is { Length: > 0 } p
                     && p[0].ParameterType == typeof(INetworkFactory)
                     && p.Skip(1).All(x => x.IsOptional))
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault();

    /// <summary>
    /// Instantiates an <see cref="ISubsystem"/> from its <see cref="Type"/> through <see cref="FactoryConstructor"/>, the optional parameters at their defaults.
    /// A discovered subsystem with no such constructor is REPORTED, never silently skipped.
    /// </summary>
    internal static ISubsystem? TryCreate(Type type, INetworkFactory networkFactory)
    {
        if (FactoryConstructor(type) is not { } ctor)
        {
            Console.Error.WriteLine($"[Runner] {type.Name} has no constructor taking an INetworkFactory (other parameters optional) — not available.");
            return null;
        }
        var args = ctor.GetParameters().Select((p, i) => i == 0 ? networkFactory : Type.Missing).ToArray();
        return (ISubsystem)ctor.Invoke(System.Reflection.BindingFlags.OptionalParamBinding, null, args, null);
    }
}
