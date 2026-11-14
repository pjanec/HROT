#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Hrot.Stride.Core;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// 🔴🔴 <b>CE-223 — the rail that would have caught a green suite hiding a dead feature.</b>
/// </summary>
/// <remarks>
/// <para><b>The defect this exists for.</b> <c>IPhysicsBodyService.SetSimulationAdvancing</c> is
/// declared with a <b>default interface implementation whose body is empty</b>, so that the dozen test
/// fakes need not implement it. <c>BulletPhysicsBodyServiceDeferred</c> — the service the LIVE app
/// constructs — forwarded every other member to its inner service and had no member for this one, so
/// it inherited that empty body. <c>CE-219</c>'s pause gate was therefore called every frame, with the
/// right value, and did nothing: gravity and contacts ran while the simulation clock was halted and
/// bodies fell in a paused world.</para>
///
/// <para>⛔⛔ <b>Why <c>StridePhysicsBracketPauseGateTests</c> could not see it.</b> That suite asserts
/// the BRACKET calls the service — and it asserts it against its own fake, which does implement the
/// method. It is green and correct, and the feature was still dead. <c>R-142</c> ③: when a suite stays
/// green while the feature is broken, the finding is the blindness, and it is fixed in place rather
/// than routed around. This rail is that fix: it asserts the PRODUCTION implementors, which is the hop
/// the bracket rail structurally cannot reach.</para>
///
/// <para>⚠ <b>Why a new class rather than a test folded into the bracket suite</b> (<c>R-142</c> ④
/// prefers the latter): <c>StridePhysicsBracketPauseGateTests</c> lives in
/// <c>Hrot.Stride.Core.Tests</c>, and the implementor it needs to inspect —
/// <c>BulletPhysicsBodyServiceDeferred</c> — lives in <c>HrotStrideApp.Game</c>, which
/// <c>Hrot.Stride.Core</c> does not and must not reference. The assembly boundary forces the split.</para>
/// </remarks>
public sealed class PhysicsPauseGateIsImplementedByProductionServicesTests
{
    /// <summary>
    /// Every production <see cref="IPhysicsBodyService"/> in the Stride host must DECLARE
    /// <c>SetSimulationAdvancing</c> — inheriting the interface's empty default is the defect.
    ///
    /// <para>⭐ Written as a sweep over the assembly rather than as a named check on one class, so the
    /// next service or wrapper added here is covered the day it appears rather than the day someone
    /// watches a tank fall through the floor.</para>
    /// </summary>
    [Fact]
    public void EveryProductionPhysicsBodyServiceDeclaresThePauseGate()
    {
        Type[] implementors = typeof(BulletPhysicsBodyService).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(IPhysicsBodyService).IsAssignableFrom(t))
            // ⛔ NoOpPhysicsBodyService is the deliberate do-nothing service for headless/CI hosts with
            //    no running Simulation. Inheriting the empty default is CORRECT there, and only there.
            .Where(t => t.Name != "NoOpPhysicsBodyService")
            .ToArray();

        // ⛔ Anti-vacuity: if the filter ever stops matching anything, this rail must fail rather than
        //    pass by finding nothing to check.
        Assert.NotEmpty(implementors);

        foreach (Type t in implementors)
        {
            MethodInfo? declared = t.GetMethod(
                nameof(IPhysicsBodyService.SetSimulationAdvancing),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.True(declared != null,
                $"CE-223: '{t.Name}' implements IPhysicsBodyService but does not DECLARE " +
                $"SetSimulationAdvancing, so it silently inherits the interface's empty default. " +
                $"A pause gate that compiles, is called every frame and does nothing is how bodies " +
                $"came to keep falling in a paused simulation. Forward it, or set " +
                $"Simulation.DisableSimulation directly.");
        }
    }

    /// <summary>
    /// The live app's service is the DEFERRED wrapper, so name it explicitly: a sweep can be weakened
    /// by a filter edit, and this is the one implementor the defect actually shipped on.
    /// </summary>
    [Fact]
    public void TheDeferredWrapperTheLiveAppConstructsDeclaresThePauseGate()
    {
        MethodInfo? declared = typeof(BulletPhysicsBodyServiceDeferred).GetMethod(
            nameof(IPhysicsBodyService.SetSimulationAdvancing),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.True(declared != null,
            "CE-223: BulletPhysicsBodyServiceDeferred is what StrideHrotGame constructs in the live " +
            "app. Without its own SetSimulationAdvancing it inherits the interface's empty default " +
            "and CE-219's pause gate is inert in production while every rail stays green.");
    }
}
