namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    // Generic ECS-free source interface for non-ECS gizmo producers
    // (standalone tools, test harnesses, remote viewers).
    // FDP-specific producers (IStatefulGizmo, IStatelessGizmo) are NOT sub-interfaces
    // of this — they live in FDP only and are unrelated to IGizmoSource.
    public interface IGizmoSource
    {
        // Called once per frame; emit primitives into 'draw'.
        void Emit(float deltaTime, IGizmoDrawBuilder draw);
    }
}
