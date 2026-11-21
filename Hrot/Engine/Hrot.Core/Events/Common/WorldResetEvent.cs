using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Published synchronously before world-reset operations so systems can drop stale entity handles.
/// </summary>
[DataPolicy(DataPolicy.NoReplay)]   // ⭐ the coordinator's replay-UI-state handoff ② (CE-4049) — a UI command: never recorded, never re-fired by a player
[EventId(8101)]
public struct WorldResetEvent { }
