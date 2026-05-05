using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// Startup-time registry of behavior-gizmo factories, keyed by behavior name.
    /// Not thread-safe; must only be populated during application initialisation.
    /// </summary>
    public sealed class BehaviorGizmoRegistry
    {
        private readonly Dictionary<string, IBehaviorGizmoFactory> _factories =
            new Dictionary<string, IBehaviorGizmoFactory>(StringComparer.Ordinal);

        /// <summary>
        /// Registers a factory, overwriting any existing entry for the same behavior name.
        /// </summary>
        public void Register(IBehaviorGizmoFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _factories[factory.BehaviorName] = factory;
        }

        /// <summary>
        /// Looks up a factory by behavior name.
        /// Returns <c>false</c> (without throwing) when the name is unknown.
        /// </summary>
        public bool TryGetFactory(string behaviorName, out IBehaviorGizmoFactory factory)
        {
            return _factories.TryGetValue(behaviorName, out factory!);
        }
    }
}
