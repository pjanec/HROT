using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Hrot.AiEditor.Persistence.Params;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Identity;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// ⭐ <c>CE-3186</c> (P2, F4 / R-2001 — docs/DESIGN_Orders_Params_Entity_Classes.md §13.8 P2-b) — quick reload compiles ONE asset
/// against the params DTO types already loaded; it never recompiles a DTO (a DTO shape change needs a full rebuild). This says when
/// that would run STALE: the asset names a GENERATED DTO (<c>[ParamsDto(AssetId)]</c>) whose <c>.params.json</c> on disk no longer
/// matches the loaded type, field by field (name + type id, in order).
/// <para>⚠ A hand-written DTO cannot change without a build — that build IS the full rebuild — so it is never refused here. A DTO
/// that is not loaded, or an asset that cannot be found, is not refused either: the compile reports it.</para>
/// </summary>
public static class ParamsDtoStaleness
{
    /// <summary>Null when the asset may quick-reload; otherwise the refusal ("full rebuild needed").</summary>
    /// <param name="paramsDtoTypeId">the asset's <c>ParamsDtoTypeId</c> (FQN).</param>
    /// <param name="paramsAssetsRoot">where the <c>.params.json</c> assets live; <see cref="AssetRoots.BehaviorAssetsRoot"/> when null.</param>
    public static string? Check(string? paramsDtoTypeId, string? paramsAssetsRoot = null)
    {
        if (string.IsNullOrWhiteSpace(paramsDtoTypeId)) return null;
        var loaded = BlackboardTypeHelper.ResolveClrType(paramsDtoTypeId!.Trim());
        if (loaded == typeof(object)) return null;
        if (loaded.GetCustomAttribute<ParamsDtoAttribute>(inherit: false) is not { AssetId: { } id } || !Guid.TryParse(id, out var guid))
            return null;

        string? path = ParamsDtoJson.FindByAssetId(paramsAssetsRoot ?? AssetRoots.BehaviorAssetsRoot, guid);
        if (path == null) return null;
        ParamsDtoAssetDto asset;
        try { asset = ParamsDtoJson.Deserialize(System.IO.File.ReadAllText(path)); }
        catch (Exception) { return null; }

        var onDisk = (asset.Fields ?? new()).Select(f => (f.Name, Type: Norm(f.TypeId))).ToArray();
        var inMemory = loaded.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(f => f.MetadataToken).Select(f => (f.Name, Type: Norm(f.FieldType.FullName))).ToArray();
        if (onDisk.SequenceEqual(inMemory)) return null;

        return $"params DTO {loaded.FullName} changed since the last build ({System.IO.Path.GetFileName(path)}: " +
               $"[{Describe(onDisk)}], loaded: [{Describe(inMemory)}]) — quick reload does not recompile params DTOs; " +
               "a full rebuild is needed (R-2001).";
    }

    private static string Norm(string? typeId) => (typeId ?? "").Trim().Replace("global::", "").Replace('+', '.');

    private static string Describe((string Name, string Type)[] fields)
        => string.Join(", ", fields.Select(f => $"{f.Type} {f.Name}"));
}
