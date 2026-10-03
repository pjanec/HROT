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

        return Collect(".bp.json").Combine(Collect(".btree.json")).Combine(Collect(".hsm.json"))
            .Select(static (files, _) => Parse(files.Left.Left, files.Left.Right, files.Right));
    }

    /// <summary>Parses every sibling asset. Unparseable files are skipped — their own generator run reports them.</summary>
    public static GeneratedTypeCatalog Parse(
        ImmutableArray<(string Path, string Text)> bpJsonFiles,
        ImmutableArray<(string Path, string Text)> btreeJsonFiles,
        ImmutableArray<(string Path, string Text)> hsmJsonFiles)
    {
        var behaviors = new List<BehaviorInputs>();
        foreach (var (_, text) in btreeJsonFiles)
        {
            BehaviorTreeAssetDto? dto;
            try { dto = BTreeJsonServices.Deserialize(text); }
            catch { continue; }
            if (dto != null) Add(dto, behaviors);
        }
        foreach (var (_, text) in hsmJsonFiles)
        {
            HsmAssetDto? dto;
            try { dto = HsmJsonServices.Deserialize(text); }
            catch { continue; }
            if (dto != null) Add(HsmBridgeEmitCore.BlackboardOwner(dto), behaviors);
        }
        return new GeneratedTypeCatalog(GeneratedBlueprintSchemaCatalog.Parse(bpJsonFiles), behaviors);
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
            return roslyn(typeId)
                ?? GeneratedBlueprintSchemaCatalog.TryResolveParamsSize(id, Blueprints, compilation)
                ?? TryResolveInputsSize(id, self!, visiting);
        };
        return self;
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
