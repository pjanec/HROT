using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Hrot.Common.Constants;
using Hrot.Core.Network;
using Hrot.Map.Definitions.Tkb;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> — the map pack builds ONE entity-authoring surface for every host (spawn adapter, picker, Add Entity
/// action, canvas menu). 🔒 User: "Do we share unified code across simhost and cgf and ig and editor? We should."
/// 📄 docs/DESIGN_Add_Entity_Picker.md §5 "One wiring".
/// </summary>
public sealed class EntityAuthoringPackTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private readonly TkbDatabase _tkb = new();
    private readonly ScenarioEntityCreationRequestSource _requests = new();

    public EntityAuthoringPackTests()
    {
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<NetworkIdentity>();
        NedTkbCatalog.RegisterAll(_tkb);
    }

    public void Dispose() => _world.Dispose();

    private MapInteraction Build(EntityAuthoringInputs? inputs) => MapInteractionPack.Build(new MapInteractionContext
    {
        World = _world,
        StartEnabled = true,
        EntityAuthoring = inputs,
    });

    private EntityAuthoringInputs Inputs(Func<ScenarioEntityCreationRequestSource?>? requests = null)
        => new(() => _tkb, requests ?? (() => _requests), () => null);

    [Fact]
    public void CE1017_AHostThatCanAuthor_GetsTheWholeSurface_FromThePack()
    {
        var mi = Build(Inputs());

        Assert.NotNull(mi.EntityAuthoring);
        Assert.Same(mi.EntityAuthoring!.CanvasMenu, mi.CanvasMenu);
        Assert.Contains("Add Entity", mi.CanvasMenu.CurrentJson());
        foreach (int id in new[] { GlobalActionIds.AddEntityFriendly, GlobalActionIds.AddEntityHostile,
                                   GlobalActionIds.AddEntityNeutral, GlobalActionIds.AddMapGraphic })
            Assert.True(mi.Actions.TryGetHandler(id, out _));
        Assert.NotNull(mi.EntityAuthoring.Pickers);                         // the pack's own registry
        Assert.Same(mi.EntityAuthoring.Spawn, mi.EntityAuthoring.Spawn);    // ONE adapter, built once
    }

    [Fact]
    public void CE1017_AHostThatCannotAuthor_StillGetsACanvasMenu_WithoutAddEntity()
    {
        var mi = Build(null);

        Assert.Null(mi.EntityAuthoring);
        Assert.DoesNotContain("Add Entity", mi.CanvasMenu.CurrentJson());
        Assert.Contains("Measurement Tool", mi.CanvasMenu.CurrentJson());
        Assert.False(mi.Actions.TryGetHandler(GlobalActionIds.AddEntityHostile, out _));
    }

    [Fact]
    public void CE1017_TheSpawnAdapter_WaitsForTheRequestQueue_AndTheMenuFollows()
    {
        ScenarioEntityCreationRequestSource? queue = null;   // the creation pack is composed after the map
        var mi = Build(Inputs(() => queue));

        Assert.Null(mi.EntityAuthoring!.Spawn);
        Assert.DoesNotContain("Add Entity", mi.CanvasMenu.CurrentJson());

        queue = _requests;

        Assert.NotNull(mi.EntityAuthoring.Spawn);
        Assert.Contains("Add Entity", mi.CanvasMenu.CurrentJson());
    }

    [Fact]
    public void CE1017_TheSpawnTool_ArmsThroughTheSameSharedAdapter()
    {
        var mi = Build(Inputs());

        Assert.True(mi.Tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.Spawn));
    }

    [Fact]
    public void CE1017_TheSharedAdapter_IsHandedTheMapsArbiter()
    {
        var mi = Build(Inputs());
        Assert.False(mi.Tools.IsRegistered(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceArea));

        _ = mi.EntityAuthoring!.Spawn;   // the adapter registers its authoring tools on the arbiter it was handed

        Assert.True(mi.Tools.IsRegistered(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceArea));
    }

    [Fact]
    public void CE1017_AHostShellRegistry_IsAdopted_NotDuplicated()
    {
        var shell = EntityAuthoring.CreatePickers();
        var mi = Build(Inputs() with { HostPickers = () => shell });

        Assert.Same(shell, mi.EntityAuthoring!.Pickers);
    }
}
