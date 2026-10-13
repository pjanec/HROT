using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
    /// <summary>⭐ <c>CE-384</c> — the asset's own GUID. The catalog already parsed it to derive
    /// <see cref="BlueprintId"/>; carrying it lets a consumer match by the handle an ASSET can
    /// author, instead of re-implementing <c>ComputeBlueprintId</c> at the call site.</summary>
    public Guid AssetId { get; }

    public string SanitizedName { get; }
    public int BlueprintId { get; }
    public bool IsAiPrimitive { get; }
    public IReadOnlyList<(string Name, string TypeId)> Parameters { get; }

    /// <summary>⭐ <c>CE-2135</c> P4 (R-2004) — the asset's named params DTO, or null. Named, the asset declares no Parameters: its
    /// generated <c>Params</c> mirrors the DTO's fields (<see cref="GeneratedBlueprintSchemaCatalog.ParamsDtoFieldsOf"/>).</summary>
    public string? ParamsDtoTypeId { get; }

    public GeneratedBlueprintSchema(
        Guid assetId, string sanitizedName, int blueprintId, bool isAiPrimitive,
        IReadOnlyList<(string Name, string TypeId)> parameters, string? paramsDtoTypeId = null)
    {
        AssetId        = assetId;
        SanitizedName = sanitizedName;
        BlueprintId   = blueprintId;
        IsAiPrimitive = isAiPrimitive;
        Parameters    = parameters;
        ParamsDtoTypeId = paramsDtoTypeId;
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

            // ⭐⭐ U-10 step 3 — v2 stores declarations as ONE tagged array, so the parameters live
            //   under `Declarations` filtered by Kind == "Parameter". v1's `parameters` is still read
            //   so a not-yet-migrated file keeps working.
            //
            // ⛔⛔ This catalog is a SECOND, independent parser of *.bp.json — it does not go through
            //   BlueprintJsonServices. When the corpus moved to v2 it did not fail: it returned a
            //   schema with ZERO parameters, so StructSizeResolver sized the Params struct wrong and
            //   a composed BTree wrote shared state at the wrong slot count. ⚠ "Silently skipped"
            //   understates what the class does — it silently returns a WRONG schema. See BP-242.
            var parameters = new List<(string Name, string TypeId)>();
            if (TryGetPropCI(root, Hrot.Blueprints.Core.BlueprintSchemaV2.DeclarationsProperty,
                             out var declProp) && declProp.ValueKind == JsonValueKind.Array)
            {
                var parameterTag = Hrot.Blueprints.Core.BlueprintSchemaV2.DeclarationTags[0];
                foreach (var item in declProp.EnumerateArray())
                {
                    if (!TryGetPropCI(item, Hrot.Blueprints.Core.BlueprintSchemaV2.KindProperty, out var kp))
                        continue;
                    if (!string.Equals(kp.GetString(), parameterTag, StringComparison.Ordinal)) continue;
                    AddParameter(item, parameters);
                }
            }
            else if (TryGetPropCI(root, "parameters", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in paramsProp.EnumerateArray())
                    AddParameter(item, parameters);
            }

            string? paramsDto = TryGetPropCI(root, "paramsDtoTypeId", out var dtoProp) && dtoProp.ValueKind == JsonValueKind.String
                ? dtoProp.GetString()?.Trim() : null;
            return new GeneratedBlueprintSchema(assetId, sanitizedName, blueprintId, isAiPrimitive, parameters,
                string.IsNullOrEmpty(paramsDto) ? null : paramsDto);
        }
        catch
        {
            return null;
        }
    }

    private static void AddParameter(JsonElement item, List<(string Name, string TypeId)> into)
    {
        string pName = TryGetPropCI(item, "name", out var np) ? (np.GetString() ?? "") : "";
        string pType = "System.Object";
        if (TryGetPropCI(item, "type", out var tp) && TryGetPropCI(tp, "typeid", out var tid))
            pType = tid.GetString() ?? "System.Object";
        if (!string.IsNullOrEmpty(pName)) into.Add((pName, pType));
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

        // ⭐ CE-2135 P4 (R-2004) — a named params DTO's fields ARE the Parameters (the blueprint compiler's ParamsDtoFields): read
        //   them from the DTO's symbol, the same walk the generator's field source uses, so the host sizes the Params the build emits.
        IEnumerable<(string Name, string TypeId)> parameters = schema.Parameters;
        if (schema.Parameters.Count == 0 && schema.ParamsDtoTypeId is { } dtoId)
        {
            if (compilation.GetTypeByMetadataName(dtoId) is not { } dto) return null;   // unknown here ⇒ loud (BTREE0002), never a guess
            parameters = ParamsDtoFieldsOf(dto).Select(f => (f.Name, f.TypeId));
        }

        // Each parameter field's managed (size, alignment) — the known-type table first, then the Roslyn resolver (handles
        // project enum and struct types authored as a blueprint Parameter type). ⭐ CE-2027: the ALIGNMENT is the field's own
        // (a Vector3 is 4-aligned), not min(size, 8) — the Params struct is a Sequential C# struct the CLR lays out.
        var fields = new List<(int Size, int Align)>();
        foreach (var param in parameters)
        {
            var layout = StructSizeResolver.ResolveFieldLayout(param.TypeId, compilation);
            if (!layout.HasValue)
                return null;
            fields.Add(layout.Value);
        }

        return global::Fdp.Toolkit.Behavior.Shared.RoslynStructLayout.SequentialSize(fields);
    }

    /// <summary>The <c>[ParamsDto]</c> attribute, by name (this project cannot reference <c>Fdp.Core</c>).</summary>
    public const string ParamsDtoAttributeName = "Fdp.Core.ParamsDtoAttribute";
    private const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";

    /// <summary>
    /// ⭐ <c>CE-2135</c> P4 (R-2004) — a params DTO's public instance fields in declaration order, minus <c>[JsonIgnore]</c> ones (not in
    /// the JSON contract), each with its <see cref="System.Type.FullName"/>-spelled type id: what the blueprint compiler makes the
    /// Parameters from. ⭐ The ONE Roslyn walk — the generator's field source and the host size path both read it; its reflection twin
    /// is <c>ReflectionParamsDtoFieldSource</c> (same filter, same spelling).
    /// </summary>
    public static IReadOnlyList<(string Name, string TypeId)> ParamsDtoFieldsOf(INamedTypeSymbol dto)
        => dto.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Public && !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared)
            .Where(f => !f.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == JsonIgnoreAttributeName))
            .Select(f => (f.Name, FullNameOf(f.Type)))
            .ToList();

    /// <summary>The <c>[ParamsDto(DefaultJson)]</c> on <paramref name="dto"/>, or null.</summary>
    public static string? ParamsDtoDefaultJsonOf(INamedTypeSymbol dto)
    {
        foreach (var a in dto.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != ParamsDtoAttributeName) continue;
            foreach (var n in a.NamedArguments)
                if (n.Key == "DefaultJson") return n.Value.Value as string;
        }
        return null;
    }

    /// <summary><see cref="System.Type.FullName"/> of a type symbol: namespace-qualified, <c>+</c> for nesting, no keyword aliases.</summary>
    public static string FullNameOf(ITypeSymbol t)
    {
        if (t is IArrayTypeSymbol arr) return FullNameOf(arr.ElementType) + "[]";
        if (t is INamedTypeSymbol { ContainingType: { } outer }) return FullNameOf(outer) + "+" + t.MetadataName;
        var ns = t.ContainingNamespace;
        return (ns is null || ns.IsGlobalNamespace ? "" : ns.ToDisplayString() + ".") + t.MetadataName;
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

    /// <summary>⭐ <c>CE-417</c> B-1 — the schema of the blueprint asset with this id, or <c>null</c>. ⭐ By id, not name: two
    /// blueprints may share a name (<c>EnumDemo</c> exists twice), never an id.</summary>
    public static GeneratedBlueprintSchema? FindByAssetId(
        IReadOnlyList<GeneratedBlueprintSchema> schemas, Guid assetId)
    {
        foreach (var s in schemas)
            if (s.AssetId == assetId) return s;
        return null;
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

    // ⭐ CE-428: the two compiler mirrors live in ONE place now — Persistence's BlueprintClassNaming.
    private static string SanitizeName(string name)
        => global::Hrot.AiEditor.Persistence.Emit.BlueprintClassNaming.SanitizeName(name);

    private static int ComputeBlueprintId(Guid assetId)
        => global::Hrot.AiEditor.Persistence.Emit.BlueprintClassNaming.ComputeBlueprintId(assetId);

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
