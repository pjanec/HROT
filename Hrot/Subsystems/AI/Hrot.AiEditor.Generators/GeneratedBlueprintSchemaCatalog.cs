using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Lightweight compile-time model of one <c>*.bp.json</c> blueprint asset's identity + AiPrimitive
/// parameter schema — everything the BTree generator needs to reason about a blueprint composed as a
/// host-BTree node (<c>DelegateShape = AiPrimitiveTickCore</c>) WITHOUT resolving its Roslyn symbol.
///
/// <para>
/// Root cause this exists to work around: the Blueprint source generator
/// (<c>Hrot.Blueprints.Generators</c>) and the BTree source generator (this project) are SIBLING
/// Roslyn <c>IIncrementalGenerator</c>s driven from the SAME input <see cref="Compilation"/>. By
/// design, sibling generators cannot see each other's generated output within one generation pass —
/// each receives the pre-generation compilation via <c>CompilationProvider</c>, and the individual
/// outputs are only merged for the final emit. So a blueprint's generated
/// <c>{SanitizedName}_{BlueprintId:X8}_Bp</c> class (and its nested <c>Params</c>/<c>WorkingState</c>/
/// <c>TickCore</c>) is NEVER resolvable via <see cref="Compilation.GetTypeByMetadataName(string)"/>
/// from inside this generator — not even in a fully successful real build.
/// </para>
///
/// <para>
/// This catalog re-derives just enough of that generated shape (class identity + Params field list)
/// straight from the same <c>.bp.json</c> AdditionalText the Blueprint generator itself parses.  It
/// deliberately does NOT reference <c>Hrot.Blueprints.Compiler</c> — the parsing this project needs
/// (top-level <c>Parameters</c> array) isn't exposed by that project's lightweight
/// <c>BlueprintSignatureParser</c> (which only extracts exported-function graph I/O, not the
/// AiPrimitive <c>Parameters</c> block), and a full cross-subsystem dependency isn't warranted for a
/// handful of small mirrored helpers.  <c>SanitizeName</c> mirrors
/// <c>Hrot.Blueprints.Core.Compiler.Emit.Sanitizer.SanitizeName</c> and the id hash mirrors
/// <c>Hrot.Blueprints.Core.Compiler.BlueprintIdHash.Compute</c> — keep both in sync.
/// </para>
/// </summary>
internal sealed class GeneratedBlueprintSchema
{
    public string SanitizedName { get; }
    public int BlueprintId { get; }
    public bool IsAiPrimitive { get; }
    public IReadOnlyList<(string Name, string TypeId)> Parameters { get; }

    public GeneratedBlueprintSchema(
        string sanitizedName, int blueprintId, bool isAiPrimitive,
        IReadOnlyList<(string Name, string TypeId)> parameters)
    {
        SanitizedName = sanitizedName;
        BlueprintId   = blueprintId;
        IsAiPrimitive = isAiPrimitive;
        Parameters    = parameters;
    }

    /// <summary>The generated class name: "{SanitizedName}_{BlueprintId:X8}_Bp" (mirrors <c>AiPrimitiveEmitter.EmitClass</c>).</summary>
    public string GeneratedClassName => $"{SanitizedName}_{BlueprintId:X8}_Bp";
}

internal static class GeneratedBlueprintSchemaCatalog
{
    /// <summary>
    /// Parses every <c>*.bp.json</c> AdditionalText into a <see cref="GeneratedBlueprintSchema"/>.
    /// Malformed/unparseable files are silently skipped (never throws) — a broken <c>.bp.json</c> is
    /// already reported by <c>Hrot.Blueprints.Generators.BlueprintIncrementalGenerator</c> itself;
    /// this catalog only needs a best-effort read.
    /// </summary>
    public static IReadOnlyList<GeneratedBlueprintSchema> Parse(
        ImmutableArray<(string Path, string Text)> bpJsonFiles)
    {
        var result = new List<GeneratedBlueprintSchema>(bpJsonFiles.Length);
        foreach (var file in bpJsonFiles)
        {
            var schema = TryParseOne(file.Text);
            if (schema != null) result.Add(schema);
        }
        return result;
    }

    private static GeneratedBlueprintSchema? TryParseOne(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (!TryGetPropCI(root, "assetId", out var idProp)) return null;
            if (!Guid.TryParse(idProp.GetString(), out var assetId)) return null;

            string name = TryGetPropCI(root, "name", out var nameProp) ? (nameProp.GetString() ?? "") : "";
            string sanitizedName = SanitizeName(name);
            int blueprintId = ComputeBlueprintId(assetId);

            bool isAiPrimitive = false;
            if (TryGetPropCI(root, "dispatch", out var dispProp))
            {
                if (dispProp.ValueKind == JsonValueKind.Number && dispProp.TryGetInt32(out int dn))
                    isAiPrimitive = dn == 1; // BlueprintDispatchKind.AiPrimitive == 1 (Library=0, AiPrimitive=1, Instance=2)
                else if (dispProp.ValueKind == JsonValueKind.String)
                    isAiPrimitive = string.Equals(dispProp.GetString(), "AiPrimitive", StringComparison.OrdinalIgnoreCase);
            }

            var parameters = new List<(string Name, string TypeId)>();
            if (TryGetPropCI(root, "parameters", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in paramsProp.EnumerateArray())
                {
                    string pName = TryGetPropCI(item, "name", out var np) ? (np.GetString() ?? "") : "";
                    string pType = "System.Object";
                    if (TryGetPropCI(item, "type", out var tp) && TryGetPropCI(tp, "typeid", out var tid))
                        pType = tid.GetString() ?? "System.Object";
                    if (!string.IsNullOrEmpty(pName))
                        parameters.Add((pName, pType));
                }
            }

            return new GeneratedBlueprintSchema(sanitizedName, blueprintId, isAiPrimitive, parameters);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the composed byte size of a blueprint's generated <c>Params</c> struct from its
    /// <c>.bp.json</c> parameter schema — the Option A fallback wired into the BTree generator's
    /// size resolver when the Roslyn <c>StructSizeResolver</c> cannot see the not-yet-visible
    /// sibling-generated type.  Returns <c>null</c> when <paramref name="typeId"/> doesn't match the
    /// generated-Params naming convention, no matching blueprint schema is found, or any parameter's
    /// field type cannot itself be sized (the caller treats a <c>null</c> the same as any other
    /// unresolvable managed-variable type — BTREE0002, never a silent/partial emit).
    /// </summary>
    public static int? TryResolveParamsSize(
        string typeId,
        IReadOnlyList<GeneratedBlueprintSchema> schemas,
        Compilation compilation)
    {
        const string paramsSuffix = "+Params";
        if (string.IsNullOrEmpty(typeId) || !typeId.EndsWith(paramsSuffix, StringComparison.Ordinal))
            return null;

        string classFqn = typeId.Substring(0, typeId.Length - paramsSuffix.Length);
        if (!TryParseGeneratedClassRef(classFqn, out string sanitizedName, out int blueprintId))
            return null;

        var schema = Find(schemas, sanitizedName, blueprintId);
        if (schema == null)
            return null;

        // Compute each parameter field's managed size: known primitive/vector table first (mirrors
        // BTreeBlackboardPackHelper.Pack's own lookup order), then the Roslyn resolver (handles
        // project enum types authored as a blueprint Parameter type).
        var fieldSizes = new List<int>(schema.Parameters.Count);
        foreach (var param in schema.Parameters)
        {
            if (Hrot.AiEditor.Persistence.Emit.BTreeBlackboardPackHelper.TryGetSize(param.TypeId, out int known))
            {
                fieldSizes.Add(known);
                continue;
            }

            int? resolved = StructSizeResolver.ResolveFieldSize(param.TypeId, compilation);
            if (!resolved.HasValue)
                return null;
            fieldSizes.Add(resolved.Value);
        }

        return StructSizeResolver.ComputeSequentialSize(fieldSizes);
    }

    /// <summary>
    /// Tries to parse a generated-blueprint class reference of the form
    /// <c>"{Namespace}.{SanitizedName}_{BlueprintId:X8}_Bp"</c>, mirroring the class name
    /// <c>AiPrimitiveEmitter.EmitClass</c> emits (<c>"{asset.SanitizedName}_{asset.BlueprintId:X8}_Bp"</c>).
    /// The caller is responsible for stripping any trailing member/nested-type suffix
    /// (e.g. <c>".TickCore"</c> or <c>"+Params"</c>) before calling this.
    /// </summary>
    public static bool TryParseGeneratedClassRef(string classFqn, out string sanitizedName, out int blueprintId)
    {
        sanitizedName = "";
        blueprintId = 0;

        if (string.IsNullOrEmpty(classFqn)) return false;

        int lastDot = classFqn.LastIndexOf('.');
        string shortName = lastDot >= 0 ? classFqn.Substring(lastDot + 1) : classFqn;

        const string classSuffix = "_Bp";
        if (!shortName.EndsWith(classSuffix, StringComparison.Ordinal)) return false;
        string withoutSuffix = shortName.Substring(0, shortName.Length - classSuffix.Length);

        int underscoreIdx = withoutSuffix.LastIndexOf('_');
        if (underscoreIdx < 0) return false;

        string namePart = withoutSuffix.Substring(0, underscoreIdx);
        string idPart    = withoutSuffix.Substring(underscoreIdx + 1);
        if (idPart.Length != 8) return false;
        if (!int.TryParse(idPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int idValue))
            return false;
        if (string.IsNullOrEmpty(namePart)) return false;

        sanitizedName = namePart;
        blueprintId = idValue;
        return true;
    }

    /// <summary>Finds the schema matching a (SanitizedName, BlueprintId) pair, or <c>null</c>.</summary>
    public static GeneratedBlueprintSchema? Find(
        IReadOnlyList<GeneratedBlueprintSchema> schemas, string sanitizedName, int blueprintId)
    {
        foreach (var s in schemas)
        {
            if (s.BlueprintId == blueprintId &&
                string.Equals(s.SanitizedName, sanitizedName, StringComparison.Ordinal))
                return s;
        }
        return null;
    }

    // ── Mirrors of Hrot.Blueprints.Core.Compiler helpers (kept in sync manually — see class remarks) ──

    /// <summary>Mirrors <c>Hrot.Blueprints.Core.Compiler.Emit.Sanitizer.SanitizeName</c>.</summary>
    private static string SanitizeName(string name)
    {
        var sb = new System.Text.StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }
        return sb.Length > 0 ? sb.ToString() : "UnknownBlueprint";
    }

    /// <summary>Mirrors <c>Hrot.Blueprints.Core.Compiler.BlueprintIdHash.Compute</c> (FNV-1a 32-bit over <c>Guid.ToByteArray()</c>).</summary>
    private static int ComputeBlueprintId(Guid assetId)
    {
        const uint offsetBasis = 2166136261u;
        const uint fnvPrime    = 16777619u;

        byte[] bytes = assetId.ToByteArray();
        uint hash = offsetBasis;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= fnvPrime;
        }
        return unchecked((int)hash);
    }

    private static bool TryGetPropCI(JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
