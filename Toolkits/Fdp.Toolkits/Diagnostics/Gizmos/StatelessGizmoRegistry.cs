using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A compiled stateless gizmo rule: the projector, its pre-computed component
    /// bitmask, and the visibility policy. Internal so that only the gizmo systems
    /// (and tests via InternalsVisibleTo) can read it.
    /// </summary>
    internal struct CompiledStatelessRule
    {
        public IStatelessGizmo Projector;
        public BitMask256 RequiredMask;
        public IGizmoVisibilityPolicy VisibilityPolicy;
        /// <summary>Position of this rule in the registry; indexes the per-frame
        /// global-visibility cache.</summary>
        public int RuleIndex;
    }

    /// <summary>
    /// Startup-time registry of all stateless gizmo projectors.
    /// <see cref="Register"/> must only be called during application initialisation,
    /// before the first ECS frame tick. Not thread-safe.
    /// </summary>
    public sealed class StatelessGizmoRegistry
    {
        private readonly List<CompiledStatelessRule> _rules = new List<CompiledStatelessRule>();

        /// <summary>Read-only view of all compiled rules, in registration order.
        /// Internal: consumed by <see cref="Systems.StatelessGizmoSystem"/> in the same assembly.</summary>
        internal IReadOnlyList<CompiledStatelessRule> Rules => _rules;

        /// <summary>
        /// Compiles a stateless projector and adds it to the registry.
        /// Each required component type must have been registered with the
        /// <see cref="EntityRepository"/> before this call.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="projector"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// A required component type has no registered ID (i.e. <c>ComponentTypeRegistry.GetId</c>
        /// returns -1). The exception message names the offending type.
        /// </exception>
        public void Register(
            IStatelessGizmo projector,
            Type[] requiredComponents,
            IGizmoVisibilityPolicy? visibilityPolicy = null)
        {
            if (projector == null) throw new ArgumentNullException(nameof(projector));
            if (requiredComponents == null) throw new ArgumentNullException(nameof(requiredComponents));

            var mask = default(BitMask256);

            foreach (var type in requiredComponents)
            {
                int id = ComponentTypeRegistry.GetId(type);
                if (id == -1)
                    throw new InvalidOperationException(
                        $"StatelessGizmoRegistry.Register: required component type '{type.Name}' is not " +
                        $"registered in ComponentTypeRegistry. Call repo.RegisterComponent<{type.Name}>() " +
                        $"before registering this projector.");

                mask.SetBit(id);
            }

            _rules.Add(new CompiledStatelessRule
            {
                Projector        = projector,
                RequiredMask     = mask,
                VisibilityPolicy = visibilityPolicy ?? AlwaysVisiblePolicy.Instance,
                RuleIndex        = _rules.Count,
            });
        }
    }
}
