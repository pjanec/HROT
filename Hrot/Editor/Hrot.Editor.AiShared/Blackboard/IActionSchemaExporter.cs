using System;
using System.Collections.Generic;
using System.Reflection;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Describes a single public instance field of a DTO struct, as reflected by
/// <see cref="ActionSchemaExporter"/>.
/// </summary>
public record DtoFieldDescriptor(string Name, Type FieldType);

/// <summary>
/// Indicates which AI systems can host a given action method.
/// A single method may be registered for multiple hosting contexts (combine with |).
/// </summary>
[Flags]
public enum ActionHosting
{
    None   = 0,
    BTree  = 1 << 0,

    /// <summary>Hostable by the HSM in SOME role. ⚠ Says nothing about WHICH role — see
    /// <see cref="HsmActivity"/> / <see cref="HsmGuard"/>, which are set alongside it.</summary>
    Hsm    = 1 << 1,

    Shared = 1 << 2,
    // ⛔ 1 << 3 is the DELETED `Heavy` bit (CE-330) — deliberately not reused, see the note below.

    /// <summary>
    /// ⭐⭐⭐ <c>CE-386</c> — usable as an HSM TRANSITION GUARD (<c>[HsmGuard]</c>,
    /// <c>[SharedAiCondition]</c>, or a blueprint declaring <c>hsmGuard</c>).
    ///
    /// <para>🔴 <b>Why this bit had to exist.</b> Until now <c>HsmAction</c> and <c>HsmGuard</c> were
    /// BOTH folded into <see cref="Hsm"/> — the exporter read the two attribute flags and then
    /// discarded the distinction — so nothing downstream could tell an HSM activity from an HSM
    /// guard, and the two HSM pickers could not be filtered apart.</para>
    ///
    /// <para>⛔ <b>Why not reuse <c>IsCondition</c>, which <c>SharedAiCondition</c> already sets.</b>
    /// 📐 <c>BTreeNodeCatalog.cs:104</c> turns <c>IsCondition</c> into the PERSISTED node kind
    /// (<c>bt/condition</c> vs <c>bt/action</c>). ⇒ a blueprint declaring both <c>bTreeAction</c> and
    /// <c>hsmGuard</c> would silently flip from an action leaf to a condition leaf. ⚠ Two vocabularies
    /// that happen to overlap are not one vocabulary.</para>
    ///
    /// <para>⭐ <b>Purely additive:</b> <see cref="Hsm"/> keeps its old meaning and is still set, so
    /// the one production reader (<c>BehaviorActionCatalog.MapHosting</c>) is untouched.</para>
    /// </summary>
    HsmGuard = 1 << 4,

    /// <summary>⭐ <c>CE-386</c> — usable as an HSM STATE ACTIVITY / entry / exit / timer action
    /// (<c>[HsmAction]</c>, <c>[SharedAiAction]</c>, or a blueprint declaring <c>hsmAction</c>).
    /// ⚠ A method may carry BOTH this and <see cref="HsmGuard"/>; the attribute allows it, so the
    /// schema must be able to say it rather than force a choice.</summary>
    HsmActivity = 1 << 5,
    // ⛔ CE-330 (2026-09-23): `Heavy = 1 << 3` is DELETED. It marked an action carrying a heavy
    //   DTO parameter — i.e. one using [SharedAiHeavyAction], which CE-327 removed (§30.29).
    //   📐 It had no code reader even before that: only a doc-comment in BehaviorActionCatalog
    //   noting it was "a modifier, not a host". ⚠ The bit is NOT reused — nothing persists this
    //   enum (it is rebuilt by reflection each Rebuild()), but leaving the gap costs nothing and
    //   a reused bit would silently re-interpret any stale value that did survive somewhere.
}

/// <summary>
/// Records how an action method accesses its first ref (blackboard DTO) parameter.
/// </summary>
public enum BlackboardAccess
{
    Unknown   = 0,   // unannotated -- treated as ReadWrite by caller
    ReadOnly,
    ReadWrite,
}

/// <summary>
/// Describes a single reflected action/condition/guard entry in the schema.
/// </summary>
/// <param name="Fqn">
/// Fully-qualified name in "{DeclaringType.FullName}.{MethodName}" format.
/// </param>
/// <param name="DtoType">Type of the first <c>ref</c> parameter of the method.</param>
/// <param name="Hosting">Set of AI systems that can host this action.</param>
/// <param name="Access">
/// Blackboard access annotation read from <c>[BlackboardReadOnly]</c> or
/// <c>[BlackboardReadWrite]</c> on the first parameter; defaults to <c>Unknown</c>.
/// </param>
/// <param name="IsCondition">
/// True when the method was registered via a condition-declaring attribute
/// (<c>[BTreeCondition]</c>, <c>[SharedAiCondition]</c>);
/// false for actions and guards. Defaults to false for backward compatibility.
/// </param>
/// <param name="DtoFields">
/// Public instance fields of <see cref="DtoType"/>, enumerated in declaration order.
/// Used by the Variables panel to show hardcoded DTO fields as read-only rows.
/// </param>
/// <param name="IsAiPrimitive">
/// True when this entry was discovered from a Blueprint-compiled AiPrimitive's generated
/// <c>TickCore</c> (via <c>[Fbt.Kernel.GeneratedAiPrimitiveAction]</c>) rather than a hand-written
/// <c>[BTreeAction]</c>/<c>[SharedAiAction]</c> method. Lets downstream catalogs/UI present
/// blueprint-authored actions distinctly. Defaults to false for backward compatibility.
/// </param>
public record ActionSchemaEntry(
    string Fqn,
    Type DtoType,
    ActionHosting Hosting,
    BlackboardAccess Access,
    bool IsCondition = false,
    IReadOnlyList<DtoFieldDescriptor>? DtoFields = null,
    bool IsAiPrimitive = false,
    Type? WorkingStateType = null
)
{
    // ⭐ CE-2099 — WorkingStateType: the method's SECOND `ref` parameter, the stateful form `(ref P, ref WS, Entity,
    //   EntityRepository)` (an action or, since CE-2069, a condition); null for every other form. The inspector binds it.
}

/// <summary>
/// Provides a dictionary of all reflected action/condition/guard entries keyed by FQN.
/// </summary>
public interface IActionSchemaExporter
{
    /// <summary>All known entries, keyed by FQN.</summary>
    IReadOnlyDictionary<string, ActionSchemaEntry> All { get; }

    /// <summary>Returns the entry for <paramref name="fqn"/>, or null if not found.</summary>
    ActionSchemaEntry? Lookup(string fqn);

    /// <summary>
    /// Rescans all loaded assemblies and repopulates <see cref="All"/>.
    /// Raises <see cref="Changed"/> after the update.
    /// </summary>
    void Rebuild();

    /// <summary>Raised after every successful <see cref="Rebuild"/> call.</summary>
    event Action? Changed;
}
