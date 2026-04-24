using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Utils;
using StructEdit.Core;
using Xunit;

using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests;

/// <summary>
/// Tests for CE09: <see cref="ComponentReflector"/> double-click integration and
/// edit-window registration logic.
///
/// All tests call <c>TryOpenEditWindow</c> directly (the internal extracted method) to verify
/// window registration behaviour without requiring ImGui mouse-click simulation.
/// </summary>
[Collection("ImGui Sequential")]
public class ComponentReflectorDoubleClickTests
{
    // ── Fake edit session ─────────────────────────────────────────────────────

    private sealed class FakeEditSession : IEditSession
    {
        public EditDocument Document => throw new NotImplementedException("Not used in construction tests.");
        public bool IsDirty => false;
        public EditRebuildState RebuildState => EditRebuildState.Stable;
        public void MarkStructuralChange() { }
        public void RebuildDocument() { }
        public ValidationResult Validate() => ValidationResult.Ok();
        public object Commit() => new object();
        public void Cancel() { }
        public void Dispose() { }
    }

    // ── Fake edit service ─────────────────────────────────────────────────────

    private sealed class FakeEditService : IComponentEditService
    {
        public int OpenCallCount { get; private set; }
        public EditScope? LastScope { get; private set; }

        public IEditSession Open(object component, Type componentType,
            EditScope? scope = null, EditContext? context = null)
        {
            OpenCallCount++;
            LastScope = scope;
            return new FakeEditSession();
        }
    }

    // ── Minimal writable session stub ─────────────────────────────────────────

    private sealed class WritableSession : IInspectableSession
    {
        private readonly Type    _type;
        private readonly object? _data;
        private readonly bool    _readOnly;

        public WritableSession(Type type, object? data, bool readOnly = false)
        {
            _type     = type;
            _data     = data;
            _readOnly = readOnly;
        }

        public bool IsReadOnly  => _readOnly;
        public int  EntityCount => 1;

        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public IEnumerable<Type>   GetAllComponentTypes() => new[] { _type };
        public bool   HasComponent(Entity e, Type t)  => t == _type;
        public object? GetComponent(Entity e, Type t) => t == _type ? _data : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public bool HasAuthority(Entity e, Type t) => false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    [ComponentId(251)]
    private struct DummyComponent { public int X; }

    private static Entity MakeEntity(int index = 3, int generation = 2)
    {
        // Build an entity with specific Index/Generation for deterministic-ID tests.
        using var repo = new EntityRepository();
        // Create enough entities to reach the desired index.
        for (int i = 0; i < index; i++)
            repo.CreateEntity();
        return repo.CreateEntity(); // index+1 might not equal 'index' exactly, so use returned value
    }

    private static WM MakeWindowManager()
    {
        // IconAtlas requires no GPU context for basic operations (no Render/Draw calls in tests).
        var atlas = new IconAtlas(IntPtr.Zero, 128f, 128f, 16f);
        return new WM(atlas);
    }

    // ── T-CE09a: no-op when read-only ─────────────────────────────────────────

    /// <summary>
    /// T-CE09a: When <see cref="IInspectableSession.IsReadOnly"/> is <c>true</c>, no window
    /// is registered in the <see cref="WindowManager"/> even if all other properties are set.
    /// </summary>
    [Fact]
    public void TryOpenEditWindow_ReadOnlySession_NoWindowRegistered()
    {
        var fakeService = new FakeEditService();
        var reflector   = new ComponentReflector(fakeService);
        var wm          = MakeWindowManager();
        var session     = new WritableSession(typeof(DummyComponent), new DummyComponent { X = 1 }, readOnly: true);
        var data        = new DummyComponent { X = 1 };
        var entity      = MakeEntity();

        reflector.EditWindowManager  = wm;
        reflector.EditSessionGetter  = () => session;

        reflector.TryOpenEditWindow(session, entity, typeof(DummyComponent), data,
            headerDoubleClicked: true, doubleClickedPath: null);

        // No window should have been registered because session is read-only.
        string expectedId = $"cedit_{entity.Index}_{entity.Generation}_{typeof(DummyComponent).FullName}";
        Assert.False(wm.TryGetWindow(expectedId, out _),
            "RegisterWindow must not be called for read-only sessions.");
        Assert.Equal(0, fakeService.OpenCallCount);
    }

    // ── T-CE09b: no-op when manager null ──────────────────────────────────────

    /// <summary>
    /// T-CE09b: When <see cref="ComponentReflector.EditWindowManager"/> is <c>null</c>,
    /// <see cref="ComponentReflector.TryOpenEditWindow"/> returns silently without throwing.
    /// </summary>
    [Fact]
    public void TryOpenEditWindow_NullWindowManager_NoException()
    {
        var fakeService = new FakeEditService();
        var reflector   = new ComponentReflector(fakeService);
        var session     = new WritableSession(typeof(DummyComponent), new DummyComponent { X = 1 });
        var data        = new DummyComponent { X = 1 };
        var entity      = MakeEntity();

        // EditWindowManager intentionally left null.
        reflector.EditSessionGetter = () => session;

        // Must not throw.
        var ex = Record.Exception(() =>
            reflector.TryOpenEditWindow(session, entity, typeof(DummyComponent), data,
                headerDoubleClicked: true, doubleClickedPath: null));

        Assert.Null(ex);
    }

    // ── T-CE09c: window registered on header double-click ─────────────────────

    /// <summary>
    /// T-CE09c: A writable session with all injection properties set and
    /// <c>headerDoubleClicked=true</c> causes a window to be registered.
    /// </summary>
    [Fact]
    public void TryOpenEditWindow_HeaderDoubleClick_RegistersWindow()
    {
        var fakeService = new FakeEditService();
        var reflector   = new ComponentReflector(fakeService);
        var wm          = MakeWindowManager();
        var session     = new WritableSession(typeof(DummyComponent), new DummyComponent { X = 1 });
        var data        = new DummyComponent { X = 1 };
        var entity      = MakeEntity();

        reflector.EditWindowManager = wm;
        reflector.EditSessionGetter = () => session;

        reflector.TryOpenEditWindow(session, entity, typeof(DummyComponent), data,
            headerDoubleClicked: true, doubleClickedPath: null);

        string expectedId = $"cedit_{entity.Index}_{entity.Generation}_{typeof(DummyComponent).FullName}";
        Assert.True(wm.TryGetWindow(expectedId, out _),
            "RegisterWindow must be called when headerDoubleClicked is true.");
        Assert.Equal(1, fakeService.OpenCallCount);
    }

    // ── T-CE09d: focus on duplicate ───────────────────────────────────────────

    /// <summary>
    /// T-CE09d: When the same component is triggered a second time (window already registered),
    /// the window is focused rather than a second one being registered.
    /// </summary>
    [Fact]
    public void TryOpenEditWindow_SecondDoubleClick_FocusesNotDuplicates()
    {
        var fakeService = new FakeEditService();
        var reflector   = new ComponentReflector(fakeService);
        var wm          = MakeWindowManager();
        var session     = new WritableSession(typeof(DummyComponent), new DummyComponent { X = 1 });
        var data        = new DummyComponent { X = 1 };
        var entity      = MakeEntity();

        reflector.EditWindowManager = wm;
        reflector.EditSessionGetter = () => session;

        // First double-click: registers the window.
        reflector.TryOpenEditWindow(session, entity, typeof(DummyComponent), data,
            headerDoubleClicked: true, doubleClickedPath: null);

        int openAfterFirst = fakeService.OpenCallCount; // should be 1

        // Second double-click: window already exists → FocusWindow path.
        reflector.TryOpenEditWindow(session, entity, typeof(DummyComponent), data,
            headerDoubleClicked: true, doubleClickedPath: null);

        // _editService.Open must NOT be called again (FocusWindow, not RegisterWindow).
        Assert.Equal(1, openAfterFirst);
        Assert.Equal(1, fakeService.OpenCallCount);
    }

    // ── T-CE09e: deterministic ID format uses FullName ────────────────────────

    /// <summary>
    /// T-CE09e: The window ID format is <c>"cedit_{Index}_{Generation}_{FullName}"</c>.
    /// Verified by computing the string directly without going through ImGui.
    /// </summary>
    [Fact]
    public void WindowId_DeterministicFormat_UsesFullName()
    {
        // Use a type with a known FullName that includes a namespace.
        Type type = typeof(DummyComponent);

        // Build entity with specific Index and Generation by creating entities in a repo.
        // We control the entity values directly by reading what repo returns.
        using var repo = new EntityRepository();
        Entity e = default;
        for (int i = 0; i <= 3; i++)
            e = repo.CreateEntity(); // will have Index around 3; exact values depend on repo

        string expectedId = $"cedit_{e.Index}_{e.Generation}_{type.FullName}";

        // Now verify our reflector produces this ID by triggering registration and checking the key.
        var fakeService = new FakeEditService();
        var reflector   = new ComponentReflector(fakeService);
        var wm          = MakeWindowManager();
        var session     = new WritableSession(type, new DummyComponent { X = 1 });

        reflector.EditWindowManager = wm;
        reflector.EditSessionGetter = () => session;

        reflector.TryOpenEditWindow(session, e, type, new DummyComponent { X = 1 },
            headerDoubleClicked: true, doubleClickedPath: null);

        Assert.True(wm.TryGetWindow(expectedId, out _),
            $"Expected window ID '{expectedId}' to be registered.");
    }

    // ── T-CE09f: scoped open on field-row double-click ────────────────────────

    /// <summary>
    /// T-CE09f: When <c>doubleClickedPath == "$.Position.X"</c>, the session is opened with
    /// <see cref="EditScope.ForField"/> scope (not whole-component scope).
    /// </summary>
    [Fact]
    public void TryOpenEditWindow_FieldDoubleClick_OpensWithFieldScope()
    {
        const string fieldPath = "$.Position.X";

        var fakeService = new FakeEditService();
        var reflector   = new ComponentReflector(fakeService);
        var wm          = MakeWindowManager();
        var session     = new WritableSession(typeof(DummyComponent), new DummyComponent { X = 1 });
        var data        = new DummyComponent { X = 1 };
        var entity      = MakeEntity();

        reflector.EditWindowManager = wm;
        reflector.EditSessionGetter = () => session;

        reflector.TryOpenEditWindow(session, entity, typeof(DummyComponent), data,
            headerDoubleClicked: false, doubleClickedPath: fieldPath);

        Assert.Equal(1, fakeService.OpenCallCount);

        // Scope must be ForField, not WholeComponent.
        var scope = fakeService.LastScope;
        Assert.NotNull(scope);
        Assert.True(scope!.IncludedPaths.Count == 1,
            "ForField scope must have exactly one path.");
        Assert.Equal(fieldPath, scope.IncludedPaths[0].Value);
    }
}
