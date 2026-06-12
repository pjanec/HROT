using System;
using System.Collections.Generic;
using System.Reflection;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Indicates which AI systems can host a given action method.
/// A single method may be registered for multiple hosting contexts (combine with |).
/// </summary>
[Flags]
public enum ActionHosting
{
    None   = 0,
    BTree  = 1 << 0,
    Hsm    = 1 << 1,
    Shared = 1 << 2,
    Heavy  = 1 << 3,   // action has a heavy DTO parameter
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
/// <param name="HeavyDtoType">
/// Non-null for <c>[SharedAiHeavyAction]</c>/<c>[SharedAiHeavyCondition]</c> with an
/// unmanaged heavy parameter; null otherwise.
/// </param>
/// <param name="IsCondition">
/// True when the method was registered via a condition-declaring attribute
/// (<c>[BTreeCondition]</c>, <c>[SharedAiCondition]</c>, <c>[SharedAiHeavyCondition]</c>);
/// false for actions and guards. Defaults to false for backward compatibility.
/// </param>
public record ActionSchemaEntry(
    string Fqn,
    Type DtoType,
    ActionHosting Hosting,
    BlackboardAccess Access,
    Type? HeavyDtoType,
    bool IsCondition = false
);

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
