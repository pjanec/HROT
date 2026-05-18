using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Utils;
using Xunit;

namespace Fdp.Presentation.Tests;

/// <summary>
/// Tests for CE10: <see cref="EntityInspectorPanel.Reflector"/> and
/// <see cref="EntityWatchPanel.Reflector"/> exposure.
///
/// Verifies that the <c>Reflector</c> property is non-null, public, and that
/// setting its injectable properties works correctly from outside the assembly.
/// </summary>
public class ReflectorExposureTests
{
    // ── T-CE10a: EntityInspectorPanel.Reflector non-null ─────────────────────

    /// <summary>
    /// T-CE10a: <c>new EntityInspectorPanel().Reflector</c> returns a non-null
    /// <see cref="ComponentReflector"/>.
    /// </summary>
    [Fact]
    public void EntityInspectorPanel_Reflector_IsNonNull()
    {
        var panel = new EntityInspectorPanel();

        Assert.NotNull(panel.Reflector);
    }

    // ── T-CE10b: EntityWatchPanel.Reflector non-null ──────────────────────────

    /// <summary>
    /// T-CE10b: <c>new EntityWatchPanel(someEntity).Reflector</c> returns a non-null
    /// <see cref="ComponentReflector"/>.
    /// </summary>
    [Fact]
    public void EntityWatchPanel_Reflector_IsNonNull()
    {
        using var repo   = new EntityRepository();
        var entity = repo.CreateEntity();
        var panel  = new EntityWatchPanel(entity);

        Assert.NotNull(panel.Reflector);
    }

    // ── T-CE10c: Reflector properties are publicly settable ───────────────────

    /// <summary>
    /// T-CE10c: Setting <c>panel.Reflector.EditWindowManager = null</c> compiles and
    /// executes, confirming the property is truly public and the type is accessible.
    /// </summary>
    [Fact]
    public void EntityInspectorPanel_Reflector_EditWindowManager_IsPublicAndSettable()
    {
        var panel = new EntityInspectorPanel();

        // This assignment must compile (verifies ComponentReflector is accessible
        // from this assembly) and must not throw at runtime.
        panel.Reflector.EditWindowManager = null;

        Assert.Null(panel.Reflector.EditWindowManager);
    }

    /// <summary>
    /// T-CE10c (watch panel variant): Same check for <see cref="EntityWatchPanel"/>.
    /// </summary>
    [Fact]
    public void EntityWatchPanel_Reflector_EditWindowManager_IsPublicAndSettable()
    {
        using var repo   = new EntityRepository();
        var entity = repo.CreateEntity();
        var panel  = new EntityWatchPanel(entity);

        panel.Reflector.EditWindowManager = null;

        Assert.Null(panel.Reflector.EditWindowManager);
    }

    // ── T-CE10d: existing EntityInspectorPanelTests still pass ───────────────
    // (This is verified by running the full test suite; there is no separate test
    //  that re-runs the other class here. The CI run covers it.)
    // The following fact documents that the Reflector property does NOT break
    // the existing panel Draw logic by ensuring Draw still runs without exception.

    /// <summary>
    /// T-CE10d regression: adding the <c>Reflector</c> property does not break the
    /// existing <see cref="EntityInspectorPanel.Draw"/> pipeline.
    /// </summary>
    [Collection("ImGui Sequential")]
    public class RegressionSmoke
    {
        [Fact]
        public void EntityInspectorPanel_Draw_StillRunsAfterReflectorAdded()
        {
            using var fixture = new ImGuiTestFixture();
            using var repo    = new EntityRepository();
            var panel    = new EntityInspectorPanel();
            var ctx      = new FakeInspectorContext();
            var session  = new Fdp.Presentation.Adapters.RepositoryAdapter(repo);

            repo.CreateEntity();

            fixture.NewFrame();
            panel.Draw(session, ctx); // must not throw
            fixture.Render();
        }
    }
}
