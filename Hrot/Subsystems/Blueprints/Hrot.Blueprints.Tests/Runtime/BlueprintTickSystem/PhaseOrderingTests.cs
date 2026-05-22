using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.MockSystems;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// SC3: BlueprintTickSystem runs before LocomotionDispatcherSystem (phase ordering).
/// Per Runtime DD §11.2.
/// </summary>
[Collection("DebugProbe")]
public sealed class PhaseOrderingTests
{
    // SC3: Blueprint Tick writes ActiveAction; MockLocomotionDispatcher (added as aux sim system)
    //      runs after TickSystem and sees the updated value.
    [Fact]
    public void BlueprintTick_CommandVisibleToDispatcher_SameFrame()
    {
        using var fixture = new BlueprintTestFixture();

        // Register LocomotionChannel component
        fixture.World.RegisterComponent<LocomotionChannel>();

        // Register a Blueprint whose Tick directly sets ActiveAction = 42 on the entity
        var asset = new BlueprintAsset
            { AssetId = new Guid("A1B2C3D4-0000-0000-0000-000000000000"), Name = "LocoWriter" };

        var staging = fixture.Registry.BeginStaging();
        staging.Add(BlueprintIdHash.Compute(asset.AssetId),
            new BlueprintDefinition
            {
                Name = "LocoWriter",
                Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
                StructureHash = 0x1122334455667788UL,
                StateSize = Unsafe.SizeOf<FakeInstanceBp.State>(),
                InitDefault = b => b.Clear(),
                Tick = TickWritesLocoChannel,
                StateFields = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
                {
                    ["TickCount"] = new BlueprintFieldDescriptor(
                        "TickCount", typeof(int),
                        OffsetBytes: Unsafe.SizeOf<BlueprintLatentCursor>(),
                        SizeBytes: sizeof(int), CategoryOrEmpty: ""),
                },
            });
        fixture.Registry.CommitStaging(staging);

        var dispatcher = new MockLocomotionDispatcher();
        fixture.AddSimulationSystem(dispatcher);

        var entity = fixture.CreateEntity();
        fixture.World.AddComponent(entity, default(LocomotionChannel));
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        // Blueprint tick ran BEFORE dispatcher, so dispatcher saw ActiveAction != 0
        Assert.Equal(1, dispatcher.InvokeCount);
    }

    private static void TickWritesLocoChannel(
        Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
        Entity self, float time, float deltaTime, uint instanceVersion)
    {
        // Directly mutate the component (not via ECB) so it is visible in the same frame
        var repo = (EntityRepository)view;
        if (repo.HasComponent<LocomotionChannel>(self))
            repo.SetComponent(self, new LocomotionChannel { ActiveAction = 42 });
    }
}
