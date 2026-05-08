using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.IG;

// -- Managed events (published via FdpEventBus.PublishManaged / ReadManagedEvents) --

/// <summary>
/// Sent from IG to ExCon when the operator selects a non-local context action.
/// Non-local means the action name does not start with "IG_".
/// </summary>
// ContextActionTriggered has been moved to Hrot.Common/Events/IgCommonEvents.cs so that
// GizmoInteractionIngressSystem (in Hrot.Network.NED) can also publish it without
// introducing a circular dependency.
