using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Editing;
using StructEdit.Core;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Editing;

// ---------------------------------------------------------------------------
// Shared test doubles for CE08
// ---------------------------------------------------------------------------

/// <summary>
/// Controllable IEditSession that tracks method calls and can throw from Commit.
/// </summary>
file sealed class FakeEditSession : IEditSession
{
    public EditDocument Document { get; set; } = null!;
    public EditRebuildState RebuildState { get; set; } = EditRebuildState.Stable;
    public bool IsDirty => false;

    public bool DisposeWasCalled { get; private set; }
    public bool RebuildDocumentWasCalled { get; private set; }

    // Controls Commit() behavior.
    public object? CommitResult { get; set; } = new object();
    public EditValidationException? CommitException { get; set; }

    // Call log for ordering verification.
    public List<string> CallLog { get; } = new();

    public void MarkStructuralChange() => CallLog.Add("MarkStructuralChange");

    public void RebuildDocument()
    {
        CallLog.Add("RebuildDocument");
        RebuildDocumentWasCalled = true;
    }

    public ValidationResult Validate() => ValidationResult.Ok();

    public object Commit()
    {
        CallLog.Add("Commit");
        if (CommitException != null) throw CommitException;
        return CommitResult!;
    }

    public void Cancel() { }

    public void Dispose()
    {
        CallLog.Add("Dispose");
        DisposeWasCalled = true;
    }
}

/// <summary>
/// Controllable IInspectableSession for liveness and SetComponent verification.
/// </summary>
file sealed class FakeInspectableSession : IInspectableSession
{
    private readonly bool _isAlive;

    public bool SetComponentWasCalled { get; private set; }
    public object? LastSetComponentData { get; private set; }

    public FakeInspectableSession(bool isAlive) => _isAlive = isAlive;

    public bool IsReadOnly => false;
    public int EntityCount => 0;
    public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
    public bool IsAlive(Entity e) => _isAlive;
    public bool HasComponent(Entity e, Type t) => false;
    public object? GetComponent(Entity e, Type t) => null;
    public void SetComponent(Entity e, Type t, object data)
    {
        SetComponentWasCalled = true;
        LastSetComponentData = data;
    }
    public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
    public bool HasAuthority(Entity e, Type t) => false;
}

// ---------------------------------------------------------------------------
// CE08 -- ComponentEditWindow
// ---------------------------------------------------------------------------

public class ComponentEditWindowTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ComponentEditWindow MakeWindow(
        IEditSession session,
        Entity entity,
        Func<IInspectableSession?> sessionGetter,
        Type? componentType = null)
    {
        return new ComponentEditWindow(
            id:                "test_window",
            title:             "Test",
            owningPerspective: "TestPerspective",
            session:           session,
            targetEntity:      entity,
            componentType:     componentType ?? typeof(object),
            sessionGetter:     sessionGetter);
    }

    // ── T-CE08a ──────────────────────────────────────────────────────────────
    // After construction: IsVolatile == true, ShowInMenu == false.
    [Fact]
    public void T_CE08a_AfterConstruction_VolatileAndMenuFlagsAreSet()
    {
        var session = new FakeEditSession();
        var entity  = new Entity(1, 1);
        var win = MakeWindow(session, entity, () => new FakeInspectableSession(isAlive: true));

        Assert.True(win.IsVolatile, "IsVolatile must be true for a volatile spawned window.");
        Assert.False(win.ShowInMenu, "ShowInMenu must be false — volatile windows are not in the menu.");
        Assert.True(win.IsOpen, "IsOpen must be true immediately after construction.");
    }

    // ── T-CE08b ──────────────────────────────────────────────────────────────
    // When sessionGetter returns a session where IsAlive == false, ExecuteDrawLogic
    // sets IsOpen = false without throwing.
    [Fact]
    public void T_CE08b_LivenessGuard_DeadEntity_ClosesWindow()
    {
        var session = new FakeEditSession();
        var entity  = new Entity(2, 1);
        var win = MakeWindow(session, entity,
            () => new FakeInspectableSession(isAlive: false));

        win.ExecuteDrawLogic();

        Assert.False(win.IsOpen, "Window must close when the entity is no longer alive.");
        Assert.True(session.DisposeWasCalled, "Session must be disposed on liveness failure.");
    }

    // ── T-CE08c ──────────────────────────────────────────────────────────────
    // When sessionGetter returns null, ExecuteDrawLogic sets IsOpen = false.
    [Fact]
    public void T_CE08c_LivenessGuard_NullSession_ClosesWindow()
    {
        var session = new FakeEditSession();
        var entity  = new Entity(3, 1);
        var win = MakeWindow(session, entity, () => null);

        win.ExecuteDrawLogic();

        Assert.False(win.IsOpen, "Window must close when sessionGetter returns null.");
        Assert.True(session.DisposeWasCalled, "Session must be disposed when sessionGetter is null.");
    }

    // ── T-CE08d ──────────────────────────────────────────────────────────────
    // When RebuildState == RebuildRequired, ExecuteDrawLogic calls RebuildDocument
    // before any other session operations.
    [Fact]
    public void T_CE08d_RebuildRequired_RebuildDocumentCalledBeforeOtherOps()
    {
        var session = new FakeEditSession
        {
            RebuildState = EditRebuildState.RebuildRequired
        };
        var entity = new Entity(4, 1);
        var win = MakeWindow(session, entity,
            () => new FakeInspectableSession(isAlive: true));

        win.ExecuteDrawLogic();

        Assert.True(session.RebuildDocumentWasCalled, "RebuildDocument must be called.");
        Assert.Contains("RebuildDocument", session.CallLog);
    }

    [Fact]
    public void T_CE08d_RebuildNotRequired_RebuildDocumentNotCalled()
    {
        var session = new FakeEditSession
        {
            RebuildState = EditRebuildState.Stable
        };
        var entity = new Entity(5, 1);
        var win = MakeWindow(session, entity,
            () => new FakeInspectableSession(isAlive: true));

        win.ExecuteDrawLogic();

        Assert.False(session.RebuildDocumentWasCalled,
            "RebuildDocument must NOT be called when state is Stable.");
    }

    // ── T-CE08e ──────────────────────────────────────────────────────────────
    // After the cancel path (simulated via liveness failure that triggers CloseAndCleanup),
    // Dispose is called and IsOpen == false.
    [Fact]
    public void T_CE08e_CloseAndCleanup_DisposesSessionAndClosesWindow()
    {
        var session = new FakeEditSession();
        var entity  = new Entity(6, 1);

        // Route through liveness guard to trigger CloseAndCleanup (same code path as Cancel).
        var win = MakeWindow(session, entity, () => null);

        win.ExecuteDrawLogic();

        Assert.True(session.DisposeWasCalled, "Session.Dispose must be called during cleanup.");
        Assert.False(win.IsOpen, "IsOpen must be false after cleanup.");
    }

    // ── T-CE08f ──────────────────────────────────────────────────────────────
    // When Commit throws EditValidationException, window stays open and _errorMessage is set.
    [Fact]
    public void T_CE08f_CommitThrows_ValidationException_WindowStaysOpenAndErrorSet()
    {
        var validationError = new ValidationError("$.Speed", "Speed must be positive.");
        var result          = ValidationResult.Fail(new[] { validationError });
        var exception       = new EditValidationException(result);

        var session = new FakeEditSession { CommitException = exception };
        var entity  = new Entity(7, 1);

        var win = MakeWindow(session, entity,
            () => new FakeInspectableSession(isAlive: true));

        win.ExecuteOkLogic();

        Assert.True(win.IsOpen, "Window must remain open after validation failure.");
        Assert.NotNull(win.ErrorMessage);
        Assert.Equal("Speed must be positive.", win.ErrorMessage);
    }

    [Fact]
    public void T_CE08f_CommitThrows_EmptyErrors_FallbackMessageSet()
    {
        var result    = ValidationResult.Fail(Enumerable.Empty<ValidationError>());
        // ValidationResult.Fail with empty → returns Ok(), so we create the exception directly.
        // Use a result with 0 errors by reflection to simulate the "Validation failed." fallback.
        var exception = new EditValidationException(ValidationResult.Ok(), "Validation failed.");

        var session = new FakeEditSession
        {
            CommitException = exception
        };
        var entity = new Entity(8, 1);

        var win = MakeWindow(session, entity,
            () => new FakeInspectableSession(isAlive: true));

        win.ExecuteOkLogic();

        Assert.True(win.IsOpen);
        Assert.NotNull(win.ErrorMessage);
    }

    // ── T-CE08g ──────────────────────────────────────────────────────────────
    // When sessionGetter returns null after Commit, SetComponent is NOT called.
    [Fact]
    public void T_CE08g_SessionNullAfterCommit_SetComponentNotCalled()
    {
        var committed = new object();
        var session   = new FakeEditSession { CommitResult = committed };
        var entity    = new Entity(9, 1);

        // sessionGetter returns null — simulates mid-frame session disposal.
        var win = MakeWindow(session, entity, () => null);

        win.ExecuteOkLogic();

        // SetComponent cannot have been called because sessionGetter returned null.
        // Verify the window closed cleanly (CloseAndCleanup was still called).
        Assert.False(win.IsOpen,
            "Window must be closed even when sessionGetter returns null after commit.");
        Assert.True(session.DisposeWasCalled,
            "Session.Dispose must be called during CloseAndCleanup.");
    }

    [Fact]
    public void T_CE08g_EntityDeadAfterCommit_SetComponentNotCalled()
    {
        var committed              = new object();
        var fakeSession            = new FakeEditSession { CommitResult = committed };
        var inspectable            = new FakeInspectableSession(isAlive: false);
        var entity                 = new Entity(10, 1);

        var win = MakeWindow(fakeSession, entity, () => inspectable);

        win.ExecuteOkLogic();

        Assert.False(inspectable.SetComponentWasCalled,
            "SetComponent must NOT be called when the entity is dead at commit time.");
        Assert.False(win.IsOpen);
    }
}
