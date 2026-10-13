using System.Reflection;
using System.Text.Json;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>One public field of a params DTO, in declaration order: what a blueprint's Parameter is made from.</summary>
/// <param name="Name">the field name — the flat params JSON key and the Parameter's name.</param>
/// <param name="TypeId">the field type's FQN (<c>Type.FullName</c> spelling, <c>+</c> for nesting) — a <see cref="BlueprintTypeRef.TypeId"/>.</param>
/// <param name="DefaultValueJson">the field's default as one JSON value, from <c>[ParamsDto(DefaultJson)]</c>; null when it has none.</param>
public readonly record struct ParamsDtoField(string Name, string TypeId, string? DefaultValueJson);

/// <summary>
/// ⭐ <c>CE-2135</c> P4 (R-2004 — <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10 P4-a) — where the compiler learns a named
/// params DTO's fields. The Roslyn generator cannot reflect over the assembly it is building, so it supplies one over its compilation
/// (<see cref="CompileOptions.ParamsDtoFields"/>); null there means <see cref="ReflectionParamsDtoFieldSource"/> (the editor / in-process
/// path) — the <see cref="IClrSignatureResolver"/> precedent.
/// </summary>
public interface IParamsDtoFieldSource
{
    /// <summary>The DTO's public instance fields in declaration order; false when the type is unknown to this source.</summary>
    bool TryGetFields(string typeId, out IReadOnlyList<ParamsDtoField> fields);
}

/// <summary>
/// The loaded-assembly field source. ⚠ The LAST loaded copy of a type wins (a rebuilt assembly loads after the old one — CE-3186 / R-2001).
/// The <c>[ParamsDto]</c> attribute is read by NAME through <see cref="CustomAttributeData"/>: this project cannot reference <c>Fdp.Core</c>
/// on its netstandard2.0 target.
/// </summary>
public sealed class ReflectionParamsDtoFieldSource : IParamsDtoFieldSource
{
    public static readonly ReflectionParamsDtoFieldSource Instance = new();

    internal const string ParamsDtoAttributeName = "Fdp.Core.ParamsDtoAttribute";   // twin: GeneratedBlueprintSchemaCatalog (Roslyn walk)
    internal const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";

    public bool TryGetFields(string typeId, out IReadOnlyList<ParamsDtoField> fields)
    {
        fields = Array.Empty<ParamsDtoField>();
        var type = Find(typeId);
        if (type is null) return false;

        string? defaultJson = null;
        foreach (var a in type.GetCustomAttributesData())
        {
            if (a.AttributeType.FullName != ParamsDtoAttributeName) continue;
            foreach (var n in a.NamedArguments)
                if (n.MemberName == "DefaultJson") defaultJson = n.TypedValue.Value as string;
        }

        var list = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.GetCustomAttributesData().Any(a => a.AttributeType.FullName == JsonIgnoreAttributeName))   // not in the JSON contract
            .OrderBy(f => f.MetadataToken)
            .Select(f => (f.Name, TypeId: f.FieldType.FullName ?? f.FieldType.Name))
            .ToList();
        fields = ParamsDtoFields.WithDefaults(list, defaultJson);
        return true;
    }

    private static Type? Find(string typeId)
    {
        // ⚠ A nested DTO is spelled as Type.FullName spells it (<c>Outer+Inner</c>) — the BlueprintTypeRef convention.
        string exact = ParamsDtoFields.Norm(typeId, keepNesting: true);
        var found = Scan(exact);
        if (found is not null) return found;
        // ⚠ Assemblies load lazily: a DTO's assembly may be referenced and not loaded yet (a test that compiled nothing from it).
        LoadReferencedClosure();
        return Scan(exact);
    }

    private static Type? Scan(string exact)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = assemblies.Length - 1; i >= 0; i--)                 // last loaded wins
        {
            try { if (assemblies[i].GetType(exact, throwOnError: false) is { } t) return t; }
            catch (Exception) { /* a broken dynamic assembly answers nothing */ }
        }
        return null;
    }

    private static int _closureLoaded;

    private static void LoadReferencedClosure()
    {
        if (System.Threading.Interlocked.Exchange(ref _closureLoaded, 1) == 1) return;
        var seen = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? ""));
        var queue = new Queue<Assembly>(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic));
        while (queue.Count > 0)
        {
            AssemblyName[] refs;
            try { refs = queue.Dequeue().GetReferencedAssemblies(); } catch (Exception) { continue; }
            foreach (var r in refs)
            {
                if (r.Name is null || !seen.Add(r.Name)) continue;
                try { queue.Enqueue(Assembly.Load(r)); } catch (Exception) { /* not deployed here */ }
            }
        }
    }
}

/// <summary>
/// ⭐ <c>CE-2135</c> P4 (R-2004 — §13.10 P4-b) — no blueprint DECLARES Parameters: an asset names a params DTO
/// (<see cref="BlueprintAsset.ParamsDtoTypeId"/>) and the compiler makes its Parameters from the DTO's fields, one per field, in order,
/// with deterministic ids. Everything downstream of the Parameter view (the <c>Params</c> struct, the flat <c>ParseParams</c>, Get All
/// Parameters' pins, <c>IrOp_ReadParam</c>, the AiPrimitive hosts' <c>_Bp+Params</c> binding) keeps reading Parameters — now the DTO's.
/// <para>⛔ An asset that names a DTO AND declares Parameters is <c>BP1689</c>: two declarations of one contract.</para>
/// </summary>
public static class ParamsDtoFields
{
    /// <summary>
    /// Fills <paramref name="asset"/>'s Parameters from its named DTO. ⚠ Call on the compiler's OWN copy (never the caller's asset —
    /// a saved file must not gain Parameters). Returns false (with a <c>BP1689</c>) when the DTO cannot be read.
    /// </summary>
    public static bool Materialize(BlueprintAsset asset, IParamsDtoFieldSource? source, Action<Diagnostic> report)
    {
        string? named = asset.ParamsDtoTypeId?.Trim();
        if (string.IsNullOrEmpty(named)) return true;
        // ⛔ Names a DTO AND declares Parameters: V_ParamsDtoRules reports BP1689 (one home for the message — Validate() reaches it
        //   without coming through here). Nothing is made, so the declared ones are not silently doubled.
        if (asset.ParametersFromParamsDto || asset.Declarations.CountIn(DeclarationKind.Parameter) > 0) return true;
        // ⛔ A kind with no params (an Instance, a plain Library) gets nothing made: V_ParamsDtoRules reports it as BP1689.
        if (!HasParams(asset)) return true;
        if (!(source ?? ReflectionParamsDtoFieldSource.Instance).TryGetFields(named!, out var fields))
        {
            report(Diagnostic.Error(DiagnosticCodes.BP1689,
                $"'{asset.Name}' names params DTO '{named}', which is not a known type — its fields are the blueprint's Parameters (R-2004).",
                asset.AssetId, null, null));
            return false;
        }

        var made = fields.Select(f => BlueprintDeclaration.For(new ParameterDecl
        {
            Id               = ParameterId(asset.AssetId, f.Name),
            Name             = f.Name,
            Type             = new BlueprintTypeRef { TypeId = f.TypeId },
            DefaultValueJson = f.DefaultValueJson,
        })).ToList();
        asset.DeclarationStore.InsertRange(0, made);   // Parameters are the store's first run (DeclarationList.KindOrder)
        asset.ParameterOrder = null;                   // the DTO's field order is the order
        asset.ParametersFromParamsDto = true;
        return true;
    }

    /// <summary>True for the kinds that take params JSON — and so may name a params DTO: a behaviour, an AiPrimitive, a resolver
    /// (a Library with a <c>ResolverSubject</c>). ⭐ One predicate for the materializer and <c>V_ParamsDtoRules</c>.</summary>
    public static bool HasParams(BlueprintAsset asset)
        => asset.Dispatch is BlueprintDispatchKind.Behavior or BlueprintDispatchKind.AiPrimitive
        || (asset.Dispatch == BlueprintDispatchKind.Library && asset.ResolverSubject is not null);

    /// <summary>The id of the Parameter made from DTO field <paramref name="fieldName"/> — the same on every compile, in every process, so
    /// a node that references a Parameter by id (<c>GetVariable</c>, a task's <c>ParamsVariable</c>) keeps resolving.</summary>
    public static Guid ParameterId(Guid assetId, string fieldName) => DeterministicIds.FromString($"{assetId:N}/params/{fieldName}");

    /// <summary>
    /// The asset's Parameters as (name, type id) WITHOUT changing it: the declared ones, else its named DTO's fields. ⭐ The editor's
    /// Get All Parameters pins read this (<c>NodePinSchema</c>) — the twin of the compiler's materialized view, so the canvas shows the
    /// pins the build will have. Empty when the DTO is unknown (the compile reports it).
    /// </summary>
    public static IReadOnlyList<ParamsDtoField> ParametersOf(BlueprintAsset asset, IParamsDtoFieldSource? source = null)
    {
        string? named = asset.ParamsDtoTypeId?.Trim();
        if (string.IsNullOrEmpty(named) || asset.Declarations.CountIn(DeclarationKind.Parameter) > 0)
            return asset.Declarations.Of(DeclarationKind.Parameter)
                .Select(p => new ParamsDtoField(p.Name, string.IsNullOrEmpty(p.Type?.TypeId) ? "System.Object" : p.Type!.TypeId, p.DefaultValueJson))
                .ToList();
        return (source ?? ReflectionParamsDtoFieldSource.Instance).TryGetFields(named!, out var fields) ? fields : Array.Empty<ParamsDtoField>();
    }

    /// <summary>Pairs each field with its value in the DTO's <c>DefaultJson</c> object (case-insensitive, as the params JSON reads).</summary>
    public static IReadOnlyList<ParamsDtoField> WithDefaults(IEnumerable<(string Name, string TypeId)> fields, string? defaultJson)
    {
        Dictionary<string, string>? defaults = null;
        if (!string.IsNullOrWhiteSpace(defaultJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(defaultJson!);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in doc.RootElement.EnumerateObject()) defaults[p.Name] = p.Value.GetRawText();
                }
            }
            catch (JsonException) { /* a malformed default is the DTO's own rail's business (CE-3185), not the compile's */ }
        }
        return fields.Select(f => new ParamsDtoField(f.Name, Norm(f.TypeId, keepNesting: true),
            defaults is not null && defaults.TryGetValue(f.Name, out var v) ? v : null)).ToList();
    }

    /// <summary>The comparable spelling of a type id: no <c>global::</c>, and (unless <paramref name="keepNesting"/>) <c>.</c> for nesting.</summary>
    internal static string Norm(string typeId, bool keepNesting = false)
    {
        var s = typeId.Trim();
        if (s.StartsWith("global::", StringComparison.Ordinal)) s = s.Substring("global::".Length);
        return keepNesting ? s : s.Replace('+', '.');
    }
}
