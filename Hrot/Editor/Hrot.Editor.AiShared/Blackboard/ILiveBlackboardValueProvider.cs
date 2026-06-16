using System.Collections.Generic;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Seam for reading live blackboard variable values from the selected entity at runtime.
/// Injected optionally into <see cref="Hrot.Editor.AiShared.Windows.BlackboardAuthoringWindow"/>.
/// </summary>
public interface ILiveBlackboardValueProvider
{
    /// <summary>
    /// Returns a map of variable name → formatted live value for the asset's authored variables,
    /// ONLY when an entity is selected AND it is currently running this asset's behavior
    /// (name-match gate: <c>BehaviorRegistry.TryGetId(asset.Name)</c> == <c>BehaviorState.ActiveBehaviorHash</c>).
    /// Returns an empty map otherwise (no selection, behavior mismatch, or no live world).
    /// Never throws.
    /// </summary>
    IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset);
}
