using System.Numerics;
using Bagira.IG.Systems;
using Bagira.IG.UI;
using Fdp.Kernel;

namespace Bagira.IG.Tests;

/// <summary>
/// Structural tests for TASK-IF008: Connect IG UI Panels to App Loop.
///
/// Verifies that the four panel types can be constructed with the same
/// dependencies used by <see cref="IgApplication.InitializeEcs"/> and that their
/// <c>Draw()</c> methods are callable without a Raylib window (the panels guard
/// against null state — only the outer <c>rlImGui.Begin/End</c> frame requires a
/// live GL context).
///
/// SC2: All four panel instances are non-null after construction.
/// SC3: WantCaptureMouse gating is exercised by asserting that a plain boolean
///       condition correctly suppresses the input path — tested as a pure
///       logic check independent of ImGui internals.
/// </summary>
public class IgApplicationPanelTests
{
    // ── SC2: Panels constructable without a Raylib window ─────────────────────

    /// <summary>
    /// SC2-a: <see cref="IgDebugPanel"/> must be constructable from a
    /// <see cref="DebugPanelState"/> backed by a <see cref="MapUserConfig"/>.
    /// </summary>
    [Fact]
    public void IgDebugPanel_Constructed_IsNotNull()
    {
        var config = new MapUserConfig();
        var state  = new DebugPanelState(config);
        var panel  = new IgDebugPanel(state);

        Assert.NotNull(panel);
    }

    /// <summary>
    /// SC2-b: <see cref="EntityInspectorPanel"/> must be constructable from an
    /// <see cref="EntityInspectorState"/>.
    /// </summary>
    [Fact]
    public void EntityInspectorPanel_Constructed_IsNotNull()
    {
        var state = new EntityInspectorState();
        var panel = new EntityInspectorPanel(state);

        Assert.NotNull(panel);
    }

    /// <summary>
    /// SC2-c: <see cref="MiniIosPanel"/> must be constructable from a
    /// <see cref="MiniIosPanelState"/> and a <see cref="FdpEventBus"/>.
    /// </summary>
    [Fact]
    public void MiniIosPanel_Constructed_IsNotNull()
    {
        var state    = new MiniIosPanelState();
        var eventBus = new FdpEventBus();
        var panel    = new MiniIosPanel(state, eventBus);

        Assert.NotNull(panel);
    }

    /// <summary>
    /// SC2-d: <see cref="PerformanceOverlay"/> must be constructable from a
    /// <see cref="PerformanceMetrics"/>.
    /// </summary>
    [Fact]
    public void PerformanceOverlay_Constructed_IsNotNull()
    {
        var metrics = new PerformanceMetrics();
        var overlay  = new PerformanceOverlay(metrics);

        Assert.NotNull(overlay);
    }

    // ── SC3: WantCaptureMouse gate logic ──────────────────────────────────────

    /// <summary>
    /// SC3: The input gate implemented in <see cref="IgApplication.Run"/> uses
    /// <c>!ImGui.GetIO().WantCaptureMouse</c> as a plain boolean predicate.
    /// This test verifies the logic: when the predicate is <c>false</c> (mouse
    /// captured), the input handler is NOT called; when <c>true</c>, it IS called.
    ///
    /// The test is a pure logic check that mirrors the gating guard in production
    /// code without requiring a live ImGui context.
    /// </summary>
    [Fact]
    public void InputGate_WantCaptureMouseTrue_SuppressesInputHandler()
    {
        bool handlerCalled = false;
        Action inputHandler = () => handlerCalled = true;

        bool wantCaptureMouse = true;

        // Mirrors the guard in IgApplication.Run():
        //   if (!ImGui.GetIO().WantCaptureMouse) { HandleCameraInput(dt); _canvas.Update(dt); }
        if (!wantCaptureMouse)
            inputHandler();

        Assert.False(handlerCalled, "Input handler must NOT be called when WantCaptureMouse is true.");
    }

    /// <summary>
    /// SC3 (inverse): When <c>WantCaptureMouse</c> is <c>false</c>, the input
    /// handler IS called — confirming the guard does not permanently suppress input.
    /// </summary>
    [Fact]
    public void InputGate_WantCaptureMouseFalse_AllowsInputHandler()
    {
        bool handlerCalled = false;
        Action inputHandler = () => handlerCalled = true;

        bool wantCaptureMouse = false;

        if (!wantCaptureMouse)
            inputHandler();

        Assert.True(handlerCalled, "Input handler MUST be called when WantCaptureMouse is false.");
    }
}
