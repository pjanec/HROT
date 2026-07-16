using Fdp.Toolkit.Navigation;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>Hill-attack blueprint-migration P2 pure comparator helper. GAP-12 stopgap: blueprints
    /// have no native comparison node yet, so the enum-equality check that turns a GetComponent field
    /// read into a bool condition lives in this tiny pure (contextless) FunctionCall target. When the
    /// native Compare node lands, this helper is deleted and the condition becomes fully visual.</summary>
    public static class HillAssault2NavOps
    {
        /// <summary>True when a unit's NavigationStatus.Result indicates it has arrived at its destination.</summary>
        public static bool IsArrived(NavigationResult result) => result == NavigationResult.Arrived;
    }
}
