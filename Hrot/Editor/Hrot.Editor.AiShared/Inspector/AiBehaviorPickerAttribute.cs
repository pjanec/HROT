using System;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐ <c>CE-2079</c> — marks a facet string field that names a REGISTERED BEHAVIOUR of any tier (BTree, HSM, blueprint,
/// hand-written) — the names the runtime resolves an assignment by. ⚠ Distinct from <see cref="AiAssetPickerAttribute"/>,
/// which lists catalogue ASSETS of one kind: an SOP order may start a hand-written behaviour that has no asset.
/// Drawn by <see cref="AiBehaviorPickerDrawer"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AiBehaviorPickerAttribute : Attribute { }
