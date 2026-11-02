using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Panels;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Scenario;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐ CE-4210 P2 (silent-default rule) — a host that composes the diagnostics bundle HAS the world, so the inspector panel's reflector must come out wired with the bind context
/// (asserted on the CONSTRUCTED panel, not the bundle's source): the params declared in the resident header are in scope, and a bind writes the entity's <c>ParamBindings</c>.
/// </summary>
public sealed class TheInspectorGetsTheBindContextTests
{
    [Fact]
    public void TheComposedPanel_BindsThroughTheWorldsHeaderParams()
    {
        using var repo = new EntityRepository();
        repo.RegisterManagedComponent<ParamBindings>();
        var header = new ScenarioHeader("") { Params = new[] { new ScenarioParamDecl("side", "ForceId") } };
        ScenarioDescription.Stamp(repo, null, "m", null, null, ScenarioDescription.HeaderJsonOf(header));
        var entity = repo.CreateEntity();
        var adapter = new RepositoryAdapter(repo);
        var panel = new EntityInspectorPanel();

        var wm = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(IntPtr.Zero, 512, 512));
        UiBundleHost.Compose(new IUiBundle[]
        {
            new DiagnosticsWindowsBundle(new DiagnosticsHostServices(
                "t_", "T", "Scenario", panel, () => adapter, () => new Fdp.Presentation.Abstractions.InspectorState(), new EventBrowserPanel(),
                new Vector4(0, 0, 0, 1), () => null, () => repo)),
        }, new UiBundleContext(wm));

        var factory = panel.Reflector.BindContextFactory;
        Assert.NotNull(factory);                                         // a host with a world must not leave the reflector without one
        var ctx = Assert.IsType<EntityBindContext>(factory!(entity, typeof(Fdp.Toolkit.Replication.Components.NetworkIdentity)));
        Assert.Equal("NetworkIdentity", ctx.Prefix);
        Assert.Equal(new ParamInScope("side", "ForceId"), Assert.Single(ctx.Params));
        ctx.Bind("Value", "side");
        Assert.Equal("side", ((ParamBindings)adapter.GetComponent(entity, typeof(ParamBindings))!).Bindings["NetworkIdentity.Value"]);
        Assert.Null(factory(entity, typeof(ParamBindings)));            // the bindings component itself is not bindable
    }
}
