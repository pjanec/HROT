using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Hrot.AiEditor.Persistence;

namespace Hrot.AiEditor.Persistence.Hsm;

// ── Design §5.2: HSM persisted DTO ───────────────────────────────────────────
// Runtime-only fields EXCLUDED per §5.2:
//   Blob/Metadata, FlatIndex, *PinId, _aliases hydration,
//   LoadDiagnosticMessage, IsDirty, Changed, IsBreakpoint.

// Blackboard block is shared via BTree's BlackboardBlockDto — re-declared
// here to keep the Hsm namespace self-contained and the persistence lib
// dependency-free (no cross-namespace DTO leakage needed at this layer).
// The test project imports both namespaces; the mapper uses both.

// ── Transition waypoint ───────────────────────────────────────────────────────

/// <summary>A single canvas waypoint on a transition curve.</summary>
public sealed class WaypointDto
{
    public float X { get; set; }
    public float Y { get; set; }
}

// ── Transition kind ───────────────────────────────────────────────────────────

public enum TransitionKindDto { External, Internal, Local }

// ── State node ────────────────────────────────────────────────────────────────

/// <summary>
/// Persisted editor state node.
/// StableId is the primary identity; FlatIndex is runtime-only and excluded.
/// </summary>
public sealed class StateNodeDto
{
    public Guid StableId { get; set; }
    public string Name { get; set; } = string.Empty;

    // Topology
    public List<Guid> ChildStableIds { get; set; } = new();
    public Guid? ParentStableId { get; set; }

    // State flags
    public bool IsInitial { get; set; }
    public bool IsHistory { get; set; }
    public bool IsDeepHistory { get; set; }
    public bool IsParallel { get; set; }
    public bool IsFinal { get; set; }

    // Actions (nullable strings matching editor model)
    public string? OnEntryAction { get; set; }
    public string? OnExitAction { get; set; }
    public string? ActivityAction { get; set; }
    public string? TimerAction { get; set; }

    // Region membership
    public int RegionIndex { get; set; }

    /// <summary>
    /// ⭐⭐ <c>DEBT-AIB-028</c>(a): when non-empty, this state HOSTS an external behaviour asset
    /// (BTree or nested HSM). ⛔ Until Batch 75 the model carried this field and the persistence layer
    /// did not, so <b>no asset loaded from disk could ever set it</b> — and the two validator rules
    /// that read it (rule 8 cross-region stateful subtree, rule 8b shared scope) were dormant in
    /// production regardless of what a designer authored.
    /// </summary>
    /// <remarks>
    /// ⭐ Omitted from JSON when empty, so every existing asset stays byte-identical.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Guid SubtreeAssetId { get; set; }

    // Deferred events (by name; emit core resolves to IDs using DTO event order)
    public List<string> DeferredEventNames { get; set; } = new();

    // Layout
    public float X { get; set; }
    public float Y { get; set; }
    public float? SizeOverrideX { get; set; }
    public float? SizeOverrideY { get; set; }
    public string? Comment { get; set; }
    public bool IsCollapsed { get; set; }
    public string? ColorOverride { get; set; }
}

// ── Region node ───────────────────────────────────────────────────────────────

public sealed class RegionNodeDto
{
    public Guid StableId { get; set; }
    public byte RegionIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Priority { get; set; }
    /// <summary>StableId of the initial child state in this region; null when none.</summary>
    public Guid? InitialChildStableId { get; set; }

    /// <summary>
    /// ⭐⭐ <c>BP-299</c>: the StableId of the parallel composite that OWNS this region.
    ///
    /// <para>
    /// 🔴 <b>Why it exists.</b> Ownership used to be re-derived on load from
    /// <c>InitialChild.Parent</c> (RHS-05) — the flat JSON region list carried no parent reference.
    /// ⛔ A region with <b>no</b> initial child therefore had no owner, its composite came back with
    /// zero regions, and validator rules 8 and 8b <b>skipped that composite silently</b>: no
    /// diagnostic, asset validates clean.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Nullable, and the derivation stays as the FALLBACK</b> — an asset saved before this field
    /// existed must still load. ⛔ Making it required would break every shipped asset to fix a case
    /// none of them hit.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OwnerStableId { get; set; }
    public string? Comment { get; set; }
    public string? ColorOverride { get; set; }
}

// ── Transition node ───────────────────────────────────────────────────────────

public sealed class TransitionNodeDto
{
    /// <summary>Primary editor identity (stable if layout method was present).</summary>
    public Guid VisualId { get; set; }
    public Guid SourceStableId { get; set; }
    public Guid TargetStableId { get; set; }

    // Event (by name — EventId is runtime-only)
    public string? EventName { get; set; }

    public string? GuardFunction { get; set; }
    public string? ActionFunction { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpressionTargetField { get; set; }
    public byte Priority { get; set; }
    public TransitionKindDto Kind { get; set; }
    public ushort SyncGroupId { get; set; }

    // Layout
    public List<WaypointDto> Waypoints { get; set; } = new();
    public string? Comment { get; set; }
}

// ── Global transition ─────────────────────────────────────────────────────────

public sealed class GlobalTransitionNodeDto
{
    public Guid VisualId { get; set; }
    public Guid TargetStableId { get; set; }
    public string? EventName { get; set; }
    public string? GuardFunction { get; set; }
    public string? ActionFunction { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpressionTargetField { get; set; }
    public byte Priority { get; set; }
    public string? Comment { get; set; }
}

// ── Event definition ──────────────────────────────────────────────────────────

public sealed class EventDefinitionDto
{
    /// <summary>Canonical identity.</summary>
    public string Name { get; set; } = string.Empty;
    public int PayloadSize { get; set; }
    public bool IsIndirect { get; set; }
    /// <summary>True when at least one state defers this event (from StateNode.DeferredEventIds).</summary>
    public bool IsDeferrable { get; set; }
    /// <summary>
    /// The EventId assigned by HsmBuilder at compile time.
    /// Needed by the emit core to reproduce the original builder.Event(..., eventId, ...) call
    /// byte-identically. Under JSON-SoT (PU-02+) this will be replaced by sequential reassignment
    /// in the generator; for now it is preserved for emit-core byte-identity.
    /// </summary>
    public ushort EventId { get; set; }
}

// ── Blackboard block (§5.4) ───────────────────────────────────────────────────

/// <summary>Array- and default-capable type reference (§5.4).</summary>
public sealed class HsmBlackboardTypeRefDto
{
    public string TypeId { get; set; } = string.Empty;
    public bool IsArray { get; set; }
    public int? FixedLength { get; set; }
}

public sealed class HsmBlackboardVariableDto
{
    public string Name { get; set; } = string.Empty;
    public HsmBlackboardTypeRefDto Type { get; set; } = new();
    /// <summary>JSON-encoded default value; null = no default authored (omitted from JSON for byte-stability).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultValueJson { get; set; }
    public string? Comment { get; set; }
    /// <summary>
    /// True when this variable was auto-created by the "Promote to new variable" feature.
    /// Omitted from JSON when false (default) for backwards compatibility.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAutoManaged { get; set; }
    /// <summary>
    /// Authoring role: Input (default) or State. Omitted from JSON when Input (default).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BlackboardVariableRole Role { get; set; }
    /// <summary>
    /// Working-state scope (only meaningful when Role == State). Omitted from JSON when Node (default).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WorkingStateScope Scope { get; set; }
}

public sealed class HsmBlackboardBlockDto
{
    public bool Managed { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string? HeavyDtoType { get; set; }
    public List<HsmBlackboardVariableDto> Variables { get; set; } = new();
}

// ── Canvas ────────────────────────────────────────────────────────────────────

public sealed class HsmCanvasDto
{
    public float PanX { get; set; }
    public float PanY { get; set; }
    public float Zoom { get; set; } = 1.0f;
}

// ── Suppressions ─────────────────────────────────────────────────────────────

public sealed class HsmConflictSuppressionDto
{
    public string VariableName { get; set; } = string.Empty;
    public string WriterPairKey { get; set; } = string.Empty;
}

public sealed class HsmSuppressionsDto
{
    public List<HsmConflictSuppressionDto> Conflict { get; set; } = new();
    public List<string> Unused { get; set; } = new();

    /// <summary>
    /// ⭐⭐⭐ <c>W7b</c> (§9.4) — variables whose CROSS-REGION CONCURRENT WRITES the designer has
    /// explicitly allowed. ⛔⛔ <b>NOT the same thing as <see cref="Conflict"/>.</b> A conflict
    /// suppression is per <b>(variable, writer-PAIR)</b> — §9.3: <i>"a new aliasing relationship on the
    /// same variable would surface a fresh diagnostic"</i>. ⭐ This is per <b>VARIABLE</b>: the designer
    /// has said the race on it is intended, so every pair on it is allowed, including pairs that do not
    /// exist yet. ⇒ <b>two mechanisms, deliberately, and collapsing them would silently widen one of
    /// them.</b>
    /// </summary>
    /// ⚠⚠ <b>NULLABLE and omitted when empty, UNLIKE its two neighbours</b> — and that asymmetry is
    /// deliberate. <c>Conflict</c> and <c>Unused</c> have always serialised as <c>[]</c>, so their
    /// presence is baked into every stored document. ⛔ A new ALWAYS-EMITTED list changes the bytes of
    /// every asset that has no allowance at all — caught by <c>MigrationEquivalenceTests</c>, which
    /// round-trips stored JSON and compares it verbatim. ⇒ <c>WhenWritingNull</c> keeps the existing
    /// corpus byte-identical while still persisting the flag for anyone who sets it.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? ConcurrentWritesAllowed { get; set; }
}

// ── Root DTO ──────────────────────────────────────────────────────────────────

/// <summary>
/// Persisted representation of an HSM asset. Serialized to *.hsm.json.
/// Design §5.2/§5.4.
/// Runtime-only excluded: Blob/Metadata, FlatIndex, *PinId,
/// _aliases hydration, LoadDiagnosticMessage, IsDirty, Changed, IsBreakpoint.
/// </summary>
public sealed class HsmAssetDto
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public Guid AssetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TargetNamespace { get; set; } = string.Empty;
    public string BlackboardTypeName { get; set; } = string.Empty;

    // ── Topology ──────────────────────────────────────────────────────────────
    public List<StateNodeDto> States { get; set; } = new();
    public List<RegionNodeDto> Regions { get; set; } = new();
    public List<TransitionNodeDto> Transitions { get; set; } = new();
    public List<GlobalTransitionNodeDto> GlobalTransitions { get; set; } = new();
    public List<EventDefinitionDto> Events { get; set; } = new();

    // ── Canvas layout ─────────────────────────────────────────────────────────
    public HsmCanvasDto Canvas { get; set; } = new();

    // ── Suppressions (§5.2) ───────────────────────────────────────────────────
    public HsmSuppressionsDto Suppressions { get; set; } = new();

    // ── Approach-A alias bindings (§7; persistence design :132) ─────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 91 (<c>91b</c>) — alias relationships, keyed by VARIABLE NAME.</b>
    /// 📄 <c>…Persistence_Detailed_Design.md:132</c> lists these beside the sync bindings and the
    /// suppressions; 🔴 they were the one item of the three never built, so every authored alias was
    /// lost on reload.
    ///
    /// <para>⚠⚠ <b>NULLABLE and omitted when empty — and the reason is the GOLDEN.</b> 📌 The same
    /// rule <c>ConcurrentWritesAllowed</c> states above: <i>"a new ALWAYS-EMITTED list changes the
    /// bytes of every asset"</i>, and <c>MigrationEquivalenceTests</c> round-trips stored JSON
    /// verbatim. ⭐ <b>No shipped asset can contain an alias</b> *(they never persisted)*, so with
    /// <c>WhenWritingNull</c> the field is absent everywhere and the whole corpus stays
    /// byte-identical.</para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<Hrot.AiEditor.Persistence.BlackboardAliasBindingDto>>? Aliases { get; set; }


    // ── Blackboard (§5.4) ─────────────────────────────────────────────────────
    public HsmBlackboardBlockDto Blackboard { get; set; } = new();
}
