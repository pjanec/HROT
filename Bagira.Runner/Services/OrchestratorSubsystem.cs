using Bagira.Map.Common;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using FDP.Framework.Runner;

namespace Bagira.Runner.Services;

/// <summary>
/// Hosts <see cref="DrillMaster"/> (DDS control plane + ID allocator server) under the Runner process.
/// </summary>
public sealed class OrchestratorSubsystem : ISubsystem
{
    private DdsParticipant? _participant;
    private DrillMaster? _drillMaster;

    public string Name => "Orchestrator";

    public System.Numerics.Vector4 TitleBarColor => new(0.12f, 0.18f, 0.42f, 1f);

    public void Initialize(SubsystemConfig config)
    {
        _participant = BagiraEnvironment.CreateParticipant(config.DomainId);
        _drillMaster = new DrillMaster(_participant);
    }

    public void Update(float deltaTime)
    {
        _drillMaster?.Tick();
    }

    public void DrawWorld() { }

    public void DrawUI() { }

    public void Shutdown()
    {
        _drillMaster?.Dispose();
        _drillMaster = null;
        _participant?.Dispose();
        _participant = null;
    }
}
