using System;
using System.Collections.Generic;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⛔⛔⛔ <b>RETIRED <c>2026-09-23</c> (<c>CE-337</c>) — <see cref="Emit"/> ALWAYS RETURNS <c>null</c>,
/// on BOTH arms.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.12. ⭐ Sub-tree hosting is
/// per-SITE now: a host declares <c>{SubtreeAssetId, SubtreeName}</c>, the bridge declares the child's
/// tree-state slot and binds its interpreter through <c>HostedChildren</c>, and the brain ticks it
/// every frame — <c>E5</c>'s shape. ⛔ The reasoning is in <see cref="Emit"/>'s body, at length,
/// because it is the kind of removal a future reader will otherwise try to undo.
/// ⚠ The type and its two production callers stay: a caller that gets <c>null</c> emits no file,
/// which is what every shipped asset already did.
///
/// <para>⛔ HISTORY — what it was, kept so the supersession is legible:</para>
///
/// <para><b>Batch 92 (<c>92a</c>) — the BTree orchestrator emit BODY, moved off the editor model.</b>
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
    /// <summary>Fallback namespace when the asset declares none — matches the editor emitter.
    /// ⚠ Retained: callers reference it.</summary>
    public const string DefaultTargetNamespace = "Hrot.AI.Behaviors";

    /// <summary>
    /// ⛔⛔ <b>Always <c>null</c> since <c>CE-337</c> — the caller emits no file.</b> See the body for
    /// why both arms were retired rather than repaired.
    /// </summary>
    /// <param name="approachBGroups">
    /// ⚠ Kept in the signature: two production callers pass it *(`BTreeJsonGenerator:352`, the editor
    /// sidecar `BTreeOrchestratorEmitter:62`)*, and the argument-validation contract below is the one
    /// thing about this method that never became untrue.
    /// </param>
    public static string? Emit(
        BehaviorTreeAssetDto dto, IReadOnlyList<OrchestratorSyncGroup> approachBGroups)
    {
        if (dto is null)              throw new ArgumentNullException(nameof(dto));
        if (approachBGroups is null)  throw new ArgumentNullException(nameof(approachBGroups));

        // ⭐⭐⭐ CE-337 — BOTH ARMS RETIRED 2026-09-23, ROUTED THE WAY CE-333 ROUTED THE HSM TWIN.
        //   🔒 User, 2026-09-23: "retire the arm."
        //   📄 DESIGN_Occurrence_Scoped_Storage.md §32.12.
        //
        // 🔴🔴 THE EMISSION HAS NEVER COMPILED, AND CE-336's COMPILE RAIL FOUND WHY — FOUR TIMES:
        //   ① `{Child}.GetInterpreter()` — a method defined NOWHERE in the repository (CE-335).
        //   ② `[BTreeAction(Name = "…")]` — Fbt.BTreeActionAttribute is an EMPTY attribute class
        //      with no Name property; every hand-authored use in the corpus is a bare [BTreeAction].
        //   ③ `ref  master,` / `ref  ctx,` — two EMPTY type names for any asset that declares no
        //      BlackboardTypeName/ContextTypeName, which is the default.
        //   ④ 🔴 AND THE ONE THAT CANNOT BE PATCHED: both arms project onto a MASTER BLACKBOARD
        //      STRUCT — `ref master.{VarName}` (Approach A) and `ref master.{sliceField}`
        //      (Approach B). P4 DELETED BrainBlackboard, and every shipped *.btree.json still names
        //      it, as does AiEmitCoreBase.DefaultBlackboardTypeName. ⇒ there is no master struct for
        //      a non-managed asset and no corpus asset can satisfy either arm.
        //   ⭐ ①–③ were fixed; ④ is a DESIGN question, and the answer is that the mechanism is wrong.
        //
        // ⛔⛔ AND APPROACH B WAS ALREADY DEAD ON ITS OWN TERMS — this type's own remarks said so
        //   before any of the above: the sub-tree IDENTITY is session-local (`_syncNodeMeta`, written
        //   only by an InspectorWindow draw, deliberately excluded from the DTO), and the destination
        //   FIELD "never reaches Blackboard.Variables and no blackboard emitter declares it".
        //
        // ⭐⭐ WHERE THE CAPABILITY GOES. Hosting is per-SITE now, not per-alias: E5 gave an HSM STATE
        //   a {SubtreeAssetId, SubtreeName} pair, a tree-state slot keyed by
        //   OccurrenceSlotKey.ComputeTreeStateKey(host, site, child), a registration-time binding
        //   through HostedChildren, and a host that ticks it every frame. ⇒ BTree-hosts-BTree is the
        //   same shape with the NODE's visual id as the site — ⛔ NOT BUILT, and it is a slice, not a
        //   patch. 🔒 One mechanism for one concept (ruling 9); this was the second and third.
        //
        // ⚠ Measured before removing: 0 shipped *.btree.json carries an alias, and Approach B has no
        //   load path at all ⇒ nothing in the field loses a capability.
        // ⛔ The ALIAS DATA and the sync BINDINGS are untouched — only the EMISSION is retired.
        return null;
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
