using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Spatial.Eqs
{
    // Phase enum for the per-sensor cross-tick state machine.
    public enum EqsEvalPhase
    {
        Idle              = 0, // No evaluation in progress; ready for next query.
        Evaluating        = 1, // Running generation + filtering (unused in current impl, reserved).
        _AwaitingRaycasts = 2, // Some candidates have FlagPendingRay; waiting for ring buffer.
        Finalizing        = 3, // All raycasts resolved; sort + write next tick (reserved).
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.SensorEvalState)]
    public struct SensorEvalState
    {
        // Current phase of this sensor's evaluation.
        public EqsEvalPhase Phase;

        // How many RaycastRequestEvents have been submitted but not yet resolved.
        // Used to short-circuit polling when budget was exhausted mid-batch.
        public int PendingRaycastCount;

        // Tick at which the sensor entered _AwaitingRaycasts (diagnostic).
        public uint AwaitingSinceTick;

        // Snapshot of sensor.Epoch when evaluation started.
        // If sensor.Epoch changes, evalState is reset.
        public uint CurrentEpoch;

        // Tracks the StructureHash of the template currently evaluated.
        // Used by EqsSolverSystem to detect structural hot-reloads (hard reset).
        public ulong CurrentStructureHash;
    }

    [ComponentId(GlobalComponentIds.EqsSolverGlobalState)]
    public struct EqsSolverGlobalState
    {
        // Maximum RaycastRequestEvents the EQS system is allowed to submit per EqsModule tick.
        // All sensors share this budget. Default: 2048.
        public int MaxAccurateRaycastsPerSolverTick;

        // Running count reset at the start of each EqsModule.Tick before the solver runs.
        public int AccurateRaysSubmittedThisTick;
    }
}
