namespace Hrot.ScenarioEditor.Events;

/// <summary>
/// Published synchronously before <c>EntityRepository.Clear()</c> when a world reset
/// is triggered (new scenario, load scenario).
/// Consumers flush any cached <see cref="Fdp.Core.Entity"/> handles immediately on receipt
/// to prevent stale-pointer access after the repository is wiped.
/// </summary>
public sealed class WorldResetEvent { }
