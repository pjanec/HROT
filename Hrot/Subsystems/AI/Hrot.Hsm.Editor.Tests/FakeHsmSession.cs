using System;
using System.Collections.Generic;
using Fdp.Core;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Debug;
using Hrot.Hsm.Editor.Debug;

namespace Hrot.Hsm.Editor.Tests;

// Minimal stub of IHsmDebugSession for use in renderer tests.
// Only GetBreakpoints() and IsAttached are used by the renderers under test.
internal sealed class FakeHsmSession : IHsmDebugSession
{
    private readonly List<Breakpoint> _breakpoints = new();

    public bool IsAttached            => true;
    public bool IsPaused              => false;
    public bool IsAnyBreakpointActive => _breakpoints.Exists(b => b.Enabled);
    public Breakpoint? PausedAt       => null;
    public Entity?     PausedOnEntity => null;

    public bool HeatmapModeActive { get; set; }

    public void AddBreakpoint(Breakpoint bp) => _breakpoints.Add(bp);

    public IReadOnlyList<Breakpoint>           GetBreakpoints()                      => _breakpoints.AsReadOnly();
    public HsmInstanceSnapshot?                GetCurrentStateSnapshot()             => null;
    public IReadOnlyList<HsmTraceRecord>       GetRecentTraceHistory(int max = 100)  => Array.Empty<HsmTraceRecord>();
    public IReadOnlyDictionary<Guid, int>?     GetStateEntryCounts(Guid assetId)     => null;
    public IReadOnlyList<Entity>               GetActiveEntities(Guid assetId)       => Array.Empty<Entity>();

    public void Detach()               { }
    public void ResetStateEntryCounts() { }
    public void Continue()             { }
    public void StepOver()             { }
    public void StepInto()             { }
    public void StepOut()              { }
    public void Pause()                { }
    public void BeginObservingAsset(Guid assetId, Hrot.Editor.AiShared.Debug.TraceLevel level) { }
    public void EndObservingAsset(Guid assetId) { }

    public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
    public void ClearBreakpoint(BreakpointId id) { }
    public void ClearAllBreakpoints() { }

    public event Action<HsmBreakpointHit>?   OnBreakpointHit  { add { } remove { } }
    public event Action<HsmStateEntered>?    OnStateEntered   { add { } remove { } }
    public event Action<HsmStateExited>?     OnStateExited    { add { } remove { } }
    public event Action<HsmTransitionFired>? OnTransitionFired { add { } remove { } }
    public event Action<HsmEventQueued>?     OnEventQueued    { add { } remove { } }
    public event Action<HsmRegionConflict>?  OnRegionConflict { add { } remove { } }
    public event Action<HsmGuardEvaluated>?  OnGuardEvaluated { add { } remove { } }
    public event Action<HsmTimerEvent>?      OnTimerEvent     { add { } remove { } }
    public event Action?                     OnSessionStateChanged { add { } remove { } }
}
