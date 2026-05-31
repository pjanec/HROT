using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime.BlueprintTickSystem;

/// <summary>
/// BPF-006: Verifies the IReloadLogSink interface shape and that BlueprintTickSystem
/// calls OnHardReset with the correct entity and hash context.
/// </summary>
[Collection("DebugProbe")]
public sealed class ReloadLogSinkTests
{
    private sealed class SpySink : IReloadLogSink
    {
        public record HardResetCall(int BlueprintId, Entity Entity, ulong OldHash, ulong NewHash);
        public record SoftReloadCall(int BlueprintId, Entity Entity, ulong Hash);

        public List<HardResetCall> HardResets  { get; } = new();
        public List<SoftReloadCall> SoftReloads { get; } = new();

        public void OnHardReset(int blueprintId, Entity entity, ulong oldHash, ulong newHash)
            => HardResets.Add(new HardResetCall(blueprintId, entity, oldHash, newHash));

        public void OnSoftReload(int blueprintId, Entity entity, ulong hash)
            => SoftReloads.Add(new SoftReloadCall(blueprintId, entity, hash));
    }

    [Fact]
    public void OnHardReset_CalledWith_CorrectEntity_And_Hashes()
    {
        var spy     = new SpySink();
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset   = FakeInstanceBp.MakeAsset();

        var entity  = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        // Commit new staging with a DIFFERENT structure hash
        ulong newHash = 0xDEADBEEF12345678UL;
        var staging = fixture.Registry.BeginStaging();
        staging.Add(FakeInstanceBp.BlueprintId, new BlueprintDefinition
        {
            Name          = "FakeInstance",
            Kind          = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
            StructureHash = newHash,
            StateSize     = FakeInstanceBp.StateSize,
            InitDefault   = FakeInstanceBp.InitDefault,
            Tick          = FakeInstanceBp.Tick,
        });
        fixture.Registry.CommitStaging(staging);

        // Tick with spy-backed system
        var sinkSystem = new Fdp.Toolkit.Blueprints.Systems.BlueprintTickSystem(fixture.Registry, spy);
        sinkSystem.Execute(fixture.World, 0.016f);

        Assert.Single(spy.HardResets);
        var call = spy.HardResets[0];
        Assert.Equal(FakeInstanceBp.BlueprintId, call.BlueprintId);
        Assert.Equal(entity, call.Entity);
        // Slot stores the lower 32 bits of StructureHash (DEBT-014 truncation).
        Assert.Equal(unchecked((ulong)(uint)FakeInstanceBp.StructureHash), call.OldHash);
        Assert.Equal(newHash, call.NewHash);
    }

    [Fact]
    public void OnSoftReload_InterfaceMethod_IsCallable()
    {
        // Verifies the interface method signature exists and is callable.
        // (No production code path calls OnSoftReload yet; this confirms the interface contract.)
        var spy    = new SpySink();
        var entity = new Entity(42, 1);

        ((IReloadLogSink)spy).OnSoftReload(999, entity, 0xABCDEF01UL);

        Assert.Single(spy.SoftReloads);
        Assert.Equal(999, spy.SoftReloads[0].BlueprintId);
        Assert.Equal(entity, spy.SoftReloads[0].Entity);
        Assert.Equal(0xABCDEF01UL, spy.SoftReloads[0].Hash);
    }
}
