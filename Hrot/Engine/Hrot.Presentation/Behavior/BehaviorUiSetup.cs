using Fdp.Toolkit.Behavior.Params;

namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// Single source of truth for binding CGF behavior-parameter DTOs to their
    /// string behavior IDs in the <see cref="BehaviorUiRegistry"/>.
    ///
    /// <para>Lives in <c>Hrot.Presentation</c> so that any assembly that already
    /// depends on the presentation layer (e.g. <c>Hrot.ExCon</c>) can call
    /// <see cref="CreateRegistry"/> at its composition root without taking a direct
    /// dependency on <c>Hrot.CGF</c>.</para>
    /// </summary>
    public static class BehaviorUiSetup
    {
        /// <summary>
        /// Creates a <see cref="BehaviorUiRegistry"/> pre-registered with all CGF
        /// behavior param DTO types so the mission editor panel can render each
        /// behavior's parameters generically.
        /// </summary>
        public static BehaviorUiRegistry CreateRegistry()
        {
            var registry = new BehaviorUiRegistry();
            registry.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
            registry.Register<FollowRouteParamsJsonDto>("FollowRoute");
            registry.Register<MoveToLocationParamsJsonDto>("MoveToLocation");
            return registry;
        }
    }
}
