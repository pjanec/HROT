using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-2026</c> (S8c) — the ONE generation-time answer to <i>"what is this sibling-generated type?"</i>, for every
/// generator.</b> 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8c design".
///
/// <para>
/// 🔴 Three source generators run on one compilation (<c>Hrot.AI.Behaviors</c>) and none sees another's output. ⇒ a host
/// variable typed as a SIBLING's generated struct — a blueprint's <c>{Name}_{Id:X8}_Bp+Params</c>, a BTree's or an HSM's Inputs
/// struct — is invisible to Roslyn here: <c>Pack</c> cannot size it and <c>TypeExists</c> says it is not there. This catalogue
/// answers both from the sibling asset files, with the SAME naming rule (<see cref="BTreeEmitCore.InputsStructTypeId"/>) and the
/// SAME packing (<see cref="BTreeBlackboardPackHelper.Pack"/>) the child's own generator emits with, so they cannot disagree.
/// </para>
///
/// <para>
/// ⭐ It replaces <c>GeneratedBehaviorSchemaCatalog</c> (<c>CE-439</c>, BTree only) and the size chain the BTree and HSM generators
/// each spelled out; it adds the HSM tier (read through <see cref="HsmBridgeEmitCore.BlackboardOwner"/>, the view the HSM
/// generator packs its own struct with); and it is SHARED BY LINK into <c>Hrot.Blueprints.Generators</c> (the
/// <c>StructSizeResolver</c> precedent), whose size oracle and <c>TypeExists</c> were Roslyn-only.
/// </para>
/// </summary>
internal sealed class GeneratedTypeCatalog
{
    /// <summary>One sibling behaviour (BTree or HSM) that publishes an Inputs struct: its type id and its own variables.</summary>
    internal sealed class BehaviorInputs   // ⚠ a class, not a record: netstandard2.0 (no IsExternalInit)
    {
        public BehaviorInputs(string inputsTypeId, IReadOnlyList<BlackboardVariableDto> variables)
        { InputsTypeId = inputsTypeId; Variables = variables; }
        public string InputsTypeId { get; }
        public IReadOnlyList<BlackboardVariableDto> Variables { get; }
    }

    private GeneratedTypeCatalog(IReadOnlyList<GeneratedBlueprintSchema> blueprints, IReadOnlyList<BehaviorInputs> behaviors)
    { Blueprints = blueprints; Behaviors = behaviors; }

    /// <summary>Every <c>*.bp.json</c> sibling (class identity + <c>Params</c>).</summary>
    public IReadOnlyList<GeneratedBlueprintSchema> Blueprints { get; }

    /// <summary>Every <c>*.btree.json</c> / <c>*.hsm.json</c> sibling that publishes an Inputs struct.</summary>
    public IReadOnlyList<BehaviorInputs> Behaviors { get; }

    /// <summary>⭐ <c>CE-2136</c> — every <c>*.params.json</c> sibling (a params DTO asset), by its generated type id.</summary>
    public IReadOnlyDictionary<string, Hrot.AiEditor.Persistence.Params.ParamsDtoAssetDto> ParamsDtos { get; private set; }
        = new Dictionary<string, Hrot.AiEditor.Persistence.Params.ParamsDtoAssetDto>(StringComparer.Ordinal);

    /// <summary>
    /// ⭐ The ONE wiring every generator uses: every <c>*.bp.json</c>, <c>*.btree.json</c> and <c>*.hsm.json</c> of the compilation,
    /// parsed once per change (not once per asset).
    /// </summary>
    public static IncrementalValueProvider<GeneratedTypeCatalog> Provider(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<(string Path, string Text)>> Collect(string extension) =>
            context.AdditionalTextsProvider
                .Where(at => at.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) => (at.Path, at.GetText(ct)?.ToString() ?? string.Empty))
                .Collect();

        return Collect(".bp.json").Combine(Collect(".btree.json")).Combine(Collect(".hsm.json")).Combine(Collect(".params.json"))
            .Select(static (files, _) => Parse(files.Left.Left.Left, files.Left.Left.Right, files.Left.Right, files.Right));
    }

    /// <summary>Parses every sibling asset. Unparseable files are skipped — their own generator run reports them.</summary>
    public static GeneratedTypeCatalog Parse(
        ImmutableArray<(string Path, string Text)> bpJsonFiles,
        ImmutableArray<(string Path, string Text)> btreeJsonFiles,
        ImmutableArray<(string Path, string Text)> hsmJsonFiles)
        => Parse(bpJsonFiles, btreeJsonFiles, hsmJsonFiles, ImmutableArray<(string Path, string Text)>.Empty);

    /// <summary>⭐ <c>CE-2136</c> — <see cref="Parse(ImmutableArray{ValueTuple{string,string}},ImmutableArray{ValueTuple{string,string}},ImmutableArray{ValueTuple{string,string}})"/>
    /// plus the params DTO assets.</summary>
    public static GeneratedTypeCatalog Parse(
        ImmutableArray<(string Path, string Text)> bpJsonFiles,
        ImmutableArray<(string Path, string Text)> btreeJsonFiles,
        ImmutableArray<(string Path, string Text)> hsmJsonFiles,
        ImmutableArray<(string Path, string Text)> paramsJsonFiles)
    {
        var behaviors = new List<BehaviorInputs>();
        foreach (var (_, text) in btreeJsonFiles)
        {
            BehaviorTreeAssetDto? dto;
            try { dto = BTreeJsonServices.Deserialize(text); }
            catch { continue; }
            if (dto != null) Add(dto.WithParamsVariable(), behaviors);   // CE-2135: the reserved Params
        }
        foreach (var (_, text) in hsmJsonFiles)
        {
            HsmAssetDto? dto;
            try { dto = HsmJsonServices.Deserialize(text); }
            catch { continue; }
            if (dto != null) Add(HsmBridgeEmitCore.BlackboardOwner(dto), behaviors);
        }
        var paramsDtos = new Dictionary<string, Hrot.AiEditor.Persistence.Params.ParamsDtoAssetDto>(StringComparer.Ordinal);
        foreach (var (_, text) in paramsJsonFiles)
        {
            Hrot.AiEditor.Persistence.Params.ParamsDtoAssetDto dto;
            try { dto = Hrot.AiEditor.Persistence.Params.ParamsDtoJson.Deserialize(text); }
            catch { continue; }   // its own generator reports it
            if (Hrot.AiEditor.Persistence.Params.ParamsDtoEmitCore.Validate(dto).Count == 0)
                paramsDtos[Hrot.AiEditor.Persistence.Params.ParamsDtoEmitCore.TypeIdOf(dto)] = dto;
        }
        return new GeneratedTypeCatalog(GeneratedBlueprintSchemaCatalog.Parse(bpJsonFiles), behaviors) { ParamsDtos = paramsDtos };
    }

    private static void Add(BehaviorTreeAssetDto owner, List<BehaviorInputs> into)
    {
        string? typeId = BTreeEmitCore.InputsStructTypeId(owner);
        if (typeId != null) into.Add(new BehaviorInputs(typeId, owner.Blackboard.Variables));
    }

    /// <summary>
    /// True when <paramref name="typeId"/> names a type a SIBLING generator emits in this run: a behaviour's Inputs struct, or a
    /// generated blueprint class (or a type nested in it, e.g. <c>+Params</c>). ⚠ Accepts the <c>global::</c> form.
    /// </summary>
    public bool Declares(string typeId)
    {
        string id = Strip(typeId);
        if (id.Length == 0) return false;
        foreach (var b in Behaviors)
            if (string.Equals(b.InputsTypeId, id, StringComparison.Ordinal)) return true;
        if (ParamsDtos.ContainsKey(id)) return true;   // ⭐ CE-2136
        int nested = id.IndexOf('+');
        string classFqn = nested >= 0 ? id.Substring(0, nested) : id;
        return GeneratedBlueprintSchemaCatalog.TryParseGeneratedClassRef(classFqn, out string name, out int blueprintId)
            && GeneratedBlueprintSchemaCatalog.Find(Blueprints, name, blueprintId) != null;
    }

    /// <summary>
    /// The ONE size resolver every generator injects: Roslyn first (a type that already exists), then a sibling blueprint's
    /// <c>Params</c>, then a sibling behaviour's Inputs struct (packed recursively — a child may bind a grandchild's Inputs).
    /// <paramref name="fieldSizes"/> picks <see cref="StructSizeResolver.MakeFieldSizeDelegate"/> (enums too, <c>global::</c>
    /// accepted — the blueprint compiler's oracle) over <see cref="StructSizeResolver.MakeDelegate"/> (structs — the packer's).
    /// ⚠ One resolver serves one generator run: it carries the cycle guard (an A-hosts-B-hosts-A layout has no size).
    /// </summary>
    public Func<string, int?> SizeResolver(Compilation compilation, bool fieldSizes = false)
    {
        Func<string, int?> roslyn = fieldSizes
            ? StructSizeResolver.MakeFieldSizeDelegate(compilation)
            : StructSizeResolver.MakeDelegate(compilation);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        Func<string, int?>? self = null;
        self = typeId =>
        {
            string id = Strip(typeId);
            int? known = roslyn(typeId)
                ?? GeneratedBlueprintSchemaCatalog.TryResolveParamsSize(id, Blueprints, compilation)
                ?? TryResolveInputsSize(id, self!, visiting);
            if (known != null) return known;
            TryLayoutParamsDto(id, compilation, out _, out int dtoSize);   // ⭐ CE-2136 — a params DTO asset's struct
            return dtoSize >= 0 ? dtoSize : (int?)null;
        };
        return self;
    }

    /// <summary>
    /// ⭐ <c>CE-3182</c> — the field layouts a binding path (<c>Variable.Field</c>) walks, beside <see cref="SizeResolver"/>: Roslyn's
    /// CLR layout of a struct that already exists. ⛔ A sibling's GENERATED struct (an Inputs struct, a blueprint's <c>Params</c>)
    /// answers null — its fields are laid out in this same pass — so a path into it fails closed with a diagnostic.
    /// </summary>
    public Hrot.AiEditor.Persistence.Emit.FieldLayoutResolver FieldResolver(Compilation compilation)
        => (structTypeId, field) =>
        {
            if (StructSizeResolver.ResolveFieldOf(structTypeId, field, compilation) is { } f)
                return new Hrot.AiEditor.Persistence.Emit.FieldLayout(f.Offset, f.Size, f.TypeId);
            // ⭐ CE-2136 — a field of a params DTO asset's struct, laid out from its JSON (it is generated in this same pass).
            TryLayoutParamsDto(Strip(structTypeId), compilation, out var fields, out _);
            if (fields != null)
                foreach (var lf in fields)
                    if (string.Equals(lf.Name, field, StringComparison.Ordinal))
                        return new Hrot.AiEditor.Persistence.Emit.FieldLayout(lf.Offset, lf.Size, lf.TypeId);
            return null;
        };

    /// <summary>⭐ <c>CE-2136</c> — a params DTO asset's CLR layout (sequential), its field types laid out by Roslyn / the known-type
    /// table; null (and <paramref name="size"/> −1) when <paramref name="typeId"/> is no such asset or a field cannot be laid out.
    /// Returns null always, so it chains after <c>??</c> for the size and hands the size back through the out value.</summary>
    private int? TryLayoutParamsDto(string typeId, Compilation compilation,
        out IReadOnlyList<Hrot.AiEditor.Persistence.Params.ParamsDtoEmitCore.LaidOutField>? fields, out int size)
    {
        fields = null; size = -1;
        if (!ParamsDtos.TryGetValue(typeId, out var dto)) return null;
        if (Hrot.AiEditor.Persistence.Params.ParamsDtoEmitCore.TryLayout(dto, t => StructSizeResolver.ResolveFieldLayout(Strip(t), compilation),
                out var laid, out int bytes))
        { fields = laid; size = bytes; }
        return null;
    }

    private int? TryResolveInputsSize(string typeId, Func<string, int?> resolver, HashSet<string> visiting)
    {
        foreach (var b in Behaviors)
        {
            if (!string.Equals(b.InputsTypeId, typeId, StringComparison.Ordinal)) continue;
            if (!visiting.Add(typeId)) return null;
            try
            {
                BTreeBlackboardPackHelper.Pack(b.Variables, t => resolver(t), out int bytes);
                return bytes;
            }
            catch (NotSupportedException) { return null; }
            finally { visiting.Remove(typeId); }
        }
        return null;
    }

    private static string Strip(string? typeId)
        => typeId == null ? string.Empty
         : typeId.StartsWith("global::", StringComparison.Ordinal) ? typeId.Substring("global::".Length) : typeId;
}
