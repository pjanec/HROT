using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.ImGui.Abstractions;
using Fdp.Toolkit.ImGui.Panels;
using Fdp.Toolkit.ImGui.Utils;
using Xunit;

namespace Fdp.Toolkit.ImGui.Tests;

// ── Minimal descriptor stubs ──────────────────────────────────────────────────

file struct Position { public float X; public float Y; public float Z; }
file struct Health   { public float Hp; }

// ── Tests ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for <see cref="DerEntityInspectorPanel"/>.
///
/// <para>All tests exercise the non-drawing, testable helpers
/// (<see cref="DerEntityInspectorPanel.GetEntityListRows"/>,
/// <see cref="DerEntityInspectorPanel.NoSelection"/> and the public
/// context-menu registration API) without requiring an active ImGui context.
/// The <see cref="Draw_SmokeTest_RunsWithoutException"/> test uses
/// <see cref="ImGuiTestFixture"/> and runs inside the "ImGui Sequential"
/// collection to prevent concurrent native-window access.</para>
/// </summary>
[Collection("ImGui Sequential")]
public class DerEntityInspectorPanelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DerRepo CreateRepo(params int[] entityIds)
    {
        var repo = new DerRepo();
        foreach (var id in entityIds)
            repo.CreateEntity(id, 1L);
        return repo;
    }

    // ── NoSelection constant ──────────────────────────────────────────────────

    [Fact]
    public void NoSelection_IsZero()
    {
        Assert.Equal(0, DerEntityInspectorPanel.NoSelection);
    }

    // ── GetEntityListRows ─────────────────────────────────────────────────────

    [Fact]
    public void GetEntityListRows_EmptyRepo_ReturnsEmpty()
    {
        var repo = new DerRepo();

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo);

        Assert.Empty(rows);
    }

    [Fact]
    public void GetEntityListRows_NoFilter_ReturnsAllIds()
    {
        var repo = CreateRepo(10, 20, 30);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo);

        Assert.Equal(3, rows.Count);
        Assert.Contains(10, rows);
        Assert.Contains(20, rows);
        Assert.Contains(30, rows);
    }

    [Fact]
    public void GetEntityListRows_NumericFilter_MatchesExactId()
    {
        var repo = CreateRepo(1, 2, 3);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "2");

        Assert.Single(rows);
        Assert.Equal(2, rows[0]);
    }

    [Fact]
    public void GetEntityListRows_NumericFilter_NoExactMatch_ReturnsEmpty()
    {
        var repo = CreateRepo(1, 2, 3);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "99");

        Assert.Empty(rows);
    }

    [Fact]
    public void GetEntityListRows_NonNumericFilter_ReturnsEmpty()
    {
        var repo = CreateRepo(1, 2, 3);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "xyz");

        Assert.Empty(rows);
    }

    [Fact]
    public void GetEntityListRows_WhitespaceFilter_ReturnsAll()
    {
        var repo = CreateRepo(7, 8);

        var rows = DerEntityInspectorPanel.GetEntityListRows(repo, "   ");

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void GetEntityListRows_NullRepo_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DerEntityInspectorPanel.GetEntityListRows(null!));
    }

    // ── Constructor / registration ────────────────────────────────────────────

    [Fact]
    public void Construct_DoesNotThrow()
    {
        var ex = Record.Exception(() => new DerEntityInspectorPanel());

        Assert.Null(ex);
    }

    [Fact]
    public void RegisterContextMenuHandler_Null_ThrowsArgumentNullException()
    {
        var panel = new DerEntityInspectorPanel();

        Assert.Throws<ArgumentNullException>(() =>
            panel.RegisterContextMenuHandler(null!));
    }

    [Fact]
    public void RegisterContextMenuHandler_ValidHandler_DoesNotThrow()
    {
        var panel   = new DerEntityInspectorPanel();
        var handler = new LambdaDerContextMenuHandler((_, _) => { });

        var ex = Record.Exception(() => panel.RegisterContextMenuHandler(handler));

        Assert.Null(ex);
    }

    // ── Context menu handler invocation ──────────────────────────────────────

    [Fact]
    public void InvokeContextMenuHandlers_SingleHandler_InvokesPopulate()
    {
        var panel  = new DerEntityInspectorPanel();
        var repo   = CreateRepo(5);
        var entity = repo.GetEntity(5)!;

        IDerEntity? capturedEntity = null;
        panel.RegisterContextMenuHandler(new LambdaDerContextMenuHandler((e, _) =>
        {
            capturedEntity = e;
        }));

        var stubBuilder = new StubContextMenuBuilder();
        panel.InvokeContextMenuHandlers(entity, stubBuilder);

        Assert.Same(entity, capturedEntity);
    }

    [Fact]
    public void InvokeContextMenuHandlers_MultipleHandlers_AllInvoked()
    {
        var panel  = new DerEntityInspectorPanel();
        var repo   = CreateRepo(1);
        var entity = repo.GetEntity(1)!;

        var invocations = new List<int>();
        panel.RegisterContextMenuHandler(new LambdaDerContextMenuHandler((_, _) => invocations.Add(1)));
        panel.RegisterContextMenuHandler(new LambdaDerContextMenuHandler((_, _) => invocations.Add(2)));
        panel.RegisterContextMenuHandler(new LambdaDerContextMenuHandler((_, _) => invocations.Add(3)));

        panel.InvokeContextMenuHandlers(entity, new StubContextMenuBuilder());

        Assert.Equal(new[] { 1, 2, 3 }, invocations);
    }

    [Fact]
    public void InvokeContextMenuHandlers_NoHandlers_DoesNotThrow()
    {
        var panel  = new DerEntityInspectorPanel();
        var repo   = CreateRepo(1);
        var entity = repo.GetEntity(1)!;

        var ex = Record.Exception(() =>
            panel.InvokeContextMenuHandlers(entity, new StubContextMenuBuilder()));

        Assert.Null(ex);
    }

    // ── LambdaDerContextMenuHandler ───────────────────────────────────────────

    [Fact]
    public void LambdaDerContextMenuHandler_NullDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new LambdaDerContextMenuHandler(null!));
    }

    [Fact]
    public void LambdaDerContextMenuHandler_PopulateMenu_InvokesDelegate()
    {
        bool called = false;
        var handler = new LambdaDerContextMenuHandler((_, _) => called = true);
        var repo    = CreateRepo(1);
        var entity  = repo.GetEntity(1)!;

        handler.PopulateMenu(entity, new StubContextMenuBuilder());

        Assert.True(called);
    }

    // ── Draw smoke test ───────────────────────────────────────────────────────

    [Fact]
    public void Draw_SmokeTest_RunsWithoutException()
    {
        using var fixture = new ImGuiTestFixture();
        var repo          = CreateRepo(1, 2, 3);
        var panel         = new DerEntityInspectorPanel();

        fixture.NewFrame();
        panel.Draw(repo, "Test Inspector");
        fixture.Render();
    }

    // ── Stub / fakes ──────────────────────────────────────────────────────────

    private sealed class StubContextMenuBuilder : IContextMenuBuilder
    {
        public void AddItem(string label, Action callback, bool enabled = true) { }
        public IContextMenuBuilder BeginSubmenu(string label) => this;
        public void EndSubmenu() { }
        public void AddSeparator() { }
    }
}
