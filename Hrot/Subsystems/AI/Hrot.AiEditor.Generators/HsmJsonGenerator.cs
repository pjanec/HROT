using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Roslyn IncrementalGenerator that consumes <c>*.hsm.json</c> AdditionalTexts
/// and emits <c>CreateBuilder()</c> + <c>[HsmDefinition]</c> thunk (NO <c>[HsmLayout]</c>)
/// to <c>obj/GeneratedFiles/{Name}.g.cs</c>.
///
/// Design §6.2 (PU-202): JSON-owned assets generate topology core only; layout lives in JSON.
/// Per-asset deserialization failure → Roslyn diagnostic (never throws, never fails siblings).
/// Mirrors <see cref="Hrot.Blueprints.Generators.BlueprintIncrementalGenerator"/> control flow.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HsmJsonGenerator : IIncrementalGenerator
{
    /// <summary>Diagnostic code for HSM JSON parse/emit errors.</summary>
    public const string DiagnosticId = "HSM0001";

    /// <summary>⭐ CE-423 — a <c>Role=State</c> variable that would get no storage (<c>StateVariableStorage</c>).</summary>
    public const string StateStorageErrorId = "HSM0002";
    /// <summary>⭐ CE-417 — a C# [SharedAi*] binding whose variable is not the method's <c>ref</c> type.</summary>
    public const string SharedAiTypeErrorId = "HSM0003";

    /// <summary>⭐ <c>CE-506</c> — a global transition binds a BLUEPRINT guard/action: it has no source state, so there is no
    /// (state, site) the blueprint's params could be seeded from (§28.6c). Methods only.</summary>
    public const string GlobalBlueprintErrorId = "HSM0004";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider: raw file text from *.hsm.json AdditionalTexts
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(static at => at.Path.EndsWith(".hsm.json",
                    System.StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) =>
                {
                    string text = at.GetText(ct)?.ToString() ?? string.Empty;
                    return (at.Path, text);
                });

        // ⭐ BP-281: combine with the full compilation so the bridge can resolve struct-DTO sizes for a
        //   managed blackboard — the SAME seam BTreeJsonGenerator uses (StructSizeResolver).
        //   ⛔ Without it a struct-typed HSM input variable would be unsizeable and the params supply
        //   would silently not be emitted: exactly the "caller HAS the dependency and does not pass it"
        //   shape this codebase files as a defect.
        //   Incrementality note: as on the BTree side, this makes GenerateOneAsset re-run on ANY
        //   compilation change. Acceptable for the small *.hsm.json asset set (VE-DEBT-003).
        // ⭐⭐⭐ CE-384 — the *.bp.json AdditionalTexts, so a state's activity or a transition's guard
        //   can BE a blueprint. 🔴 This is the ONLY way to learn a blueprint's BlueprintId from here:
        //   the Blueprint generator and this one are SIBLING Roslyn generators driven from the same
        //   PRE-generation compilation, so the generated {Name}_{BlueprintId:X8}_Bp class is
        //   unresolvable by symbol "not even in a fully successful real build"
        //   (GeneratedBlueprintSchemaCatalog's own header). ⭐ Same providers the BTree generator
        //   already collects for the AiPrimitiveTickCore composition path.
        //   ⭐ CE-2026 (S8c) — every sibling asset (*.bp.json, *.btree.json, *.hsm.json) as ONE catalogue, the wiring
        //   all three generators share (DESIGN_Unified_Behaviour_Run "S8c design").
        IncrementalValueProvider<GeneratedTypeCatalog> siblings = GeneratedTypeCatalog.Provider(context);

        IncrementalValuesProvider<(string Path, string Text, Compilation Compilation, GeneratedTypeCatalog Siblings)> combined =
            rawFiles.Combine(context.CompilationProvider)
                    .Combine(siblings)
                    .Select(static (pair, _) =>
                        (pair.Left.Left.Path, pair.Left.Left.Text, pair.Left.Right, pair.Right));

        // Per-asset: deserialize → emit topology core → register source output
        context.RegisterSourceOutput(combined, static (spc, item) =>
        {
            GenerateOneAsset(spc, item.Path, item.Text, item.Compilation, item.Siblings);
        });
    }

    private static void GenerateOneAsset(
        SourceProductionContext spc, string path, string text, Compilation compilation,
        GeneratedTypeCatalog siblings)
    {
        // Deserialize — failure becomes a diagnostic, never throws, never fails siblings.
        HsmAssetDto? dto;
        try
        {
            dto = HsmJsonServices.Deserialize(text);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during deserialization: " + ex.Message));
            return;
        }

        if (dto is null)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Deserialization returned null (empty or invalid JSON)."));
            return;
        }

        // ⭐⭐ CE-423 — a State variable that would get NO storage is an ERROR, not a silent skip.
        foreach (var unstored in Hrot.AiEditor.Persistence.Emit.StateVariableStorage.UnstoredStateVariables(dto))
            spc.ReportDiagnostic(MakeStateStorageDiagnostic(path, unstored));

        // BP-281 / E7b: the Roslyn-backed struct-size resolver, built once and used by BOTH
        // emitters — the topology core bakes expression-target offsets into action keys and the
        // bridge writes ParseParams at the same offsets, so they must resolve sizes identically.
        // Only built when there is a managed blackboard to size; otherwise null, and the emitted
        // output is byte-identical to before.
        // ⭐⭐ CE-384 — Guid → the ushort the blueprint's generated thunk registers under.
        //   ⛔ Returns null for an unknown asset id rather than 0: a 0 would be a VALID action id and
        //   would silently mis-dispatch, which is the failure mode this whole id story exists to
        //   avoid. The emitter then emits nothing and the validator is what complains.
        //   ⚠ Built even when there are no blueprints — the catalogue's list is then empty, and the
        //   emitted output is byte-identical because no DTO names a blueprint.
        var blueprintSchemas = siblings.Blueprints;

        // BP-281 / E7b: the Roslyn-backed struct-size resolver, built once and used by BOTH
        // emitters — the topology core bakes expression-target offsets into action keys and the
        // bridge writes ParseParams at the same offsets, so they must resolve sizes identically.
        // Only built when there is a managed blackboard to size; otherwise null, and the emitted
        // output is byte-identical to before.
        //
        // ⭐⭐⭐ CE-414 — AND IT COMPOSES THE OPTION-A FALLBACK, exactly as BTreeJsonGenerator:182
        //   already does. 🔴 Without it a variable typed `{Blueprint}_{Id:X8}_Bp+Params` — which is
        //   the WHOLE POINT of the compose step: one struct-typed variable per hosting site — is
        //   invisible to this sibling generator, Pack throws, PackParams swallows it and returns
        //   EMPTY, and the asset emits no ParseParams and no param bindings AT ALL. ⛔ Silent, and
        //   the symptom is a hosted blueprint whose params are always zero.
        System.Func<string, int?>? sizeResolver = null;
        if (dto.Blackboard != null && dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
        {
            // ⭐ CE-439, CE-2026 — and a SIBLING behaviour's Inputs struct (BTree or HSM: a hosted subtree's bound params
            //   variable). The ONE resolver every generator injects (GeneratedTypeCatalog.SizeResolver).
            sizeResolver = siblings.SizeResolver(compilation);
        }

        System.Func<System.Guid, ushort?> blueprintIdResolver = assetId =>
        {
            foreach (var schema in blueprintSchemas)
            {
                if (schema.AssetId != assetId) continue;
                // ⚠ A Library or Instance blueprint emits no HsmActivity/HsmGuard thunk, so resolving
                //   one would bake an id nothing registers — a silent TryGetValue miss at dispatch,
                //   which is the exact failure E6 spent a batch on.
                if (!schema.IsAiPrimitive) return null;
                return unchecked((ushort)schema.BlueprintId);
            }
            return null;
        };

        // ⭐⭐⭐ CE-388 / Q74 D-B1 — the same lookup, returning the GENERATED CLASS NAME.
        //
        //   🔒 That string is what BOTH sides hash to get the exit-cleanup id: CSharpEmitter
        //      registers the thunk under HsmActionKey.ForExitCleanup(className), and HsmEmitCore
        //      bakes .OnExitId(<the same expression>) into the blob. The formula itself is a LINKED
        //      file (Q74 D-F) precisely so there is one of it.
        //   ⛔ Same null discipline as above, and for a sharper reason: a non-AiPrimitive emits no
        //      HsmExitCleanup thunk at all, so resolving one would bake an id nothing registered —
        //      and the ONLY symptom would be a channel that is never released. CE-403 measured that
        //      exact class of silent miss in the sibling C# table.
        System.Func<System.Guid, string?> blueprintClassNameResolver = assetId =>
        {
            foreach (var schema in blueprintSchemas)
            {
                if (schema.AssetId != assetId) continue;
                if (!schema.IsAiPrimitive) return null;
                return schema.GeneratedClassName;
            }
            return null;
        };

        // ⭐⭐⭐ CE-388 / Q74 D-D1 — "does this C# activity declare [WritesChannel]?"
        //
        //   🔒 User, 2026-09-28: "same auto-bind" — one rule for both routes, so a C# activity that
        //      writes a channel releases it on exit without the author binding anything.
        //   ⭐ Answered from the Roslyn COMPILATION, which is the only thing that knows: the
        //      attribute lives on the method symbol, and no artefact carries it to the emitter.
        //      (This is the same capability RoslynClrSignatureResolver already demonstrates.)
        //   ⛔ Cached per compilation, not per state — GetTypeByMetadataName is not free and an
        //      asset can bind the same activity in many states.
        var writesChannelCache = new System.Collections.Generic.Dictionary<string, bool>(System.StringComparer.Ordinal);
        System.Func<string, bool> csharpWritesChannel = actionFqn =>
        {
            if (writesChannelCache.TryGetValue(actionFqn, out bool cached)) return cached;

            bool result = false;
            int dot = actionFqn.LastIndexOf('.');
            if (dot > 0)
            {
                string typeName   = actionFqn.Substring(0, dot);
                string methodName = actionFqn.Substring(dot + 1);
                var type = compilation.GetTypeByMetadataName(typeName);
                if (type != null)
                {
                    foreach (var member in type.GetMembers(methodName))
                    {
                        foreach (var attr in member.GetAttributes())
                        {
                            // ⭐ The SAME comparison HsmActionGenerator.cs:129 makes. ⛔ Not a
                            //   name-only check: an unrelated [WritesChannel] in another namespace
                            //   must not turn on a cleanup binding.
                            if (attr.AttributeClass?.ToDisplayString() == "Fbt.Kernel.WritesChannelAttribute")
                            {
                                result = true;
                                break;
                            }
                        }
                        if (result) break;
                    }
                }
            }

            writesChannelCache[actionFqn] = result;
            return result;
        };

        // ⭐⭐⭐ CE-417 B-2 (a′) — a bound C# [SharedAi*] method gets ONE generated call per binding, reading its host
        //   variable in place. ⛔ The variable must BE the method's ref type: refused here, loudly, rather than left to a
        //   CS1503 in generated code (or, before CE-417, a silent type-pun — F7, HsmVariableShowcase).
        // ⭐⭐ CE-3082 D2 — an HSM state's EMPTY OnExit runs its C# activity's [BTreeDeactivator] (the BTree host calls it when
        //   a branch is left; the HSM host never did). A DTO rewrite BEFORE every emitter reads the dto, so the filled OnExit is
        //   an ordinary binding. 📄 docs/DESIGN_Decision_Layer.md §3.3c D2.
        Hrot.AiEditor.Persistence.Emit.HsmDeactivatorExits.Fill(dto, fqn => BTreeDeactivatorScanner.DeactivatorFqnOf(compilation, fqn));

        var sharedAi = SharedAiMethodResolver.Make(compilation);
        bool sharedAiOk = true;
        // ⭐ S8 — the HSM binds every shared form; only a stateful binding whose working state is not a block St member of the
        //   method's WS type is unbindable, and that is an error, never a silent skip.
        foreach (var (site, fqn, problem) in Hrot.AiEditor.Persistence.Emit.SharedAiBindings.UnbindableBindings(dto, sharedAi, sizeResolver))
        {
            spc.ReportDiagnostic(MakeSharedAiTypeDiagnostic(path,
                $"{site} binds the stateful shared method '{fqn}', but it {problem} (design S8: an HSM stateful method's working " +
                "state lives in the HSM's block St)."));
            sharedAiOk = false;
        }
        // ⭐ CE-506 — a global transition binds methods only (the facet offers no blueprint there): reported, never dropped.
        foreach (var gt in dto.GlobalTransitions)
        {
            foreach (var (b, slot) in new[] { (gt.Guard, "guard"), (gt.Action, "action") })
            {
                if (b == null || b.BlueprintAssetId == Guid.Empty) continue;
                spc.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                        GlobalBlueprintErrorId, "HSM global transition binds a blueprint", "'{0}': {1}", "HsmJsonGenerator",
                        DiagnosticSeverity.Error, isEnabledByDefault: true),
                    Location.None, path,
                    $"global transition '{gt.EventName}' {slot} binds blueprint {b.BlueprintAssetId:D}; a global transition " +
                    "has no source state to seed a blueprint's params from — bind a method (CE-506)."));
                sharedAiOk = false;
            }
        }
        foreach (var e in Hrot.AiEditor.Persistence.Emit.SharedAiBindings.Collect(
                     dto, HsmBridgeEmitCore.PackParamsFor(dto, sizeResolver), sharedAi, sizeResolver))
        {
            if (e.Form == Hrot.AiEditor.Persistence.Emit.SharedAiBindings.Form.NoParams) continue;   // S8: binds no variable
            if (string.Equals(e.VariableTypeId, e.Method.ParamTypeId, StringComparison.Ordinal)) continue;
            spc.ReportDiagnostic(MakeSharedAiTypeDiagnostic(path, SharedAiMethodResolver.DescribeMismatch(e)));
            sharedAiOk = false;
        }
        if (!sharedAiOk) return;

        // Emit topology core (CreateBuilder + [HsmDefinition] thunk, NO [HsmLayout]).
        string source;
        try
        {
            source = HsmEmitCore.EmitTopologyCore(dto, sizeResolver, blueprintIdResolver,
                                                 blueprintClassNameResolver, csharpWritesChannel, sharedAi);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during code generation: " + ex.Message));
            return;
        }

        string baseName = System.IO.Path.GetFileNameWithoutExtension(
                              System.IO.Path.GetFileNameWithoutExtension(path));

        // Topology core: {Name}.g.cs
        spc.AddSource(baseName + ".g.cs", source);

        // Bridge: {Name}.Registrar.g.cs  (additive, separate hint name — PU-203, §14 item 3)
        string bridge;
        try
        {
            bridge = HsmBridgeEmitCore.EmitBridge(dto, sizeResolver, sharedAi);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during bridge code generation: " + ex.Message));
            return;
        }

        spc.AddSource(baseName + ".Registrar.g.cs", bridge);

        // ⭐⭐⭐ CE-416 (Q75-S1, Q76 §12.27) — {Name}.Blackboard.g.cs: {Asset}_Blackboard (the Inputs, Pack's offsets) and
        //   {Asset}_Block (In + St). The SAME emitter the BTree generator calls, on the HSM as a blackboard owner — the
        //   registrar's JsonParamsDtoType / BlackboardLayoutType name these types.
        if (dto.Blackboard != null && dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
        {
            string? structSource;
            try
            {
                structSource = Hrot.AiEditor.Persistence.Emit.BTreeEmitCore.EmitBlackboardStructSource(
                    HsmBridgeEmitCore.BlackboardOwner(dto), sizeResolver, out _);
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                    "Exception during blackboard struct generation: " + ex.Message));
                return;
            }
            if (structSource != null)
                spc.AddSource(baseName + ".Blackboard.g.cs", structSource);
        }

        // ⭐⭐⭐ Batch 92 (92b): orchestrators — {Name}.Orchestrators.g.cs
        //
        // ⛔ OMITTED ENTIRELY when the core returns null, which is every asset in today's corpus
        // (none carries an alias) ⇒ the generated output stays byte-identical.
        //
        // ⭐⭐ This is the arm 91b made meaningful: HSM hosts a sub-tree ONLY through an Approach-A
        // alias, and until aliases persisted, nothing an HSM loaded from disk could ever emit.
        // ⛔ It is not "HSM sub-tree hosting is complete" — there is still no authoring gesture that
        // creates the alias, and no blackboard aggregation behind it.
        string? orchestrators;
        try
        {
            orchestrators = HsmOrchestratorEmitCore.Emit(dto);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeParseErrorDiagnostic(path,
                "Exception during orchestrator code generation: " + ex.Message));
            return;
        }

        if (orchestrators != null)
            spc.AddSource(baseName + ".Orchestrators.g.cs", orchestrators);
    }

    /// <summary>Creates a Roslyn diagnostic for an HSM JSON parse/emit error.</summary>
    internal static Diagnostic MakeParseErrorDiagnostic(string path, string detail)
    {
        // Descriptor created inline to avoid RS2008 (release tracking required for static fields).
        // Mirrors BlueprintIncrementalGenerator.ToRoslynDiagnostic pattern.
        var descriptor = new DiagnosticDescriptor(
            id:                 DiagnosticId,
            title:              "HSM JSON parse error",
            messageFormat:      "Failed to process '{0}': {1}",
            category:           "HsmJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path, detail);
    }

    /// <summary>⭐ CE-423 — an ERROR: the variable would silently have no slot, and nothing at runtime could find it.</summary>
    internal static Diagnostic MakeStateStorageDiagnostic(string path, string variableName)
    {
        var descriptor = new DiagnosticDescriptor(
            id:                 StateStorageErrorId,
            title:              "HSM State variable has no storage",
            messageFormat:      "'{0}': {1}",
            category:           "HsmJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path,
            Hrot.AiEditor.Persistence.Emit.StateVariableStorage.Describe(variableName));
    }

    internal static Diagnostic MakeSharedAiTypeDiagnostic(string path, string message)
    {
        var descriptor = new DiagnosticDescriptor(
            id:                 SharedAiTypeErrorId,
            title:              "HSM C# action bound to a variable of the wrong type",
            messageFormat:      "'{0}': {1}",
            category:           "HsmJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path, message);
    }
}
