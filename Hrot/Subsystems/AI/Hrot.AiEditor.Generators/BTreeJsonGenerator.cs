using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// Roslyn IncrementalGenerator that consumes <c>*.btree.json</c> AdditionalTexts
/// and emits <c>CreateBuilder()</c> + <c>[BTreeDefinition]</c> thunk (NO <c>[BTreeLayout]</c>)
/// to <c>obj/GeneratedFiles/{Name}.g.cs</c>.
///
/// Design §6.2 (PU-201): JSON-owned assets generate topology core only; layout lives in JSON.
/// Per-asset deserialization failure → Roslyn diagnostic (never throws, never fails siblings).
/// Mirrors <see cref="Hrot.Blueprints.Generators.BlueprintIncrementalGenerator"/> control flow.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BTreeJsonGenerator : IIncrementalGenerator
{
    /// <summary>Diagnostic code for BTree JSON parse/deserialize errors.</summary>
    public const string DiagnosticId = "BTREE0001";

    /// <summary>Diagnostic code for BTree codegen validation failures (skipped asset, non-build-breaking).</summary>
    public const string CodegenWarningId = "BTREE0002";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Provider: raw file text from *.btree.json AdditionalTexts
        IncrementalValuesProvider<(string Path, string Text)> rawFiles =
            context.AdditionalTextsProvider
                .Where(static at => at.Path.EndsWith(".btree.json",
                    System.StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) =>
                {
                    string text = at.GetText(ct)?.ToString() ?? string.Empty;
                    return (at.Path, text);
                });

        // Provider: raw file text from *.bp.json AdditionalTexts (Blueprint assets).
        //
        // Option A (I2/I3 gap fix): the Blueprint source generator and this generator are sibling
        // IIncrementalGenerators that cannot see each other's generated output within one generation
        // pass — a blueprint's generated `{Name}_{Id:X8}_Bp` class (Params/WorkingState/TickCore) is
        // never resolvable via Compilation.GetTypeByMetadataName from here. Collecting the SAME
        // *.bp.json AdditionalTexts the Blueprint generator itself parses lets this generator derive
        // just enough of that generated shape (class identity + Params field schema) from the JSON
        // source of truth instead — see GeneratedBlueprintSchemaCatalog.
        IncrementalValuesProvider<(string Path, string Text)> bpJsonFiles =
            context.AdditionalTextsProvider
                .Where(static at => at.Path.EndsWith(".bp.json",
                    System.StringComparison.OrdinalIgnoreCase))
                .Select(static (at, ct) =>
                {
                    string text = at.GetText(ct)?.ToString() ?? string.Empty;
                    return (at.Path, text);
                });

        IncrementalValueProvider<ImmutableArray<(string Path, string Text)>> bpJsonCollected =
            bpJsonFiles.Collect();

        // ⭐⭐⭐ Q49 option D — the SIBLING TREES, for subtree-sync identity.
        //    A generator cannot load assets, so a master tree cannot ask "what blackboard type does the
        //    subtree I call declare?" the way the editor does (Q49 option C, catalog.FindByAssetId).
        //    ⭐ These are the SAME *.btree.json AdditionalTexts this generator already receives — a
        //      second projection of texts in hand, not new plumbing. See GeneratedBTreeSchemaCatalog.
        IncrementalValueProvider<ImmutableArray<(string Path, string Text)>> btreeJsonCollected =
            rawFiles.Collect();

        // Combine with the full compilation so the method-compatibility validator can
        // resolve type/method symbols, plus the collected *.bp.json schemas (Option A fallback).
        //
        // Incrementality note: combining with the full CompilationProvider means
        // GenerateOneAsset re-runs on ANY compilation change (not only asset changes).
        // This is acceptable for the small *.btree.json asset set.  A fancier
        // incremental symbol extraction is deferred (VE-DEBT-003).
        IncrementalValuesProvider<(string Path, string Text, Compilation Compilation, ImmutableArray<(string Path, string Text)> BpJsonFiles, ImmutableArray<(string Path, string Text)> BtreeJsonFiles)> combined =
            rawFiles.Combine(context.CompilationProvider)
                    .Combine(bpJsonCollected)
                    .Combine(btreeJsonCollected)
                    .Select(static (pair, _) =>
                        (pair.Left.Left.Left.Path, pair.Left.Left.Left.Text, pair.Left.Left.Right,
                         pair.Left.Right, pair.Right));

        // Per-asset: deserialize → validate bound methods → emit topology core → register source output
        context.RegisterSourceOutput(combined, static (spc, item) =>
        {
            GenerateOneAsset(spc, item.Path, item.Text, item.Compilation, item.BpJsonFiles, item.BtreeJsonFiles);
        });
    }

    private static void GenerateOneAsset(SourceProductionContext spc, string path, string text,
        Compilation compilation, ImmutableArray<(string Path, string Text)> bpJsonFiles,
        ImmutableArray<(string Path, string Text)> btreeJsonFiles)
    {
        // Option A: parse the blueprint schemas once, up front — used both by the method-compatibility
        // validator (AiPrimitiveTickCore method-resolution fallback) and the struct-size resolver
        // (AiPrimitiveTickCore Params-size fallback) below.
        System.Collections.Generic.IReadOnlyList<GeneratedBlueprintSchema> blueprintSchemas =
            GeneratedBlueprintSchemaCatalog.Parse(bpJsonFiles);
        // ⭐ Q49 option D: what every SIBLING tree declares — the only input the subtree-sync projection
        //   cannot read out of this asset's own JSON.
        System.Collections.Generic.IReadOnlyDictionary<Guid, GeneratedBTreeSchemaCatalog.Entry> btreeCatalog =
            GeneratedBTreeSchemaCatalog.Parse(btreeJsonFiles);
        // Deserialize — failure becomes a diagnostic, never throws, never fails siblings.
        BehaviorTreeAssetDto? dto;
        try
        {
            dto = BTreeJsonServices.Deserialize(text);
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

        // ⭐⭐⭐ Q50 option A + Q49 option D — DECLARE THE SUB-TREE SLICES, then derive the groups.
        //    🔒 User, 2026-08-22: "i hoped the editor automatically adds the subtree's data, which is
        //       likely the option A."
        //    ⛔⛔ ORDER IS LOAD-BEARING: this runs BEFORE the blackboard is sized, packed or emitted,
        //       because the slice fields ARE blackboard variables. Doing it after would emit an
        //       orchestrator writing `ref master.X` into a struct that was already packed without X —
        //       gap ②, which is exactly why the Approach-B arm could never ship (BP-342, BP-306's shape).
        //    ⭐ Both halves come from ONE walk over persisted data (SubtreeSyncProjection): the groups
        //      and the fields they require. Two walks would be two answers to "which nodes qualify".
        //    ⛔⛔ AND IT REQUIRES A **MANAGED** MASTER BLACKBOARD — measured 2026-08-22, by a rail that
        //       failed with the probe REMOVED. A Category-1 blackboard is a HAND-WRITTEN struct this
        //       generator only reflects: it cannot gain a field, so the slice can never be declared and
        //       emitting the orchestrator anyway would reference a member that does not exist — the very
        //       state gap ② is. ⇒ no managed blackboard ⇒ NO groups, NO slices, silently and completely.
        //       ⚠ Neither BP-342 nor Q50 named this constraint; the rail found it.
        var (approachBGroups, sliceFields) = dto.Blackboard.Managed
            ? SubtreeSyncProjection.Project(
                dto,
                assetId => btreeCatalog.TryGetValue(assetId, out var e) ? e.BlackboardTypeName : null)
            : (System.Array.Empty<OrchestratorSyncGroup>(),
               System.Array.Empty<SubtreeSyncProjection.SliceField>());

        foreach (var slice in sliceFields)
        {
            // ⚠ A hand-authored variable of the same name WINS — the designer's declaration is explicit
            //   and this one is derived; silently overwriting it would lose authored data.
            bool alreadyDeclared = false;
            foreach (var existing in dto.Blackboard.Variables)
                if (string.Equals(existing.Name, slice.FieldName, StringComparison.Ordinal))
                { alreadyDeclared = true; break; }
            if (alreadyDeclared) continue;

            dto.Blackboard.Variables.Add(new BlackboardVariableDto
            {
                Name = slice.FieldName,
                Type = new BlackboardTypeRefDto { TypeId = slice.TypeId },
                Comment = "Auto-allocated sub-tree parameter slice (Approach B).",
                IsAutoManaged = true,
            });
        }

        // Validate bound method signatures before emitting.
        // An asset with any incompatible/unresolved bound leaf is skipped + BTREE0002 Warning.
        // This prevents the emitted .Action(Method,...) / .Condition(Method,...) calls from
        // breaking the Hrot.AI.Behaviors assembly build (the catastrophic hole fixed by BT-17).
        string? compatError;
        try
        {
            compatError = BTreeMethodCompatibilityValidator.Validate(dto, compilation, blueprintSchemas);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                "Exception during method compatibility validation: " + ex.Message));
            return;
        }

        if (compatError != null)
        {
            spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path, compatError));
            return;
        }

        // S1-2b: Build a Roslyn-backed struct-size resolver for this compilation.
        // The resolver handles struct-DTO types not in BTreeBlackboardPackHelper.KnownSizes.
        // Guarded by Managed flag — non-managed assets get a null resolver (no change).
        System.Func<string, int?>? structSizeResolver = null;
        if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
        {
            System.Func<string, int?> roslynResolver = StructSizeResolver.MakeDelegate(compilation);

            // Option A: when the Roslyn resolver can't see a type (e.g. a blueprint's generated
            // `{Name}_{Id:X8}_Bp+Params` — never visible to this sibling generator, see
            // GeneratedBlueprintSchemaCatalog), fall back to computing its size from the matching
            // .bp.json's parameter schema using the SAME Sequential-alignment math
            // (StructSizeResolver.ComputeSequentialSize) the Roslyn path itself uses.
            structSizeResolver = typeId =>
                roslynResolver(typeId)
                ?? GeneratedBlueprintSchemaCatalog.TryResolveParamsSize(typeId, blueprintSchemas, compilation);

            // Check for any unresolvable managed variable BEFORE emitting anything.
            // An unresolvable type means we cannot guarantee the struct layout, so skip
            // the whole asset with BTREE0002 (never a partial/silent emit).
            // S3-G: State-role variables live in the partition tier, NOT the inline param region, so
            // they never contribute to the inline struct layout and their size is taken at runtime via
            // Marshal.SizeOf<T>(). They are excluded from this compile-time size pre-check (matching the
            // Pack/WouldOverflow exclusion) — this lets fixed-buffer working-state structs the
            // compile-time StructSizeResolver cannot size (e.g. HillAttackMutableState) still generate.
            foreach (var v in dto.Blackboard.Variables)
            {
                if (v.Role == Hrot.AiEditor.Persistence.BlackboardVariableRole.State) continue;

                string typeId = v.Type?.TypeId ?? string.Empty;
                if (!BTreeBlackboardPackHelper.TryGetSize(typeId, out _))
                {
                    // Not a known primitive — try the struct resolver.
                    int? resolved = structSizeResolver(typeId);
                    if (!resolved.HasValue)
                    {
                        spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                            $"managed blackboard variable '{v.Name}' has type '{typeId}' which cannot be resolved " +
                            "in the compilation — asset skipped (ensure the type is defined and referenced)"));
                        return;
                    }
                }
            }
        }

        // S1-2: 100-byte inline-budget guard for managed blackboards.
        // A managed asset whose packed variables exceed the 100-byte inline budget would
        // emit an oversized struct (and a topology that assumes it). Detect overflow BEFORE
        // emitting anything and skip the whole asset with a BTREE0002 Warning (never a hard
        // build break, matching the other BTREE0002 skips). Guarded by dto.Blackboard.Managed
        // so Managed==false assets stay byte-identical.
        if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
        {
            bool wouldOverflow;
            try
            {
                wouldOverflow = BTreeBlackboardPackHelper.WouldOverflow(
                    dto.Blackboard.Variables, structSizeResolver, out _);
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                    "Exception during blackboard overflow check: " + ex.Message));
                return;
            }

            if (wouldOverflow)
            {
                spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                    $"managed blackboard exceeds the {BTreeBlackboardPackHelper.MaxInlineBytes}-byte " +
                    "inline budget — asset skipped (reduce the number/size of blackboard variables)"));
                return;
            }
        }

        // Emit topology core (CreateBuilder + [BTreeDefinition] thunk, NO [BTreeLayout]).
        string source;
        try
        {
            source = BTreeEmitCore.EmitTopologyCore(dto, structSizeResolver);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                "Exception during code generation: " + ex.Message));
            return;
        }

        string baseName = System.IO.Path.GetFileNameWithoutExtension(
                              System.IO.Path.GetFileNameWithoutExtension(path));

        // Topology core: {Name}.g.cs
        spc.AddSource(baseName + ".g.cs", source);

        // S1-2: Managed blackboard struct: {Name}.Blackboard.g.cs
        // Guard: only when dto.Blackboard.Managed == true; non-managed assets are byte-identical.
        if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
        {
            string? structSource;
            try
            {
                structSource = BTreeEmitCore.EmitBlackboardStructSource(dto, structSizeResolver, out _);
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                    "Exception during blackboard struct generation: " + ex.Message));
                return;
            }

            if (structSource != null)
                spc.AddSource(baseName + ".Blackboard.g.cs", structSource);
        }

        // Bridge: {Name}.Registrar.g.cs  (additive, separate hint name — PU-203, §14 item 3)
        // HAJSON-B: scan for [BTreeDeactivator] hooks and pass them to the bridge emitter.
        string bridge;
        try
        {
            // Build the set of action keys that will be registered by this bridge so the
            // scanner can match against them without needing to re-derive offsets.
            System.Func<string, int?>? resolverForDeactivator = structSizeResolver;
            System.Collections.Generic.IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packed = null;
            if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0 && resolverForDeactivator != null)
            {
                try { packed = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, resolverForDeactivator, out _); }
                catch { packed = null; }
            }
            else if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
            {
                try { packed = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, null, out _); }
                catch { packed = null; }
            }

            var registeredKeys = BTreeBridgeEmitCore.CollectRegisteredActionKeys(dto, packed);
            var deactivators   = BTreeDeactivatorScanner.Scan(compilation, registeredKeys);
            bridge = BTreeBridgeEmitCore.EmitBridge(dto, structSizeResolver, deactivators);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                "Exception during bridge code generation: " + ex.Message));
            return;
        }

        spc.AddSource(baseName + ".Registrar.g.cs", bridge);

        // ⭐⭐⭐ Batch 92 (92b): orchestrators — {Name}.Orchestrators.g.cs
        //
        // ⛔ OMITTED ENTIRELY when the core returns null, which is every asset in today's corpus
        // (none carries an alias or a sync binding) ⇒ the generated output stays byte-identical.
        //
        // ⭐⭐⭐ Q49 D + Q50 A (2026-08-22): THE GROUPS ARE REAL NOW.
        // ⛔ (was: "a generator provably has no groups to pass" — true then, for two reasons that are
        //    both now closed. ① the identity needed BehaviorTreeAsset._syncNodeMeta, a UI-draw-only
        //    field: option D reads the sibling *.btree.json instead, so no editor state is involved.
        //    ② the field the body writes into was declared by nothing: option A declares it above,
        //    BEFORE the blackboard is packed.)
        // ⚠ Still omitted entirely when the core returns null — no alias and no sync binding — which is
        //    every asset in today's corpus, so the generated output stays byte-identical.
        string? orchestrators;
        try
        {
            orchestrators = BTreeOrchestratorEmitCore.Emit(dto, approachBGroups);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(MakeCodegenWarningDiagnostic(path,
                "Exception during orchestrator code generation: " + ex.Message));
            return;
        }

        if (orchestrators != null)
            spc.AddSource(baseName + ".Orchestrators.g.cs", orchestrators);
    }

    /// <summary>Creates a Roslyn diagnostic for a BTree JSON parse/deserialize error.</summary>
    internal static Diagnostic MakeParseErrorDiagnostic(string path, string detail)
    {
        // Descriptor created inline to avoid RS2008 (release tracking required for static fields).
        // Mirrors BlueprintIncrementalGenerator.ToRoslynDiagnostic pattern.
        var descriptor = new DiagnosticDescriptor(
            id:                 DiagnosticId,
            title:              "BTree JSON parse error",
            messageFormat:      "Failed to process '{0}': {1}",
            category:           "BTreeJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path, detail);
    }

    /// <summary>Creates a Roslyn Warning diagnostic for a BTree codegen validation failure.</summary>
    internal static Diagnostic MakeCodegenWarningDiagnostic(string path, string detail)
    {
        var descriptor = new DiagnosticDescriptor(
            id:                 CodegenWarningId,
            title:              "BTree asset skipped (codegen validation)",
            messageFormat:      "Skipped '{0}': {1}. Fix the asset in the editor.",
            category:           "BTreeJsonGenerator",
            defaultSeverity:    DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.None, path, detail);
    }
}
