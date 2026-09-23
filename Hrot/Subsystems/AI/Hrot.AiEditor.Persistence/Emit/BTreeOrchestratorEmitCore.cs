using System;
using System.Collections.Generic;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <b>Batch 92 (<c>92a</c>) — the BTree orchestrator emit BODY, moved off the editor model.</b>
///
/// <para>📐 Companion to <see cref="HsmOrchestratorEmitCore"/>; the Approach-A alias arm is shared via
/// <see cref="OrchestratorAliasCollector"/>. ⭐ <b>One body</b> serves the editor sidecar path
/// (<c>92c</c>) and the generator's <c>{baseName}.Orchestrators.g.cs</c> (<c>92b</c>) — 📌 ruling 9.</para>
///
/// <para>⛔⛔ <b>THE APPROACH-B ARM CANNOT BE DRIVEN FROM THE DTO, AND THAT IS MEASURED, NOT ASSUMED.</b>
/// The handoff's premise — <i>"the DTOs now carry BOTH inputs: <c>SubtreeSyncBindings</c> (since PU)
/// and <c>Aliases</c> (since <c>91b</c>)"</i> — is ⭐ <b>true for the bindings and false for everything
/// else Approach B needs</b>:</para>
///
/// <list type="number">
/// <item>⭐⭐ <b>The sub-tree IDENTITY is session-local.</b>
/// <c>BehaviorTreeAsset.GetApproachBSyncGroups()</c> (<c>:719</c>) skips any node absent from
/// <c>_syncNodeMeta</c>, whose <b>only</b> writer is <c>InspectorWindow:194</c> — a UI draw. It has no
/// load path, and <c>BehaviorTreeAssetDto.cs:10</c> names it <b>deliberately excluded</b>, enforced by
/// <c>BTreeDtoRuntimeFieldExclusionTests:29</c>. ⚠ So even in the EDITOR, Approach B emits nothing
/// after a reload until a designer re-opens that panel.</item>
/// <item>⛔⛔ <b>The destination FIELD does not exist.</b> The emitted body writes
/// <c>ref master.{SubtreeName}_{DtoTypeName}</c>. That slice comes from
/// <c>GetAutoAllocatedVariables()</c> (<c>:768</c>), whose only consumer is
/// <c>BlackboardAuthoringWindow:529</c>, which merely <b>displays</b> it greyed as
/// <i>"(size unknown until build)"</i>. ⇒ it never reaches <c>Blackboard.Variables</c> and no
/// blackboard emitter declares it.</item>
/// </list>
///
/// <para>⇒ ⭐⭐⭐ <b>Approach-B groups are therefore an explicit PARAMETER, not an optional one.</b>
/// ⛔ No default: 📌 the silent-default rule — <i>"a production caller that HAS a dependency must PASS
/// it"</i>. The editor passes its <c>_syncNodeMeta</c>-derived groups; the generator passes an EMPTY
/// list because it provably has none, and says so at the call site. ⚠ <b>Widening the DTO is a
/// persistence-schema decision and is NOT taken here</b> — and it would not be sufficient anyway
/// while gap (2) stands.</para>
/// </summary>
public static class BTreeOrchestratorEmitCore
{
    private const string Indent = "    ";
    private const string FbtNamespace          = "Fbt";
    private const string BTreeContextNs        = "Fdp.Toolkit.Behavior";
    private const string RuntimeCompilerServNs = "System.Runtime.CompilerServices";

    /// <summary>Fallback namespace when the asset declares none — matches the editor emitter.</summary>
    public const string DefaultTargetNamespace = "Hrot.AI.Behaviors";

    /// <summary>
    /// Generates the orchestrator source text for <paramref name="dto"/>.
    /// ⭐ Returns <c>null</c> when there is nothing to emit — ⛔ the caller emits <b>no file at all</b>,
    /// which is what keeps the corpus byte-identical.
    /// </summary>
    /// <param name="approachBGroups">
    /// ⭐⭐ Field-sync groups, which <b>only the editor can supply</b> — see the type remarks.
    /// Pass an empty list when the caller has none.
    /// </param>
    public static string? Emit(
        BehaviorTreeAssetDto dto, IReadOnlyList<OrchestratorSyncGroup> approachBGroups)
    {
        if (dto is null)              throw new ArgumentNullException(nameof(dto));
        if (approachBGroups is null)  throw new ArgumentNullException(nameof(approachBGroups));

        var methods = OrchestratorAliasCollector.Collect(dto.Aliases, VariableNamesOf(dto), "BTreeAsset");

        if (methods.Count == 0 && approachBGroups.Count == 0) return null;

        var usingsSet = new HashSet<string>(StringComparer.Ordinal)
        {
            RuntimeCompilerServNs,
            FbtNamespace,
            BTreeContextNs,
        };
        OrchestratorAliasCollector.AddDtoNamespaces(usingsSet, methods, dto.TargetNamespace);

        string targetNs = string.IsNullOrEmpty(dto.TargetNamespace)
            ? DefaultTargetNamespace
            : dto.TargetNamespace;

        // Approach B: subtree nodes with at least one ACTIVE field-level sync binding.
        var approachBMethods = new List<OrchestratorSyncGroup>();
        foreach (var group in approachBGroups)
        {
            string key = OrchestratorAliasCollector.SanitizeIdentifier(group.SubtreeName, "BTreeAsset");
            // Skip if already covered by Approach A.
            bool coveredByA = false;
            foreach (var m in methods)
                if (string.Equals(m.SubTreeName, key, StringComparison.Ordinal)) { coveredByA = true; break; }
            if (coveredByA) continue;

            if (ActiveBindings(group, syncIn: true).Count == 0
                && ActiveBindings(group, syncIn: false).Count == 0) continue;

            approachBMethods.Add(group);

            if (!string.IsNullOrEmpty(group.SubtreeDtoTypeNs)
                && !string.Equals(group.SubtreeDtoTypeNs, targetNs, StringComparison.Ordinal))
                usingsSet.Add(group.SubtreeDtoTypeNs!);
        }

        if (methods.Count == 0 && approachBMethods.Count == 0) return null;

        var sortedUsings = AiEmitCoreBase.SortUsings(usingsSet);

        // ⭐⭐⭐ CE-336 — THESE USED TO READ THE RAW DTO FIELDS, AND AN ASSET THAT DECLARES NEITHER
        //    (the default for a freshly-created one) EMITTED `ref  master,` / `ref  ctx,` — two
        //    EMPTY type names, i.e. C# that does not parse. 📐 Found by the first rail that ever
        //    COMPILED this emitter's output. ⛔ `AiEmitCoreBase.Effective*TypeName` has been the
        //    single source of truth for this exact fallback since CE-235, and BTreeBridgeEmitCore:329
        //    already calls it — the seam existed and this emitter never adopted it.
        // ⚠ The blackboard FALLBACK still names `BrainBlackboard`, which P4 RETIRED — see CE-337.
        //    That is a defect in the default, not in this call site, and it is filed rather than
        //    papered over here.
        string bbShort   = OrchestratorAliasCollector.ShortTypeName(
                               AiEmitCoreBase.EffectiveBlackboardTypeName(dto.BlackboardTypeName));
        string ctxShort  = OrchestratorAliasCollector.ShortTypeName(
                               AiEmitCoreBase.EffectiveContextTypeName(dto.ContextTypeName));
        string className = OrchestratorAliasCollector.SanitizeIdentifier(dto.Name, "BTreeAsset");

        var sb = new StringBuilder();

        sb.Append(AiEmitCoreBase.BuildHeader(dto.AssetId));
        sb.AppendLine("// Auto-generated orchestrator actions for aliased sub-trees.");
        sb.AppendLine($"// OwningAssetName: {dto.Name}");
        sb.AppendLine();

        foreach (var ns in sortedUsings)
        {
            if (ns.Length == 0) sb.AppendLine();
            else                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}_Orchestrators");
        sb.AppendLine("{");

        for (int i = 0; i < methods.Count; i++)
        {
            var m = methods[i];

            // ⭐⭐ CE-336 — `[BTreeAction(Name = "…")]` DOES NOT COMPILE: `Fbt.BTreeActionAttribute`
            //    is an EMPTY attribute class (Fbt.Kernel/Attributes/BTreeActionAttribute.cs:10) with
            //    no `Name` property, and every hand-authored use in the corpus is a bare
            //    `[BTreeAction]`. ⛔ Four text rails asserted the named form; none compiled it.
            // ⚠ The action's identity is its METHOD NAME, which is what Fbt.SourceGen registers —
            //    so nothing is lost. How a tree NODE binds to `Orchestrate_X_Tick` is the alias arm's
            //    own open question (CE-337), not something an invalid attribute argument answered.
            sb.AppendLine($"{Indent}[BTreeAction]   // Orchestrate_{m.SubTreeName}");
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{m.SubTreeName}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref {ctxShort} ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            // ⭐⭐⭐ O4 / C1 — the child ticks against ITS OWN BehaviorTreeState, from its own slot.
            // ⛔⛔ THIS LINE WAS THE DEFECT: it passed `ref state`, the MASTER's state, so host and
            //    child shared one RunningNodeIndex and the host won (its ExecuteAction writes AFTER
            //    the hosting action returns). 📄 §3.1, §18; rail HostedSubtreeCursorTests.O4_R1.
            // ⭐ The key is BAKED here (D3's optimisation) but computed by the SAME linked function a
            //    hand-written host calls at runtime — ⛔ one arithmetic, two callers.
            int slotKeyA = Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.ComputeTreeStateKey(
                dto.AssetId, m.SiteElementId, m.SubtreeAssetId);
            // ⭐⭐⭐ CE-335 — THIS LINE USED TO SAY `{m.SubTreeName}.GetInterpreter()`, AND THAT METHOD
            //    IS DEFINED NOWHERE. 📐 Measured 2026-09-23: three emitter sites referenced it, three
            //    tests asserted its TEXT, zero definitions existed ⇒ this arm has never compiled.
            //    ⛔ It could not have: a generated thunk is STATIC and there is no ambient
            //    BehaviorRegistry, so it cannot resolve a child by name at tick time. ⭐ HostedChildren
            //    binds the child at REGISTRATION, keyed by the slot key this site already bakes.
            // ⭐ The blackboard is the aliased DTO field reinterpreted as bytes: the registry holds
            //    Interpreter<byte, BTreeContext> (BehaviorRegistry.cs:151), which is the ONE blackboard
            //    shape every registered behaviour has. 📄 DESIGN §32.2.4.
            sb.AppendLine($"{Indent}{Indent}ref var subBb = ref Unsafe.As<{m.DtoTypeName}, byte>(ref master.{m.VarName});");
            sb.AppendLine($"{Indent}{Indent}return global::Fdp.Toolkit.Behavior.HostedSubtree.Tick(global::Fdp.Toolkit.Behavior.HostedChildren.Require({slotKeyA}), ref subBb, ref ctx, {slotKeyA});");
            sb.AppendLine($"{Indent}}}");

            if (i < methods.Count - 1 || approachBMethods.Count > 0)
                sb.AppendLine();
        }

        // ⭐ §8.3's shape, preserved verbatim: COPY IN · TICK · COPY OUT.
        foreach (var group in approachBMethods)
        {
            string subTreeId  = OrchestratorAliasCollector.SanitizeIdentifier(group.SubtreeName, "BTreeAsset");
            // ⭐⭐⭐ Q50: ONE composer for this name. SubtreeSyncProjection DECLARES the field with it;
            //    this WRITES through it. ⛔ A one-character divergence between the two is a build break
            //    with no obvious cause, so neither side spells it out (ruling 9).
            string sliceField = SubtreeSyncProjection.SliceFieldName(subTreeId, group.SubtreeDtoTypeName);
            var syncIn  = ActiveBindings(group, syncIn: true);
            var syncOut = ActiveBindings(group, syncIn: false);

            sb.AppendLine($"{Indent}[BTreeAction]   // Orchestrate_{subTreeId}");   // CE-336, as above
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{subTreeId}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref {ctxShort} ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            sb.AppendLine($"{Indent}{Indent}ref var subDto = ref master.{sliceField};");
            foreach (var b in syncIn)
                sb.AppendLine($"{Indent}{Indent}subDto.{b.FieldName} = master.{b.MasterVariableName};");
            // ⭐⭐⭐ O4 / C1 — same fix, the COPY IN / TICK / COPY OUT variant. ⛔ Was `ref state`.
            int slotKeyB = Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.ComputeTreeStateKey(
                dto.AssetId, group.SiteNodeVisualId, group.SubtreeAssetId);
            // ⭐ CE-335, the copy-in/tick/copy-out twin. Same substitution, same reason.
            sb.AppendLine($"{Indent}{Indent}ref var subBbB = ref Unsafe.As<{group.SubtreeDtoTypeName}, byte>(ref subDto);");
            sb.AppendLine($"{Indent}{Indent}var result = global::Fdp.Toolkit.Behavior.HostedSubtree.Tick(global::Fdp.Toolkit.Behavior.HostedChildren.Require({slotKeyB}), ref subBbB, ref ctx, {slotKeyB});");
            foreach (var b in syncOut)
                sb.AppendLine($"{Indent}{Indent}master.{b.MasterVariableName} = subDto.{b.FieldName};");
            sb.AppendLine($"{Indent}{Indent}return result;");
            sb.AppendLine($"{Indent}}}");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// The bindings in one direction that actually copy something, ordered by field name.
    /// ⚠ A binding with no <c>MasterVariableName</c> has no source/target and is skipped.
    /// </summary>
    private static List<OrchestratorSyncBinding> ActiveBindings(OrchestratorSyncGroup group, bool syncIn)
    {
        var result = new List<OrchestratorSyncBinding>();
        foreach (var b in group.Bindings)
        {
            if (b is null || b.MasterVariableName is null) continue;
            if (syncIn ? b.SyncIn : b.SyncOut) result.Add(b);
        }
        result.Sort(static (a, b) => string.CompareOrdinal(a.FieldName, b.FieldName));
        return result;
    }

    /// <summary>
    /// ⚠ Emission order follows the blackboard declaration order — ⛔ not the alias dictionary's key
    /// order, which is not a contract.
    /// </summary>
    private static IEnumerable<string> VariableNamesOf(BehaviorTreeAssetDto dto)
    {
        var vars = dto.Blackboard?.Variables;
        if (vars is null) yield break;
        foreach (var v in vars) yield return v.Name;
    }
}

/// <summary>
/// ⭐ One subtree node needing an Approach-B orchestrator, in terms the netstandard2.0 emit core can
/// see. ⛔ Deliberately NOT <c>Hrot.Editor.AiShared.Blackboard.ApproachBSyncGroup</c> — that type lives
/// in the net8/ImGui editor assembly this one must not reference. ⭐ The editor maps onto it.
/// </summary>
public sealed class OrchestratorSyncGroup
{
    public OrchestratorSyncGroup(
        string subtreeName,
        string subtreeDtoTypeName,
        string? subtreeDtoTypeNs,
        IReadOnlyList<OrchestratorSyncBinding> bindings,
        Guid siteNodeVisualId = default,
        Guid subtreeAssetId = default)
    {
        SubtreeName        = subtreeName;
        SubtreeDtoTypeName = subtreeDtoTypeName;
        SubtreeDtoTypeNs   = subtreeDtoTypeNs;
        Bindings           = bindings;
        SiteNodeVisualId   = siteNodeVisualId;
        SubtreeAssetId     = subtreeAssetId;
    }

    /// <summary>⭐ <c>O4</c> — the hosting node. <c>D5</c>: stable, ⛔ never an ordinal.</summary>
    public Guid SiteNodeVisualId { get; }

    /// <summary>⭐ <c>O4</c> — the hosted child asset.</summary>
    public Guid SubtreeAssetId { get; }

    /// <summary>Sub-tree asset name; sanitised into the method-name suffix.</summary>
    public string SubtreeName { get; }
    /// <summary>Short name of the sub-tree's blackboard struct.</summary>
    public string SubtreeDtoTypeName { get; }
    /// <summary>Its namespace, or null when it shares the master's.</summary>
    public string? SubtreeDtoTypeNs { get; }
    /// <summary>All bindings on the node, including inactive ones — the core filters.</summary>
    public IReadOnlyList<OrchestratorSyncBinding> Bindings { get; }
}

/// <summary>One field-level sync binding. Mirrors <c>SubtreeSyncBindingDto</c>'s four members.</summary>
public sealed class OrchestratorSyncBinding
{
    public OrchestratorSyncBinding(
        string fieldName, string? masterVariableName, bool syncIn, bool syncOut)
    {
        FieldName          = fieldName;
        MasterVariableName = masterVariableName;
        SyncIn             = syncIn;
        SyncOut            = syncOut;
    }

    public string FieldName { get; }
    public string? MasterVariableName { get; }
    public bool SyncIn { get; }
    public bool SyncOut { get; }
}
