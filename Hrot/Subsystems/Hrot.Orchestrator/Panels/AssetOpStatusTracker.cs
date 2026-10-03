using System;
using System.Collections.Generic;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// ⭐ CE-3021 — what the panel's Assets section shows about the publish / refresh it last sent: the request, and the
/// latest status the orchestrator reported for it (docs/DESIGN_Asset_Management.md §5, §10 D7). Pure state — the panel
/// feeds it the <see cref="ClusterOpCompletedEvent"/>s it sees, so it is testable without ImGui.
/// </summary>
public sealed class AssetOpStatusTracker
{
    public Guid WatchedRequestId { get; private set; }

    /// <summary>"Publish blueprint from node 3" — what was asked.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>The latest status for the watched request; null while none has arrived.</summary>
    public OrchestrationStatusCode? Status { get; private set; }

    public bool IsFinished => Status is OrchestrationStatusCode s && s != OrchestrationStatusCode.InProgress;

    public bool Failed => Status is OrchestrationStatusCode s && s.IsError();

    public void Watch(Guid requestId, string description)
    {
        WatchedRequestId = requestId;
        Description = description;
        Status = null;
    }

    public void Observe(IEnumerable<ClusterOpCompletedEvent> events)
    {
        if (WatchedRequestId == Guid.Empty) return;
        foreach (var e in events)
            if (e.RequestId == WatchedRequestId) Status = e.StatusCode;
    }
}
