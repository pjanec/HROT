using System;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.References;

/// <summary>
/// Resolves a composed BTree AiPrimitive node's <c>MethodFqn</c> back to the Blueprint asset that
/// generated it, and builds the canonical reference-catalog key both sides of the BTree↔Blueprint
/// reference agree on — using ONLY plain-string parsing plus catalog data.
/// <para>
/// <b>Identity rule — ⛔ SUPERSEDED by <c>CE-417</c> B-1 (user, <c>2026-10-01</c>).</b> A composed BTree node now names
/// its blueprint by <c>BlueprintAssetId</c> (+ <c>BlueprintName</c> to heal a rename), exactly as an HSM binding does;
/// the generated <c>…_Bp.TickCore</c> is DERIVED (<see cref="EffectiveMethodFqn"/>, and <c>BTreeBlueprintBindings</c> on
/// the build side), never persisted. <see cref="ResolveBinding"/> is the entry point. 📄
/// <c>DESIGN_Behavior_Action_Binding.md</c> §5.5. <i>(Was: "identified by their FQN string, NOT by a persisted AssetId";
/// the FQN parse below survives to read a file the editor has not healed yet.)</i>
/// </para>
/// <para>
/// <b>No Roslyn / compiler dependency here (AIE-053):</b> this type lives in the foundational shared
/// editor layer, which must not take a dependency on <c>Hrot.Blueprints.Compiler</c>/Roslyn. It
/// therefore never calls the compiler to compute a class name — ⚠ <see cref="EffectiveMethodFqn"/>'s fallback uses
/// <c>BlueprintClassNaming</c>, the persistence mirror the generators already share. Instead, the
/// blueprint-editor side (which legitimately references the compiler) precomputes each blueprint's
/// generated class name and publishes it — on the asset via <see cref="IComposedBlueprintIdentity"/>
/// and as a reference-catalog element keyed by <see cref="ElementKey"/>. Resolution here is a pure
/// string comparison of the node FQN's declaring-type name against that published
/// <see cref="IComposedBlueprintIdentity.GeneratedClassName"/>.
/// </para>
/// </summary>
public static class ComposedBlueprintResolver
{
    /// <summary>Method name the Blueprint compiler always emits for an AiPrimitive composition.</summary>
    public const string DefaultMethodName = "TickCore";

    /// <summary>Fixed namespace the Blueprint compiler emits generated AiPrimitive classes into.</summary>
    public const string GeneratedNamespace = "Hrot.AI.Behaviors.Generated";

    /// <summary>
    /// Parses a generated AiPrimitive TickCore-style <c>methodFqn</c>
    /// (<c>{Namespace...}.{SanitizedName}_{BlueprintId:X8}_Bp.{MethodName}</c>) into its generated
    /// class name (<c>{SanitizedName}_{BlueprintId:X8}_Bp</c>) and method name. Returns
    /// <see langword="false"/> (clearing the out params) for any FQN that doesn't match the
    /// pattern — including ordinary hand-written action/condition FQNs, which is how callers
    /// distinguish a composed AiPrimitive binding from a normal one. Purely lexical — no hashing.
    /// </summary>
    public static bool TryParse(string? methodFqn, out string generatedClassName, out string methodName)
    {
        generatedClassName = string.Empty;
        methodName         = string.Empty;

        if (string.IsNullOrEmpty(methodFqn))
            return false;

        int lastDot = methodFqn.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == methodFqn.Length - 1)
            return false;

        string declFqn         = methodFqn.Substring(0, lastDot);
        string candidateMethod = methodFqn.Substring(lastDot + 1);

        int declDot = declFqn.LastIndexOf('.');
        string declShort = declDot >= 0 ? declFqn.Substring(declDot + 1) : declFqn;

        // Declaring type must look like "{Name}_{8 hex}_Bp".
        const string bpSuffix = "_Bp";
        if (!declShort.EndsWith(bpSuffix, StringComparison.Ordinal))
            return false;
        string withoutSuffix = declShort.Substring(0, declShort.Length - bpSuffix.Length);

        int us = withoutSuffix.LastIndexOf('_');
        if (us <= 0) // underscore missing, or at position 0 (empty name) — not a composed class name.
            return false;

        string hexPart = withoutSuffix.Substring(us + 1);
        if (hexPart.Length != 8 || !IsHex(hexPart))
            return false;

        generatedClassName = declShort;
        methodName         = candidateMethod;
        return true;

        static bool IsHex(string s)
        {
            foreach (var c in s)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }

    /// <summary>
    /// The canonical reference-catalog key for a composed AiPrimitive:
    /// <c>{GeneratedClassName}.{MethodName}</c>. Both sides of the reference agree on this format —
    /// the blueprint side keys its published element with it, and the BTree side builds its
    /// reference <c>TargetKey</c> with it. Pure string concat.
    /// </summary>
    public static string ElementKey(string generatedClassName, string methodName = DefaultMethodName)
        => $"{generatedClassName}.{methodName}";

    /// <summary>
    /// Builds the canonical reference-catalog target key for a composed node's
    /// <paramref name="methodFqn"/>: <c>{GeneratedClassName}.{MethodName}</c> (namespace-independent).
    /// Returns <see langword="null"/> when <paramref name="methodFqn"/> is not a composed AiPrimitive FQN.
    /// </summary>
    public static string? ReferenceKeyFor(string? methodFqn)
        => TryParse(methodFqn, out var generatedClassName, out var methodName)
            ? ElementKey(generatedClassName, methodName)
            : null;

    /// <summary>
    /// Resolves a composed node's <paramref name="methodFqn"/> to the owning Blueprint asset by
    /// scanning <paramref name="catalog"/> for a <see cref="AssetKind.Blueprint"/> asset that
    /// reports (via <see cref="IComposedBlueprintIdentity"/>) a <c>GeneratedClassName</c> equal to
    /// the FQN's declaring-type name. No hashing — the blueprint side already precomputed that name.
    /// <para>
    /// Returns <see langword="null"/> when: <paramref name="methodFqn"/> is not a composed-AiPrimitive
    /// FQN (e.g. a hand-written action — not an error, just "not applicable"); or no catalog blueprint's
    /// published identity matches (a dangling reference — the blueprint was renamed or deleted).
    /// </para>
    /// </summary>
    public static IEditableAsset? Resolve(string? methodFqn, IAssetCatalog? catalog)
    {
        if (catalog is null)
            return null;
        if (!TryParse(methodFqn, out var generatedClassName, out _))
            return null;

        foreach (var asset in catalog.All)
        {
            if (asset.Kind != AssetKind.Blueprint)
                continue;
            if (asset is IComposedBlueprintIdentity id &&
                string.Equals(id.GeneratedClassName, generatedClassName, StringComparison.Ordinal))
                return asset;
        }
        return null;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-417</c> B-1 (slice 4c) — the blueprint a BINDING runs: by its asset id first (the identity B-1 persists),
    /// else by a legacy generated <c>MethodFqn</c> (a file the editor has not healed yet). Null when the binding names no
    /// blueprint, or the blueprint is not in <paramref name="catalog"/> (dangling).
    /// </summary>
    public static IEditableAsset? ResolveBinding(BehaviorActionBinding? binding, IAssetCatalog? catalog)
    {
        if (binding is null || catalog is null) return null;
        if (binding.BlueprintAssetId != Guid.Empty)
            return catalog.FindByAssetId(binding.BlueprintAssetId) is { Kind: AssetKind.Blueprint } bp ? bp : null;
        return Resolve(binding.MethodFqn, catalog);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-417</c> B-1 — the method a binding calls: its <c>MethodFqn</c>, or for a blueprint binding the generated
    /// <c>…_Bp.TickCore</c> DERIVED from the blueprint (B-1: never persisted). The class comes from the catalogue's
    /// published identity when it has the asset, else from the persisted name — the same fallback the generator uses
    /// (<c>BTreeBlueprintBindings</c>). Null when the binding names nothing.
    /// </summary>
    public static string? EffectiveMethodFqn(BehaviorActionBinding? binding, IAssetCatalog? catalog = null)
    {
        if (binding is null) return null;
        if (!string.IsNullOrEmpty(binding.MethodFqn)) return binding.MethodFqn;
        if (binding.BlueprintAssetId == Guid.Empty) return null;

        string? cls = (catalog?.FindByAssetId(binding.BlueprintAssetId) as IComposedBlueprintIdentity)?.GeneratedClassName;
        if (string.IsNullOrEmpty(cls) && !string.IsNullOrEmpty(binding.BlueprintName))
            cls = Hrot.AiEditor.Persistence.Emit.BlueprintClassNaming.ClassName(binding.BlueprintAssetId, binding.BlueprintName!);
        return string.IsNullOrEmpty(cls) ? null : Hrot.AiEditor.Persistence.Emit.BlueprintClassNaming.TickCoreFqn(cls!);
    }
}
