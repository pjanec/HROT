using System;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// A single entry in the Add-Variable type dropdown: the short display string shown to the
/// designer (see <see cref="BlackboardTypeHelper.GetDisplayName"/>) paired with the exact CLR
/// <see cref="Type"/> it resolves to.
/// <para>
/// Resolution from a chosen combo entry back to a <see cref="Type"/> MUST go through the
/// combo's selected INDEX into the ordered choice list that produced it -- never a reverse
/// lookup from the display string -- because two structs declared in different namespaces can
/// share the same short <c>Type.Name</c> (e.g. two distinct <c>Foo</c> DTOs). Indexing into the
/// same list that built the combo is the only collision-safe resolution.
/// </para>
/// </summary>
public readonly record struct VariableTypeChoice(string Display, Type Type);
