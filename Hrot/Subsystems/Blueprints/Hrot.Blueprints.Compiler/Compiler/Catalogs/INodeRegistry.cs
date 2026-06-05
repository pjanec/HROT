using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Canonical, ordered, GUID-less pin schema for a node's static structure.
/// Dynamic kinds (EventEntry/Return/variable/FunctionCall) return what is statically known
/// (often empty/exec-only); the rehydration pass enriches them from authored state.
/// </summary>
public readonly record struct PinSchema(string Name, string Direction, bool IsExec, string TypeId);

public interface INodeRegistry
{
    /// <summary>
    /// Returns the canonical ordered static pin shapes for a node.
    /// Pin order is load-bearing for the link-GUID positional assignment in Stage0_Rehydrate.
    /// </summary>
    IReadOnlyList<PinSchema> GetStaticPins(Node node);
}
