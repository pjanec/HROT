using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Runtime;

/// <summary>
/// Editor-side forwarder that converts a <see cref="BlueprintAsset"/> to a runtime
/// <c>int blueprintId</c> and delegates to <see cref="BlueprintInstanceService"/>.
/// The core attach/detach logic lives in <c>Fdp.Toolkits.Blueprints</c> so that
/// CGF/genesis and mid-runtime events can call it without an editor dependency.
/// </summary>
/// <remarks>
/// <para>
/// The sequence:
/// <list type="number">
///   <item><c>BlueprintIdHash.Compute(asset.AssetId)</c> → runtime id.</item>
///   <item>Delegate to <see cref="BlueprintInstanceService.AttachToEntity"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Backward-compatible:</b> the method signature is identical to the prior
/// implementation. Callers that pass a <c>BlueprintAsset</c> continue to work
/// unchanged.
/// </para>
/// </remarks>
public static class BlueprintAttachService
{
    /// <summary>
    /// Attaches <paramref name="asset"/>'s (already-registered) Instance blueprint to
    /// <paramref name="entity"/> in <paramref name="world"/> by forwarding to the core
    /// <see cref="BlueprintInstanceService.AttachToEntity"/>.
    /// See <see cref="BlueprintInstanceService"/> for the full attach sequence,
    /// idempotency, and run-mode semantics.
    /// </summary>
    /// <param name="world">The live entity repository hosting the entity.</param>
    /// <param name="registry">The registry the runtime ticks against (must already contain the blueprint).</param>
    /// <param name="asset">The authoring asset identifying the blueprint to attach.</param>
    /// <param name="entity">The target entity (must already exist in <paramref name="world"/>).</param>
    /// <returns>A classified <see cref="BlueprintAttachResult"/>.</returns>
    public static BlueprintAttachResult AttachToEntity(
        EntityRepository world,
        BlueprintRegistry registry,
        BlueprintAsset asset,
        Entity entity)
    {
        if (world is null)    throw new ArgumentNullException(nameof(world));
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (asset is null)    throw new ArgumentNullException(nameof(asset));

        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);
        return BlueprintInstanceService.AttachToEntity(world, registry, blueprintId, entity);
    }
}
