using Hrot.CGF;
using FDP.Framework.Runner;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// In Phase 1 the CGF acts only as a heartbeating <see cref="CgfApplication.ClusterSlave"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem
{
    private CgfApplication? _app;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.08f, 0.22f, 0.38f, 1f);

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {
        _app = new CgfApplication(config.DomainId, nodeId: config.NodeId != 0 ? config.NodeId : 400);
    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        _app?.Tick();
    }

    /// <inheritdoc/>
    public void DrawWorld() { }

    /// <inheritdoc/>
    public void DrawUI() { }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _app?.Dispose();
        _app = null;
    }
}
