namespace Fdp.Core
{
    /// <summary>
    /// Group for systems that run during the initialization phase of a frame.
    /// </summary>
    public class InitializationSystemGroup : SystemGroup { }

    /// <summary>
    /// System group for input processing (doctrine ingress, command buffering, event ingress).
    /// <para>
    /// <b>Required registration order:</b> <c>InputSystemGroup</c> must be registered in the world
    /// <em>before</em> <see cref="SimulationSystemGroup"/> so that doctrine changes (and other
    /// input-phase mutations) take effect within the same frame as the brain-tick systems that
    /// consume them.
    /// </para>
    /// <para>
    /// <b>Cross-group constraint:</b> FDP's current scheduler does not support cross-group
    /// <c>[UpdateBefore]</c> / <c>[UpdateAfter]</c> ordering.  Host applications therefore
    /// <em>must</em> register groups manually in <c>Input → Simulation → PostSimulation</c> order.
    /// </para>
    /// <para>
    /// TODO: Add <c>[UpdateBefore(typeof(SimulationSystemGroup))]</c> here once cross-group
    /// attribute-based sorting is supported by the kernel scheduler.
    /// </para>
    /// </summary>
    public class InputSystemGroup : SystemGroup { }

    /// <summary>
    /// Group for systems that run during the main simulation logic phase.
    /// </summary>
    public class SimulationSystemGroup : SystemGroup { }

    /// <summary>
    /// Group for systems that run after the main simulation phase.
    /// Covers position integration (LinearKinematicsSystem), ballistics housekeeping
    /// (BallisticsSystem), vehicle kinematics (CarKinematicsSystem), and the spatial
    /// hash rebuild (SpatialHashSystem).
    /// <para>
    /// Execution order within the group is declared per-system via
    /// <c>[UpdateBefore]</c> / <c>[UpdateAfter]</c> attributes.
    /// </para>
    /// </summary>
    public class PostSimulationSystemGroup : SystemGroup { }

    /// <summary>
    /// Group for systems that run during the presentation/rendering phase.
    /// </summary>
    public class PresentationSystemGroup : SystemGroup { }

    /// <summary>
    /// Group for systems that run in the export/telemetry phase, after all simulation
    /// and presentation systems have completed.
    /// <para>
    /// Intended for read-only observers (logging, telemetry, record-and-replay) that
    /// must see the fully-committed frame state without mutating it.
    /// </para>
    /// <para>
    /// Registration order: <c>Input → Simulation → PostSimulation → Presentation → Export</c>.
    /// </para>
    /// </summary>
    public class ExportSystemGroup : SystemGroup { }
}
