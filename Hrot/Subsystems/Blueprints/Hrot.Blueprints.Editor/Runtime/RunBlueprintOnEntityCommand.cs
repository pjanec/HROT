using System;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Runtime;

/// <summary>
/// Headless-testable logic for the "Run Blueprint on Selected Entity" toolbar button.
/// All ImGui rendering is kept outside this class; this class only resolves dependencies
/// and delegates to <see cref="BlueprintAttachService"/>.
/// </summary>
/// <remarks>
/// Resolution order:
/// <list type="number">
///   <item>Resolve the selected entity from <paramref name="selectedEntity"/>; if none → report no-op.</item>
///   <item>Resolve the active Blueprint asset from <paramref name="activeAssetRef"/>; if none or not a
///         <see cref="BlueprintAsset"/>, report and return null (no-op).</item>
///   <item>Call <see cref="BlueprintAttachService.AttachToEntity"/> and surface the
///         <see cref="BlueprintAttachResult.Status"/> to <paramref name="report"/>.</item>
/// </list>
/// <para>Run-mode-agnostic: does not gate on sim state.</para>
/// </remarks>
public static class RunBlueprintOnEntityCommand
{
    /// <summary>Button label displayed in the Blueprint toolbar.</summary>
    public const string ToolbarLabel = "Run Blueprint on Selected Entity";

    /// <summary>
    /// Executes the "run blueprint on selected entity" action.
    /// Safe to call headlessly (no ImGui dependency).
    /// </summary>
    /// <param name="world">Live entity repository.</param>
    /// <param name="registry">Blueprint registry the tick system uses.</param>
    /// <param name="selectedEntity">Currently-selected entity (null → no-op with report).</param>
    /// <param name="activeAssetRef">
    ///   The <c>AssetRef</c> from the active canvas context (<c>AiCanvasContext.AssetRef</c>), or null.
    ///   Must be a <see cref="BlueprintAsset"/> for the attach to proceed.
    /// </param>
    /// <param name="report">Receives the human-readable outcome (log line / status text).</param>
    /// <returns>The attach result, or <c>null</c> if the preconditions were not met.</returns>
    public static BlueprintAttachResult? Execute(
        EntityRepository? world,
        BlueprintRegistry? registry,
        Entity? selectedEntity,
        object? activeAssetRef,
        Action<string> report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));

        // 1. Require a world and registry (editor composition guarantee; null only in tests that
        //    haven't fully booted — report gracefully rather than throw).
        if (world is null)
        {
            report("Blueprint run: no world available.");
            return null;
        }
        if (registry is null)
        {
            report("Blueprint run: no blueprint registry available.");
            return null;
        }

        // 2. Require a selected entity.
        if (selectedEntity is null)
        {
            report("Blueprint run: select an entity first.");
            return null;
        }

        // 3. Require an active Blueprint asset.
        if (activeAssetRef is not BlueprintAsset bpAsset)
        {
            if (activeAssetRef is null)
                report("Blueprint run: no blueprint is open. Open a blueprint asset first.");
            else
                report($"Blueprint run: the active asset is not a Blueprint (got {activeAssetRef.GetType().Name}).");
            return null;
        }

        // 4. Attach via the production service (idempotent, run-mode-agnostic).
        var result = BlueprintAttachService.AttachToEntity(world, registry, bpAsset, selectedEntity.Value);

        report(result.Status switch
        {
            BlueprintAttachStatus.Attached        => result.Message,
            BlueprintAttachStatus.AlreadyAttached => result.Message,
            BlueprintAttachStatus.NotRegistered   =>
                $"{result.Message}  Compile / register the blueprint first.",
            BlueprintAttachStatus.NotInstanceKind => result.Message,
            BlueprintAttachStatus.NoSlotAvailable => result.Message,
            _                                     => result.Message,
        });

        return result;
    }
}
