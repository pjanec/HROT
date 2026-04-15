namespace Hrot.Core.Network;

/// <summary>
/// Ticks all DDS ingress/egress for the orchestrator master transport (one call per frame).
/// Called inside OrchestratorSubsystem.Update() during Phase 1, before SwapBuffers.
/// </summary>
public interface IOrchestrationTranslator : IDisposable
{
    void Tick();
}
