using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// DEBT-MVE-002 proof test: StateFields emitted by the compiler are correct and observable at runtime.
/// Verifies that a compiled (not hand-built) Instance blueprint's BlueprintDefinition.StateFields
/// contains the right Offset/Size entries, and that BlueprintStateView.TryGetField returns live values.
/// No hand-built def, no DebugMap workaround — pure end-to-end compile→register→attach→tick→read.
/// </summary>
[Collection("DebugProbe")]
public sealed class StateFields_ProofTests
{
    // PROOF-001: After compile+load, the registry definition for HealthRegen
    // has StateFields populated with the correct Offset/Size for each variable.
    // HealthRegen has: CurrentHealth (float, offset 16, size 4), MaxHealth (float, offset 20, size 4).
    // These offsets come from FieldLayout with startOffset:16 (after the 16-byte BlueprintLatentCursor).
    [Fact]
    public void CompiledInstanceBlueprint_StateFields_ContainsVariablesWithCorrectOffsetAndSize()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(asset);

        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            "HealthRegen blueprint not found in registry after CompileAndLoad.");

        // StateFields must not be empty — no hand-built def, no DebugMap path.
        Assert.True(def!.StateFields.Count > 0,
            "StateFields is empty — the compiler did not emit StateFields (DEBT-MVE-002 not closed).");

        // CurrentHealth: float at offset 16 (byte 0 of Variables, right after 16-byte cursor), size 4.
        Assert.True(def.StateFields.TryGetValue("CurrentHealth", out var currentHealthFd),
            "StateFields does not contain 'CurrentHealth'.");
        Assert.Equal(typeof(float), currentHealthFd!.ClrType);
        Assert.Equal(16, currentHealthFd.OffsetBytes);
        Assert.Equal(4,  currentHealthFd.SizeBytes);

        // MaxHealth: float at offset 20 (16 + sizeof(float)), size 4.
        Assert.True(def.StateFields.TryGetValue("MaxHealth", out var maxHealthFd),
            "StateFields does not contain 'MaxHealth'.");
        Assert.Equal(typeof(float), maxHealthFd!.ClrType);
        Assert.Equal(20, maxHealthFd.OffsetBytes);
        Assert.Equal(4,  maxHealthFd.SizeBytes);
    }

    // PROOF-002: BlueprintStateView.TryGetField<float> reads the live InitDefault value from slot memory.
    // HealthRegen.InitDefault sets CurrentHealth = 100 and MaxHealth = 100.
    // After attach + tick, TryGetField must return those live values — proving the
    // emitted offsets are correct end-to-end (not just metadata).
    [Fact]
    public void CompiledInstanceBlueprint_TryGetField_ReturnsLiveValueFromSlot()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);
        fixture.TickFrame(0.016f);

        var view = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(view);

        // Read CurrentHealth via the compiled definition's StateFields — no DebugMap, no hand-built def.
        Assert.True(view!.Value.TryGetField<float>("CurrentHealth", out var currentHealth),
            "TryGetField returned false for 'CurrentHealth' — StateFields offset or size mismatch.");
        Assert.Equal(100f, currentHealth);

        // Read MaxHealth to confirm the second field's offset is also correct.
        Assert.True(view!.Value.TryGetField<float>("MaxHealth", out var maxHealth),
            "TryGetField returned false for 'MaxHealth' — StateFields offset or size mismatch.");
        Assert.Equal(100f, maxHealth);
    }
}
