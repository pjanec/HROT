using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Generators;
using Hrot.Blueprints.Core.Compiler;
using Microsoft.CodeAnalysis;

namespace Hrot.Blueprints.Generators;

/// <summary>
/// ⭐ <c>CE-2135</c> P4 (R-2004 — <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10 P4-a) — the generator's
/// <see cref="IParamsDtoFieldSource"/>: a named params DTO's public fields from the semantic model (a hand-written DTO, in this
/// assembly or a referenced one), else from a sibling <c>.params.json</c> through the shared <see cref="GeneratedTypeCatalog"/>
/// (its generator's output is invisible to this compilation — the <see cref="RoslynClrSignatureResolver"/> / CE-2026 precedent).
/// <para>⭐ The field walk is <see cref="GeneratedBlueprintSchemaCatalog.ParamsDtoFieldsOf"/> — the one the AiPrimitive hosts size a
/// sibling's <c>Params</c> with — and its type ids use the <see cref="System.Type.FullName"/> spelling, matching
/// <see cref="ReflectionParamsDtoFieldSource"/>: the editor's compile and the build's cannot disagree on a Parameter's type.</para>
/// </summary>
internal sealed class RoslynParamsDtoFieldSource : IParamsDtoFieldSource
{
    private readonly Compilation _compilation;
    private readonly GeneratedTypeCatalog _generated;

    public RoslynParamsDtoFieldSource(Compilation compilation, GeneratedTypeCatalog generated)
    { _compilation = compilation; _generated = generated; }

    public bool TryGetFields(string typeId, out IReadOnlyList<ParamsDtoField> fields)
    {
        string id = typeId.Trim();
        if (id.StartsWith("global::", System.StringComparison.Ordinal)) id = id.Substring("global::".Length);

        if (_compilation.GetTypeByMetadataName(id) is { } type)
        {
            fields = ParamsDtoFields.Normalized(GeneratedBlueprintSchemaCatalog.ParamsDtoFieldsOf(type));
            return true;
        }

        if (_generated.ParamsDtos.TryGetValue(id, out var asset))
        {
            fields = ParamsDtoFields.Normalized(
                (asset.Fields ?? new List<Hrot.AiEditor.Persistence.Params.ParamsDtoFieldDto>()).Select(f => (f.Name, f.TypeId)));
            return true;
        }

        fields = System.Array.Empty<ParamsDtoField>();
        return false;
    }
}
