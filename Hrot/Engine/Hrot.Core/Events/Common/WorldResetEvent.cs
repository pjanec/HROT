namespace Hrot.Common.Events;

/// <summary>
/// Published synchronously before world-reset operations so systems can drop stale entity handles.
/// </summary>
public sealed class WorldResetEvent { }

