using Fdp.Core;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Editor.AiShared.Debug;
using NodeEditor.Core.Action;

namespace Hrot.Blueprints.Tests.Debug;

// ============================================================================
// BATCH-09 — AiDebugCommandsTests (MTB-P3-T5)
//
// Verifies the polymorphic AI Debug toolbar command registrar with fake
// IDebugSessionRegistry + fake IAiDebugSession + fake IBlueprintDebugSession.
// ============================================================================

// ─── Fakes ────────────────────────────────────────────────────────────────────

/// <summary>
/// Recording fake for <see cref="IAiDebugSession"/>.
/// Settable <see cref="IsPaused"/> and <see cref="IsAttached"/>; records
/// calls to Continue/StepOver/StepInto/StepOut/Pause.
/// </summary>
internal sealed class FakeAiDebugSession : IAiDebugSession
{
    // -- Settable state --
    public bool IsAttached { get; set; }
    public bool IsPaused   { get; set; }

    // -- Call recorders --
    public int ContinueCallCount  { get; private set; }
    public int StepOverCallCount  { get; private set; }
    public int StepIntoCallCount  { get; private set; }
    public int StepOutCallCount   { get; private set; }
    public int PauseCallCount     { get; private set; }

    public void Continue() => ContinueCallCount++;
    public void StepOver() => StepOverCallCount++;
    public void StepInto() => StepIntoCallCount++;
    public void StepOut()  => StepOutCallCount++;
    public void Pause()    => PauseCallCount++;

    // -- IAiTraceObserver stubs --
    public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
    public void EndObservingAsset(Guid assetId) { }
    public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    // -- Remaining IAiDebugSession stubs --
    // Fully-qualified because both Hrot.Editor.AiShared.Debug and
    // Hrot.Blueprints.Core.Debug define BreakpointId / Breakpoint.
    public void Detach() { IsAttached = false; }
    Hrot.Editor.AiShared.Debug.BreakpointId IAiDebugSession.SetBreakpoint(Guid assetId, Guid elementId) => default;
    void IAiDebugSession.ClearBreakpoint(Hrot.Editor.AiShared.Debug.BreakpointId id) { }
    public void ClearAllBreakpoints() { }
    IReadOnlyList<Hrot.Editor.AiShared.Debug.Breakpoint> IAiDebugSession.GetBreakpoints() =>
        Array.Empty<Hrot.Editor.AiShared.Debug.Breakpoint>();
    public bool IsAnyBreakpointActive => false;
    Hrot.Editor.AiShared.Debug.Breakpoint? IAiDebugSession.PausedAt => null;
    public Entity? PausedOnEntity => null;
    public event Action? OnSessionStateChanged;
}

/// <summary>
/// Recording fake implementing both <see cref="IAiDebugSession"/> and
/// <see cref="IBlueprintDebugSession"/>.
/// Because both interfaces define <c>BreakpointId</c>, <c>Breakpoint</c>,
/// <c>SetBreakpoint</c>, <c>ClearBreakpoint</c>, <c>GetBreakpoints</c>, and
/// <c>PausedAt</c> with DIFFERENT types from different assemblies, conflicting
/// members use explicit interface implementations with fully-qualified type names.
/// Settable <see cref="CurrentNodePointer"/> and <see cref="RecordedNodeCount"/>;
/// records <see cref="StepBackCallCount"/>.
/// </summary>
internal sealed class FakeBlueprintDebugSession : IAiDebugSession, IBlueprintDebugSession
{
    // -- Shared state (both interfaces) --
    public bool IsAttached { get; set; }
    public bool IsPaused   { get; set; }

    // -- Step control call recorders --
    public int ContinueCallCount  { get; private set; }
    public int StepOverCallCount  { get; private set; }
    public int StepIntoCallCount  { get; private set; }
    public int StepOutCallCount   { get; private set; }
    public int PauseCallCount     { get; private set; }
    public int StepBackCallCount  { get; private set; }

    // These five methods satisfy both IAiDebugSession and IBlueprintDebugSession
    // simultaneously (identical signatures and return types in both interfaces).
    public void Continue() => ContinueCallCount++;
    public void StepOver() => StepOverCallCount++;
    public void StepInto() => StepIntoCallCount++;
    public void StepOut()  => StepOutCallCount++;
    public void Pause()    => PauseCallCount++;

    // -- Blueprint-specific state --
    public int CurrentNodePointer { get; set; }
    public string? CurrentNodeId => CurrentNodePointer >= 0 ? $"node_{CurrentNodePointer}" : null;
    public int RecordedNodeCount  { get; set; }
    public void StepBack() => StepBackCallCount++;

    // -- Shared non-conflicting members (satisfy both interfaces) --
    public void Detach() { IsAttached = false; }
    public void ClearAllBreakpoints() { }
    public bool IsAnyBreakpointActive => false;
    public Entity? PausedOnEntity => null;
    public event Action? OnSessionStateChanged;

    // -- IAiTraceObserver stubs (also IBlueprintDebugSession.GetActiveEntities) --
    public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
    public void EndObservingAsset(Guid assetId) { }
    // Satisfies both IAiTraceObserver.GetActiveEntities and
    // IBlueprintDebugSession.GetActiveEntities (identical signatures).
    public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    // ── Conflicting IAiDebugSession members (AiShared types) ─────────────────
    Hrot.Editor.AiShared.Debug.BreakpointId IAiDebugSession.SetBreakpoint(Guid assetId, Guid elementId) => default;
    void IAiDebugSession.ClearBreakpoint(Hrot.Editor.AiShared.Debug.BreakpointId id) { }
    IReadOnlyList<Hrot.Editor.AiShared.Debug.Breakpoint> IAiDebugSession.GetBreakpoints() =>
        Array.Empty<Hrot.Editor.AiShared.Debug.Breakpoint>();
    Hrot.Editor.AiShared.Debug.Breakpoint? IAiDebugSession.PausedAt => null;

    // ── Conflicting IBlueprintDebugSession members (Blueprints.Core types) ────
    Hrot.Blueprints.Core.Debug.BreakpointId IBlueprintDebugSession.SetBreakpoint(
        Guid assetId, Guid graphId, Guid nodeId) => default;
    void IBlueprintDebugSession.ClearBreakpoint(Hrot.Blueprints.Core.Debug.BreakpointId id) { }
    IReadOnlyList<Hrot.Blueprints.Core.Debug.Breakpoint> IBlueprintDebugSession.GetBreakpoints() =>
        Array.Empty<Hrot.Blueprints.Core.Debug.Breakpoint>();
    Hrot.Blueprints.Core.Debug.Breakpoint? IBlueprintDebugSession.PausedAt => null;

    // -- IBlueprintProbeSink stubs --
    public void OnNodeEnter(Entity self, string nodeId) { }
    public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
    public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
    public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }

    // -- Remaining IBlueprintDebugSession stubs --
    public void Attach() { IsAttached = true; }
    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) => default;
    public void RemoveWatch(WatchId id) { }
    public void ClearAllWatches() { }
    public IReadOnlyList<Watch> GetWatches() => Array.Empty<Watch>();
    public bool IsAnyWatchActive => false;
    public void SetEntityFilter(Entity? entity) { }
    public Entity? GetEntityFilter() => null;
    public BlueprintStateSnapshot? GetCurrentStateSnapshot() => null;
    public BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId) => null;
    public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100) => Array.Empty<NodeExecuted>();
    public IReadOnlyList<CallFrame> GetCurrentCallStack() => Array.Empty<CallFrame>();
    public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100)
        => Array.Empty<NodeHistoryEntry>();
    public void RegisterDebugMap(DebugMap map) { }
    public void UnregisterDebugMap(Guid assetId) { }
    public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver) { }
    public bool IsNodeBreakpointable(Guid assetId, Guid graphId, Guid nodeId) => true;
    public void OnHotReloadBegin() { }
    public void OnHotReloadCompleted(Guid[] reloadedAssetIds) { }
    public void OnNewTick() { }
    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action<NodeExecuted>? OnNodeExecuted;
    public event Action<PinValueChanged>? OnPinValueChangedEvent;
    public event Action<Guid>? OnBreakpointListChanged;
}

/// <summary>
/// Fake <see cref="IDebugSessionRegistry"/> with a settable
/// <see cref="ActiveSession"/> for test control.
/// </summary>
internal sealed class FakeDebugSessionRegistry : IDebugSessionRegistry
{
    public IAiDebugSession? ActiveSession { get; set; }
    public event Action? Changed;

    /// <summary>Fires the <see cref="Changed"/> event.</summary>
    public void FireChanged() => Changed?.Invoke();

    public void SetActiveSession(IAiDebugSession? session) => ActiveSession = session;

    // -- Remaining stubs --
    public bool TryAcquireSession<TSession>(out TSession? session) where TSession : class, IAiDebugSession
    {
        session = default;
        return false;
    }
    public void ReleaseSession(IAiDebugSession session) { }
    public IDisposable RegisterObserver<TObserver>(TObserver observer) where TObserver : IAiTraceObserver
        => new DelegateDisposable(() => { });
    public IReadOnlyList<IAiTraceObserver> ActiveObservers => Array.Empty<IAiTraceObserver>();

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _onDispose;
        public DelegateDisposable(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

// ─── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for <see cref="AiDebugCommands"/> covering the polymorphic
/// AI Debug toolbar group registrar.
/// </summary>
public class AiDebugCommandsTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a recording registration delegate that captures descriptors and
    /// handlers so tests can query IsEnabled and invoke handlers.
    /// </summary>
    private static Action<EditorCommandDescriptor, Action<EditorCommandContext>> RecordingRegister(
        List<EditorCommandDescriptor> descriptors,
        List<Action<EditorCommandContext>> handlers)
    {
        return (d, h) =>
        {
            descriptors.Add(d);
            handlers.Add(h);
        };
    }

    /// <summary>Invokes the handler for a command with the given id.</summary>
    private static void InvokeHandler(
        List<EditorCommandDescriptor> descriptors,
        List<Action<EditorCommandContext>> handlers,
        string commandId)
    {
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (descriptors[i].Id == commandId)
            {
                handlers[i](default);
                return;
            }
        }
        throw new InvalidOperationException($"Command '{commandId}' not registered.");
    }

    /// <summary>Returns the descriptor for a command with the given id.</summary>
    private static EditorCommandDescriptor GetDescriptor(
        List<EditorCommandDescriptor> descriptors,
        string commandId)
    {
        for (int i = 0; i < descriptors.Count; i++)
            if (descriptors[i].Id == commandId)
                return descriptors[i];
        throw new InvalidOperationException($"Command '{commandId}' not registered.");
    }

    // ── Continue: IsEnabled ───────────────────────────────────────────────────

    [Fact]
    public void Continue_Enabled_WhenActiveSessionPaused_Else_Disabled()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession();

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);

        var desc = GetDescriptor(descriptors, AiDebugCommands.ContinueId);

        // No active session → disabled.
        registry.ActiveSession = null;
        Assert.False(desc.IsEnabled());

        // Active session, not paused → disabled.
        registry.ActiveSession = session;
        session.IsPaused = false;
        Assert.False(desc.IsEnabled());

        // Active session, paused → enabled.
        session.IsPaused = true;
        Assert.True(desc.IsEnabled());
    }

    // ── Continue: Invoke ──────────────────────────────────────────────────────

    [Fact]
    public void Continue_Invoke_CallsActiveSessionContinue()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession { IsPaused = true };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);
        registry.ActiveSession = session;

        InvokeHandler(descriptors, handlers, AiDebugCommands.ContinueId);

        Assert.Equal(1, session.ContinueCallCount);
    }

    // ── StepOver: Invoke ──────────────────────────────────────────────────────

    [Fact]
    public void StepOver_Invoke_CallsActiveSessionStepOver()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession { IsPaused = true };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);
        registry.ActiveSession = session;

        InvokeHandler(descriptors, handlers, AiDebugCommands.StepOverId);

        Assert.Equal(1, session.StepOverCallCount);
    }

    // ── StepInto: Invoke ──────────────────────────────────────────────────────

    [Fact]
    public void StepInto_Invoke_CallsActiveSessionStepInto()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession { IsPaused = true };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);
        registry.ActiveSession = session;

        InvokeHandler(descriptors, handlers, AiDebugCommands.StepIntoId);

        Assert.Equal(1, session.StepIntoCallCount);
    }

    // ── StepOut: Invoke ───────────────────────────────────────────────────────

    [Fact]
    public void StepOut_Invoke_CallsActiveSessionStepOut()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession { IsPaused = true };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);
        registry.ActiveSession = session;

        InvokeHandler(descriptors, handlers, AiDebugCommands.StepOutId);

        Assert.Equal(1, session.StepOutCallCount);
    }

    // ── Pause: Invoke ─────────────────────────────────────────────────────────

    [Fact]
    public void Pause_Invoke_CallsActiveSessionPause()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession { IsAttached = true, IsPaused = false };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);
        registry.ActiveSession = session;

        InvokeHandler(descriptors, handlers, AiDebugCommands.PauseId);

        Assert.Equal(1, session.PauseCallCount);
    }

    // ── Pause: IsEnabled ──────────────────────────────────────────────────────

    [Fact]
    public void Pause_Enabled_WhenAttachedAndRunning()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var session     = new FakeAiDebugSession();

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);

        var desc = GetDescriptor(descriptors, AiDebugCommands.PauseId);

        // No active session → disabled.
        registry.ActiveSession = null;
        Assert.False(desc.IsEnabled());

        // Detached → disabled.
        registry.ActiveSession = session;
        session.IsAttached = false;
        session.IsPaused   = false;
        Assert.False(desc.IsEnabled());

        // Attached but paused → disabled (Pause is for running sessions).
        session.IsAttached = true;
        session.IsPaused   = true;
        Assert.False(desc.IsEnabled());

        // Attached and running → enabled.
        session.IsAttached = true;
        session.IsPaused   = false;
        Assert.True(desc.IsEnabled());
    }

    // ── StepBack: present only when ActiveSession is IBlueprintDebugSession ───

    [Fact]
    public void StepBack_PresentOnly_WhenActiveSessionIsBlueprint()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var nonBpSession = new FakeAiDebugSession { IsPaused = true };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);

        // With a non-blueprint session, StepBack is registered but its
        // IsEnabled returns false (no IBlueprintDebugSession).
        registry.ActiveSession = nonBpSession;
        var desc = GetDescriptor(descriptors, AiDebugCommands.StepBackId);
        Assert.False(desc.IsEnabled());

        // BuildGroupModel: StepBack must NOT be present for non-blueprint.
        var model = AiDebugCommands.BuildGroupModel(registry);
        var stepBackItem = model.FirstOrDefault(m => m.Id == AiDebugCommands.StepBackId);
        Assert.Null(stepBackItem);

        // With a blueprint session (CurrentNodePointer = 0), StepBack is
        // present but disabled.
        var bpSession = new FakeBlueprintDebugSession
        {
            IsPaused = true,
            CurrentNodePointer = 0,
            RecordedNodeCount = 5
        };
        registry.ActiveSession = bpSession;

        model = AiDebugCommands.BuildGroupModel(registry);
        stepBackItem = model.FirstOrDefault(m => m.Id == AiDebugCommands.StepBackId);
        Assert.NotNull(stepBackItem);
        Assert.True(stepBackItem!.IsPresent);
        Assert.False(stepBackItem.IsEnabled);

        // With CurrentNodePointer > 0, StepBack is enabled.
        bpSession.CurrentNodePointer = 3;
        model = AiDebugCommands.BuildGroupModel(registry);
        stepBackItem = model.FirstOrDefault(m => m.Id == AiDebugCommands.StepBackId);
        Assert.NotNull(stepBackItem);
        Assert.True(stepBackItem!.IsEnabled);
    }

    // ── StepBack: invoke ──────────────────────────────────────────────────────

    [Fact]
    public void StepBack_Invoke_CallsActiveBlueprintSessionStepBack()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();
        var bpSession   = new FakeBlueprintDebugSession
        {
            IsPaused = true,
            CurrentNodePointer = 3,
            RecordedNodeCount = 5
        };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);
        registry.ActiveSession = bpSession;

        InvokeHandler(descriptors, handlers, AiDebugCommands.StepBackId);

        Assert.Equal(1, bpSession.StepBackCallCount);
    }

    // ── Group model: non-blueprint session ────────────────────────────────────

    [Fact]
    public void Group_Works_ForNonBlueprintSession()
    {
        var registry = new FakeDebugSessionRegistry();
        var session  = new FakeAiDebugSession { IsAttached = true, IsPaused = true };

        registry.ActiveSession = session;
        var model = AiDebugCommands.BuildGroupModel(registry);

        // All 5 common commands are present.
        Assert.Contains(model, m => m.Id == AiDebugCommands.ContinueId && m.IsPresent && m.IsEnabled);
        Assert.Contains(model, m => m.Id == AiDebugCommands.StepOverId && m.IsPresent && m.IsEnabled);
        Assert.Contains(model, m => m.Id == AiDebugCommands.StepIntoId && m.IsPresent && m.IsEnabled);
        Assert.Contains(model, m => m.Id == AiDebugCommands.StepOutId  && m.IsPresent && m.IsEnabled);
        Assert.Contains(model, m => m.Id == AiDebugCommands.PauseId    && m.IsPresent && !m.IsEnabled); // paused → Pause disabled

        // StepBack NOT present for non-blueprint.
        Assert.DoesNotContain(model, m => m.Id == AiDebugCommands.StepBackId);

        // Also verify invocation works through the command set.
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);

        // Invoke Continue and verify it calls the session.
        InvokeHandler(descriptors, handlers, AiDebugCommands.ContinueId);
        Assert.Equal(1, session.ContinueCallCount);
        Assert.Equal(0, session.StepOverCallCount);
        Assert.Equal(0, session.StepIntoCallCount);
        Assert.Equal(0, session.StepOutCallCount);
        Assert.Equal(0, session.PauseCallCount);
    }

    // ── NodePosition: empty for non-blueprint ─────────────────────────────────

    [Fact]
    public void NodePosition_EmptyForNonBlueprintSession()
    {
        var registry = new FakeDebugSessionRegistry();

        // Null session → empty.
        registry.ActiveSession = null;
        Assert.Equal(string.Empty, AiDebugCommands.NodePositionText(registry));

        // Non-blueprint session → empty.
        registry.ActiveSession = new FakeAiDebugSession { IsPaused = true };
        Assert.Equal(string.Empty, AiDebugCommands.NodePositionText(registry));

        // Blueprint session, paused, with recordings → non-empty.
        var bpSession = new FakeBlueprintDebugSession
        {
            IsPaused = true,
            CurrentNodePointer = 2,
            RecordedNodeCount = 10
        };
        registry.ActiveSession = bpSession;

        var text = AiDebugCommands.NodePositionText(registry);
        Assert.NotEqual(string.Empty, text);
        Assert.Equal("node 3 / 10", text); // 1-based: pointer 2 → "node 3 / 10"
    }

    // ── Null guards ───────────────────────────────────────────────────────────

    [Fact]
    public void Register_NullArguments_ThrowArgumentNullException()
    {
        var registry = new FakeDebugSessionRegistry();
        var validRegister = (EditorCommandDescriptor d, Action<EditorCommandContext> h) => { };

        Assert.Throws<ArgumentNullException>(() => AiDebugCommands.Register(null!, registry));
        Assert.Throws<ArgumentNullException>(() => AiDebugCommands.Register(validRegister, null!));
    }

    [Fact]
    public void BuildGroupModel_NullRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AiDebugCommands.BuildGroupModel(null!));
    }

    [Fact]
    public void NodePositionText_NullRegistry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AiDebugCommands.NodePositionText(null!));
    }

    // ── All common commands respect active session null safety ────────────────

    [Fact]
    public void CommonCommands_Invoke_NoOp_WhenActiveSessionNull()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry { ActiveSession = null };

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);

        // Invoking any common command with no active session must not throw.
        InvokeHandler(descriptors, handlers, AiDebugCommands.ContinueId);
        InvokeHandler(descriptors, handlers, AiDebugCommands.StepOverId);
        InvokeHandler(descriptors, handlers, AiDebugCommands.StepIntoId);
        InvokeHandler(descriptors, handlers, AiDebugCommands.StepOutId);
        InvokeHandler(descriptors, handlers, AiDebugCommands.PauseId);

        // No exception = pass.
    }

    // ── All 6 commands are registered ─────────────────────────────────────────

    [Fact]
    public void Register_Registers_AllSixCommands()
    {
        var descriptors = new List<EditorCommandDescriptor>();
        var handlers    = new List<Action<EditorCommandContext>>();
        var registry    = new FakeDebugSessionRegistry();

        AiDebugCommands.Register(RecordingRegister(descriptors, handlers), registry);

        Assert.Equal(6, descriptors.Count);

        var ids = descriptors.Select(d => d.Id).ToHashSet();
        Assert.Contains(AiDebugCommands.ContinueId, ids);
        Assert.Contains(AiDebugCommands.StepOverId, ids);
        Assert.Contains(AiDebugCommands.StepIntoId, ids);
        Assert.Contains(AiDebugCommands.StepOutId,  ids);
        Assert.Contains(AiDebugCommands.PauseId,    ids);
        Assert.Contains(AiDebugCommands.StepBackId, ids);
    }
}
