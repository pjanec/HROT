using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Published synchronously before world-reset operations so systems can drop stale entity handles.
/// </summary>
[EventId(8101)]
public struct WorldResetEvent { }
