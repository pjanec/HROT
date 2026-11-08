using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Orchestration;

/// <summary>
/// ⭐ CE-4331 ⑥ — the ONE mapping of a node-id request between the asker's DDS sample (<see cref="NodeIdRequest"/>, written by <c>NodeIdBroker</c>) and the bus event the master answers (<see cref="NodeIdRequestEvent"/>,
/// published by the master host's <c>NedOrchestrationTranslator</c>), so the two never drift. A field added to the request is added HERE once.
/// </summary>
public static class NodeIdRequestWire
{
    /// <summary>The asker's sample. <paramref name="subsystemName"/> is the node's kind (null = none: a dynamic id).</summary>
    public static NodeIdRequest ToTopic(string computerName, int processId, int forcedId, int preferredId, string? subsystemName) => new()
    {
        ComputerHash  = NodeIdKeys.ComputerHash(computerName),
        ProcessId     = processId,
        ComputerName  = computerName,
        ForcedId      = forcedId,
        PreferredId   = preferredId,
        SubsystemName = subsystemName ?? string.Empty,
    };

    /// <summary>The master's event. An empty kind (an older sender) reads as none.</summary>
    public static NodeIdRequestEvent ToEvent(in NodeIdRequest request) => new()
    {
        ComputerName  = request.ComputerName ?? string.Empty,
        ProcessId     = request.ProcessId,
        ForcedId      = request.ForcedId,
        PreferredId   = request.PreferredId,
        SubsystemName = string.IsNullOrEmpty(request.SubsystemName) ? null : request.SubsystemName,
    };
}
