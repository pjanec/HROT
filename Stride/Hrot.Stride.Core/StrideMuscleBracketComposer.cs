#nullable enable
using Fdp.Interfaces;

namespace Hrot.Stride.Core;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-252</c> — the ONE place the Stride physics collaborator chain is built.</b>
/// </summary>
/// <remarks>
/// <para><b>📌 What this replaces.</b> The chain — visual binding → body lifecycle → motors →
/// reverse-sync group → split sync → <see cref="StridePhysicsBracket"/> — was built in <b>THREE</b>
/// places: <c>EditorStrideSubsystem</c>'s self-contained arm and its hosted arm (steps 9–13b, measured
/// <b>byte-identical</b> to each other), and <c>StrideNodeShell.AttachPhysics</c> for mode 2. ⛔ Each
/// arm was a slightly larger superset of the last, which is the shape that rots worst: <b>a fix applied
/// to one silently misses two.</b></para>
///
/// <para>⭐ Ruling 9 classifies this as duplicate CODE ⇒ <b>route it</b>. 🔒 The user's standing
/// instruction on shared code is the same: <i>"something shareable, parametrizing shared code."</i></para>
///
/// <para><b>⚠ Every conditional here is LOAD-BEARING and was preserved verbatim from the call sites,
/// not simplified.</b> Each one encodes a defect someone already paid for:</para>
/// <list type="bullet">
///   <item><b>no visual factory ⇒ no visual binding ⇒ no lifecycle, no motors, no split sync.</b>
///     Headless tests pass a null factory and must still get a usable bracket.</item>
///   <item><b><c>physicsIsActive</c> is <c>physicsBodyService != null</c>, NOT "the service is
///     non-null after the fallback".</b> When false the bracket skips
///     <c>PhysicsBodyLifecycle.Execute</c>, so no phantom NoOp bodies are created and
///     <c>BulletReverseSyncSystem</c> cannot clobber <c>SimVelocity</c> (<c>STR-D11</c>).</item>
///   <item><b>the reverse-sync group is ALWAYS created</b>, even with no lifecycle, so the <c>P5</c>
///     replay handler always has a togglable post-sim group to sever (<c>STR-P5-T4</c>).</item>
///   <item><b><c>physicsBodyService</c> is always PASSED to the bracket</b> — <c>CE-219</c>/<c>CE-223</c>:
///     it is the pause gate, and an unpassed optional dependency there leaves gravity running while the
///     cluster is paused, with the gate looking present.</item>
/// </list>
///
/// <para>⛔ <b>What this deliberately does NOT do:</b> it does not build the visual factory and does not
/// choose the physics service. Those differ per host — mode 1 receives a service from
/// <c>StrideHrotGame</c> after <c>BeginRun</c>; mode 2 builds a
/// <c>BulletPhysicsBodyServiceDeferred</c> over its own scene. ⭐ The chain BELOW those two choices is
/// what was identical, and that is exactly what is shared here.</para>
/// </remarks>
public static class StrideMuscleBracketComposer
{
    /// <summary>The assembled chain, so a caller can keep the pieces it must expose.</summary>
    /// <param name="Bracket">The assembled bracket, ready to drive around <c>Kernel.Update()</c>.</param>
    /// <param name="VisualBinding">⭐ Null when no visual factory was supplied (headless).</param>
    /// <param name="PhysicsBodyService">The service actually used — the supplied one, or a NoOp.</param>
    /// <param name="Lifecycle">⭐ Null when there is no visual binding to resolve shapes from.</param>
    /// <param name="PhysicsIsActive">⛔ <c>false</c> when the caller supplied no real service.</param>
    public readonly record struct StrideMuscleBracket(
        StridePhysicsBracket        Bracket,
        StrideVisualBindingSystem?  VisualBinding,
        IPhysicsBodyService         PhysicsBodyService,
        PhysicsBodyLifecycleSystem? Lifecycle,
        bool                        PhysicsIsActive);

    /// <summary>
    /// Builds the chain. ⭐ Pass the host's real physics service when it has one; pass
    /// <see langword="null"/> for headless, which yields a NoOp service and
    /// <c>PhysicsIsActive == false</c>.
    /// </summary>
    public static StrideMuscleBracket Compose(
        IStrideVisualFactory?          visualFactory,
        IPhysicsBodyService?           physicsBodyService,
        ITkbDatabase?                  tkbDb,
        VehicleNavigationIntentSystem? vehicleNavIntentSystem)
    {
        // ── 9. Visual binding — only with a factory (headless passes null). ──────────────
        StrideVisualBindingSystem? visualBinding = null;
        if (visualFactory != null)
            visualBinding = new StrideVisualBindingSystem(visualFactory, tkbDb!);

        // ── 10. Service + lifecycle. ⛔ physicsIsActive reads the ARGUMENT, not the fallback. ──
        IPhysicsBodyService service = physicsBodyService ?? new NoOpPhysicsBodyService();
        bool physicsIsActive = physicsBodyService != null;

        PhysicsBodyLifecycleSystem? lifecycle = null;
        if (visualBinding != null)
            lifecycle = new PhysicsBodyLifecycleSystem(service, visualBinding);

        // ── 11. Motors — only with a lifecycle, which is what resolves body shapes. ───────
        BulletCharacterMotor?  characterMotor = null;
        KinematicVehicleMotor? vehicleMotor   = null;
        if (lifecycle != null)
        {
            characterMotor = new BulletCharacterMotor(service, lifecycle);
            vehicleMotor   = new KinematicVehicleMotor(service, lifecycle);
        }

        // ── 12. Reverse-sync group — ALWAYS created (STR-P5-T4; see the class remarks). ──
        var reverseSyncGroup = lifecycle != null
            ? new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
                  "BulletReverseSync", new BulletReverseSyncSystem(service, lifecycle))
            : new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup("BulletReverseSync");

        // ── 13. Split-authority sync — needs BOTH the binding and the factory. ───────────
        SplitAuthorityStrideSyncScript? splitSync = null;
        if (visualBinding != null && visualFactory != null)
            splitSync = new SplitAuthorityStrideSyncScript(visualBinding, visualFactory);

        // ── 13b. The bracket. ⭐ physicsBodyService is PASSED — CE-219's pause gate. ──────
        var bracket = new StridePhysicsBracket(
            physicsIsActive:      physicsIsActive,
            physicsBodyLifecycle: lifecycle,
            characterMotor:       characterMotor,
            vehicleMotor:         vehicleMotor,
            reverseSyncGroup:     reverseSyncGroup,
            splitSync:            splitSync,
            physicsBodyService:   service)
        {
            // ⭐ CE-246 — the one system that is NOT kernel-resident. A host that HAS it must pass it,
            //   or NavigationIntent never becomes steering and the node owns entities it never drives.
            VehicleNavIntentSystem = vehicleNavIntentSystem,
        };

        return new StrideMuscleBracket(bracket, visualBinding, service, lifecycle, physicsIsActive);
    }
}
