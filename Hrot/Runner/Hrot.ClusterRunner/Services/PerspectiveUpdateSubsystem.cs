using System.Numerics;
using Fdp.Engine.Runner;
using Hrot.ClusterRunner.Systems;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Thin <see cref="ISubsystem"/> wrapper around <see cref="PerspectiveCoordinatorSystem"/>.
/// Registered as the first subsystem in the orchestrator so perspective transitions are
/// processed before any other subsystem's <c>Update</c> runs.
///
/// <para>The coordinator is assigned after <c>SubsystemOrchestrator.Initialize()</c> via
/// <see cref="Coordinator"/>; until then <c>Update</c> is harmlessly a no-op.</para>
/// </summary>
internal sealed class PerspectiveUpdateSubsystem : ISubsystem
{
    /// <summary>
    /// Set this after <c>SubsystemOrchestrator.Initialize()</c> so the coordinator can
    /// receive an orchestrator reference.  <c>Update</c> is a no-op while this is <c>null</c>.
    /// </summary>
    internal PerspectiveCoordinatorSystem? Coordinator { get; set; }

    public string  Name          => "PerspectiveCoordinator";
    public Vector4 TitleBarColor => Vector4.Zero;

    public void Initialize(SubsystemConfig config) { }

    public void Update(float deltaTime) => Coordinator?.ProcessPendingEvents();

    public void DrawWorld() { }
    public void DrawUI()    { }
    public void Shutdown()  { }
}

