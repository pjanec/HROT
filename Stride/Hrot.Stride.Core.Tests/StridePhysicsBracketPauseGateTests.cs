#nullable enable
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Scheduling;
using Fdp.Toolkit.Tkb.Domain;   // CollisionShapeKind
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// <c>CE-219</c> — Stride's OWN Bullet step must be gated on whether SIM time advances.
///
/// <para>
/// <b>The defect these rails pin.</b> The bracket gates the motors on <c>simRunning</c>, but nothing
/// gated <c>Stride.Physics.Simulation</c> itself: <c>PhysicsProcessor</c> steps Bullet from the render
/// loop, which keeps running when sim time does not. So a cluster-wide pause froze the motors and left
/// gravity and contacts integrating — bodies kept falling in a paused simulation. The fix is one call
/// into <see cref="IPhysicsBodyService.SetSimulationAdvancing"/> at the top of the pre-kernel step.
/// </para>
///
/// <para>
/// ⚠ <b>Why the gate needed a rail at all, and why THIS shape.</b> The dependency is an OPTIONAL
/// constructor parameter — deliberately, because a dozen test fakes implement
/// <see cref="IPhysicsBodyService"/> and would not compile against a required one. That is precisely
/// the silent-default shape this programme keeps finding: the gate looks present at the call site and
/// does nothing because the caller never passed the service it was holding. It happened here during
/// this very slice — both production <c>StridePhysicsBracket</c> constructions in
/// <c>EditorStrideSubsystem</c> initially omitted it. So the load-bearing assertion is not "the bracket
/// calls the method" but "the call actually reaches a service", and a rail that constructed the bracket
/// without one would have passed against the broken wiring.
/// </para>
///
/// <para>
/// ⛔ There is no <c>StridePhysicsBracket</c> suite to fold these into — measured, not assumed: the
/// bracket has production references only (<c>scripts/find.sh StridePhysicsBracket</c>, 16 hits, none in
/// a test file). Its collaborators each have their own suite; the bracket's own contract had none.
/// </para>
/// </summary>
public sealed class StridePhysicsBracketPauseGateTests
{
    /// <summary>
    /// Records only what <c>CE-219</c> is about. Every other member is a stub — the bracket calls none
    /// of them on these paths, and asserting on them would couple the rail to the motors' contracts.
    /// </summary>
    private sealed class AdvancingRecorder : IPhysicsBodyService
    {
        public List<bool> AdvancingCalls { get; } = new();

        public void SetSimulationAdvancing(bool advancing) => AdvancingCalls.Add(advancing);

        public object CreateBody(
            Entity entity, CollisionShapeKind shapeKind, ShapeDims dims, in SimTransform initialPose)
            => new object();
        public void RemoveBody(object bodyHandle) { }
        public void SetCharacterVelocity(object bodyHandle, global::Stride.Core.Mathematics.Vector3 velocity) { }
        public void Jump(object bodyHandle) { }
        public bool IsGrounded(object bodyHandle) => false;
        public void SetLinearVelocityXZ(object bodyHandle, global::Stride.Core.Mathematics.Vector3 v) { }
        public void SetYawRate(object bodyHandle, float radPerSec) { }
        public KinematicMoveResult MoveKinematic(
            object bodyHandle,
            global::Stride.Core.Mathematics.Vector3    desiredDelta,
            global::Stride.Core.Mathematics.Quaternion desiredRotDelta)
            => new KinematicMoveResult(desiredDelta, desiredRotDelta);
        public BodyState GetBodyState(object bodyHandle)
            => new BodyState(
                global::Stride.Core.Mathematics.Vector3.Zero,
                global::Stride.Core.Mathematics.Quaternion.Identity,
                global::Stride.Core.Mathematics.Vector3.Zero,
                global::Stride.Core.Mathematics.Vector3.Zero,
                IsKinematic: false);
    }

    private static StridePhysicsBracket BuildBracket(IPhysicsBodyService? service)
        => new StridePhysicsBracket(
            physicsIsActive:      false,   // no lifecycle system supplied; the gate runs before it anyway
            physicsBodyLifecycle: null,
            characterMotor:       null,
            vehicleMotor:         null,
            reverseSyncGroup:     new TogglablePostSimulationGroup("ReverseSyncGroup"),
            splitSync:            null,
            physicsBodyService:   service);

    /// <summary>
    /// ⭐ THE ONE THAT MATTERS. A halted frame must DISABLE Stride's simulation, and a running frame
    /// must re-enable it. Both directions, because a gate that only ever disables leaves the world
    /// frozen after the first pause — a worse failure than the one being fixed.
    /// </summary>
    [Fact]
    public void SimulationIsDisabledWhileHalted_AndReEnabledWhenAdvancing()
    {
        using var world = new EntityRepository();
        var service = new AdvancingRecorder();
        StridePhysicsBracket bracket = BuildBracket(service);

        bracket.RunPreKernelStep(world, dt: 0f,        simRunning: false);
        bracket.RunPreKernelStep(world, dt: 1f / 60f,  simRunning: true);
        bracket.RunPreKernelStep(world, dt: 0f,        simRunning: false);

        Assert.Equal(new[] { false, true, false }, service.AdvancingCalls);
    }

    /// <summary>
    /// The gate is told on EVERY frame, not only on transitions. <c>Simulation.DisableSimulation</c> is a
    /// process-wide static that anything in the engine may flip; re-asserting each frame means the
    /// bracket's view wins rather than whoever wrote it last.
    /// </summary>
    [Fact]
    public void TheGateIsAssertedEveryFrame_NotOnlyOnTransitions()
    {
        using var world = new EntityRepository();
        var service = new AdvancingRecorder();
        StridePhysicsBracket bracket = BuildBracket(service);

        for (int i = 0; i < 4; i++)
            bracket.RunPreKernelStep(world, dt: 0f, simRunning: false);

        Assert.Equal(4, service.AdvancingCalls.Count);
        Assert.All(service.AdvancingCalls, advancing => Assert.False(advancing));
    }

    /// <summary>
    /// ⚠ The complement, and the reason the optional parameter is safe: with no service the bracket must
    /// still run its remaining steps rather than throwing. This is what lets the headless suites and the
    /// no-op path construct a bracket without implementing a physics backend.
    /// </summary>
    [Fact]
    public void NoService_IsToleratedRatherThanThrowing()
    {
        using var world = new EntityRepository();
        StridePhysicsBracket bracket = BuildBracket(service: null);

        bracket.RunPreKernelStep(world, dt: 0f, simRunning: false);
        bracket.RunPreKernelStep(world, dt: 1f / 60f, simRunning: true);
    }
}
