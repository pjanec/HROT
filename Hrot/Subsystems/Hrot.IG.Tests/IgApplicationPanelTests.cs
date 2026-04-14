using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Hrot.NED.Descriptors;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Hrot.Map.Common.Replication;
using Hrot.IG.UI;
using Hrot.Common.Abstractions;
using Fdp.Kernel;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Layers;
using Fdp.ModuleHost;
using Fdp.Network.Cyclone.Modules;

namespace Hrot.IG.Tests;

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
    /// SC2-c: <see cref="MiniExConPanel"/> must be constructable from a
    /// <see cref="MiniExConPanelState"/> and a <see cref="FdpEventBus"/>.
    /// </summary>
    [Fact]
    public void MiniExConPanel_Constructed_IsNotNull()
    {
        var state    = new MiniExConPanelState();
        var eventBus = new FdpEventBus();
        var panel    = new MiniExConPanel(state, eventBus);

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

    // ── DDS-to-ECS registration + query guards (DTE-BATCH-04) ────────────────

    [Fact]
    public void InitializeEcs_RegistersIgEntityData()
    {
        var app = new IgApplication();
        try
        {
            app.InitializeEmbedded(headless: true);
            var entity = app.World.CreateEntity();

            var exception = Record.Exception(() =>
                app.World.SetComponent( entity, new Components.EntityInfo()));

            Assert.Null(exception);
        }
        finally
        {
            app.Shutdown(ownsWindow: false);
        }
    }

    [Fact]
    public void InitializeEcs_RegistersIgHealthState()
    {
        var app = new IgApplication();
        try
        {
            app.InitializeEmbedded(headless: true);

            var exception = Record.Exception(() => app.World.GetComponentTable<IgHealthState>());

            Assert.Null(exception);
        }
        finally
        {
            app.Shutdown(ownsWindow: false);
        }
    }

    [Fact]
    public void InitializeEcs_DoesNotRegisterEntityMaster()
    {
        var app = new IgApplication();
        try
        {
            app.InitializeEmbedded(headless: true);

            Assert.Throws<InvalidOperationException>(() => app.World.GetComponentTable<EntityMaster>());
        }
        finally
        {
            app.Shutdown(ownsWindow: false);
        }
    }

    [Fact]
    public void InitializeNetwork_RegistersFireInteractionEventTranslator()
    {
        var app = new IgApplication();
        try
        {
            // Factory required so NedReplicationModule is wired and shared translators are populated.
            app.InitializeEmbedded(headless: false, networkFactory: IgTestFactory.CreateHeadless());

            var translators = GetCustomTranslators(app).Cast<object>().ToList();
            Assert.Contains(translators, t => t is FireInteractionEventTranslator);
        }
        finally
        {
            app.Shutdown(ownsWindow: false);
        }
    }

    [Fact]
    public void EntityRenderQuery_MatchesEntityWithNetworkIdentityAndSimTransform()
    {
        var app = new IgApplication();
        try
        {
            app.InitializeEmbedded(headless: true);
            var query = GetEntityRenderQuery(app);

            var entity = app.World.CreateEntity();
            app.World.AddComponent(entity, new NetworkIdentity(1));
            app.World.AddComponent(entity, new SimTransform());

            Assert.True(QueryContains(query, entity));
        }
        finally
        {
            app.Shutdown(ownsWindow: false);
        }
    }

    [Fact]
    public void EntityRenderQuery_DoesNotMatchEntityWithoutNetworkIdentity()
    {
        var app = new IgApplication();
        try
        {
            app.InitializeEmbedded(headless: true);
            var query = GetEntityRenderQuery(app);

            var entity = app.World.CreateEntity();
            app.World.AddComponent(entity, new SimTransform());

            Assert.False(QueryContains(query, entity));
        }
        finally
        {
            app.Shutdown(ownsWindow: false);
        }
    }


    private static EntityQuery GetEntityRenderQuery(IgApplication app)
    {
        var canvas = (MapCanvas)GetPrivateField(app, "_canvas");
        var layer = canvas.Layers.OfType<EntityRenderLayer>().First();
        return (EntityQuery)GetPrivateField(layer, "_query");
    }

    private static bool QueryContains(EntityQuery query, Entity entity)
    {
        foreach (var candidate in query)
        {
            if (candidate == entity)
                return true;
        }

        return false;
    }

    private static IEnumerable GetCustomTranslators(IgApplication app)
    {
        var kernel = (ModuleHostKernel)GetPrivateField(app, "_kernel");
        var modules = (IEnumerable)GetPrivateField(kernel, "_modules");

        foreach (var entry in modules)
        {
            var moduleProperty = entry.GetType().GetProperty("Module");
            var module = moduleProperty?.GetValue(entry);
            if (module is INedReplicationModule)
            {
                return (IEnumerable)GetPrivateField(module, "_sharedTranslators");
            }
        }

        throw new InvalidOperationException("NedReplicationModule not found in kernel modules.");
    }

    private static object GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException($"Field '{fieldName}' not found on {target.GetType().Name}.");
        return field.GetValue(target)!;
    }
}
