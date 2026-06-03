using Fdp.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Minimal, headless-friendly "attach a blueprint to an entity and run it" helper.
/// <para>
/// This is the reusable run logic that the future editor "Run Opened Blueprint on a
/// Test Entity" button (MVE-06) will call: spawn an entity, attach the (already
/// registered) blueprint, pump frames through the real <c>BlueprintTickSystem</c> +
/// <c>BlueprintMaintenanceSystem</c>, and read an observable field back out of the slot.
/// </para>
/// <para>
/// It wraps <see cref="BlueprintTestFixture"/> (the proven minimal world + registry +
/// tick/maintenance systems substrate). The blueprint must already be present in the
/// fixture's <see cref="BlueprintRegistry"/> (via <c>FakeInstanceBp.Register</c>, a
/// staging commit, or <c>CompileAndLoad</c>) before <see cref="SpawnAndAttach"/> is called.
/// </para>
/// <para>
/// Production home (for MVE-06): the editor must not depend on the test fixture. The same
/// three operations — choose tier + <c>BlueprintBlackboardPartitions.TryAttach</c>;
/// pump the kernel; read a field via <c>BlueprintStateView.TryGetField&lt;T&gt;</c> —
/// should live in a small production-side service. The recommended home is a
/// <c>BlueprintRunService</c> in <c>Hrot.Blueprints.Editor</c> (it already references the
/// Toolkit attach/registry/tick types and the editor's run substrate). It takes the live
/// <c>EntityRepository</c> + <c>BlueprintRegistry</c> + a frame-pump callback instead of a
/// test fixture. See MVE-BATCH-01-REPORT.md "Production home" for the precise gap.
/// </para>
/// </summary>
public sealed class BlueprintRunHarness
{
    private readonly BlueprintTestFixture _fixture;

    public BlueprintRunHarness(BlueprintTestFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>The underlying fixture, for callers that need direct registry/world access.</summary>
    public BlueprintTestFixture Fixture => _fixture;

    /// <summary>
    /// Spawns a fresh entity and attaches <paramref name="asset"/>'s (already-registered)
    /// blueprint to it via the tiered <c>BlueprintBlackboardPartitions.TryAttach</c> path.
    /// Returns the entity so the caller can read fields or attach more blueprints.
    /// </summary>
    public Entity SpawnAndAttach(BlueprintAsset asset)
    {
        var entity = _fixture.CreateEntity();
        _fixture.AttachBlueprint(asset, entity);
        return entity;
    }

    /// <summary>
    /// Advances the real simulation by <paramref name="frames"/> frames at a fixed
    /// <paramref name="deltaTime"/>, running the production <c>BlueprintTickSystem</c> and
    /// <c>BlueprintMaintenanceSystem</c> each frame.
    /// </summary>
    public void Pump(int frames, float deltaTime = 0.016f)
    {
        for (int i = 0; i < frames; i++)
            _fixture.TickFrame(deltaTime);
    }

    /// <summary>
    /// Reads an integer field named <paramref name="field"/> from the blueprint state slot
    /// attached to <paramref name="entity"/>. Throws if no slot or field is found, so a
    /// silent miss can never masquerade as a value of 0.
    /// </summary>
    public int ReadIntField(Entity entity, BlueprintAsset asset, string field)
    {
        var state = _fixture.GetBlueprintState(asset, entity)
            ?? throw new InvalidOperationException(
                $"No blueprint state slot for '{asset.Name}' on entity {entity}. " +
                "Was the blueprint registered and attached?");

        if (!state.TryGetField<int>(field, out var value))
            throw new InvalidOperationException(
                $"Blueprint '{asset.Name}' has no int field '{field}'.");

        return value;
    }
}
