using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// A compiled gizmo rule: the definition plus its pre-computed component bitmask
    /// and its stable position index in the registry.
    /// Internal so that only the gizmo systems (and tests via InternalsVisibleTo) can read it.
    /// </summary>
    internal struct CompiledGizmoRule
    {
        public IGizmoDefinition Definition;
        public BitMask256 RequiredMask;
        /// <summary>Position of this rule in the registry's Rules list; used to index into the
        /// per-frame global-visibility cache.</summary>
        public int RuleIndex;
    }

    /// <summary>
    /// Startup-time registry of all entity-bound gizmo definitions.
    /// <see cref="Register"/> must only be called during application initialisation,
    /// before the first ECS frame tick. Not thread-safe.
    /// </summary>
    public sealed class GizmoRegistry
    {
        private readonly List<CompiledGizmoRule> _rules = new List<CompiledGizmoRule>();

        /// <summary>Read-only view of all compiled rules, in registration order.
        /// Internal: consumed by <see cref="Systems.DataDrivenGizmoSystem"/> in the same assembly.</summary>
        internal IReadOnlyList<CompiledGizmoRule> Rules => _rules;

        /// <summary>
        /// Compiles a gizmo definition and adds it to the registry.
        /// Each required component type must have been registered with the
        /// <see cref="EntityRepository"/> (via <c>RegisterComponent&lt;T&gt;()</c>) before this
        /// call, so that <see cref="ComponentTypeRegistry.GetId"/> can resolve its ID.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// A required component type has no registered ID (i.e. <c>GetId</c> returns -1).
        /// The exception message names the offending type.
        /// </exception>
        public void Register(IGizmoDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            var mask = default(BitMask256);

            foreach (var type in definition.RequiredComponents)
            {
                int id = ComponentTypeRegistry.GetId(type);
                if (id == -1)
                    throw new InvalidOperationException(
                        $"GizmoRegistry.Register: required component type '{type.Name}' is not " +
                        $"registered in ComponentTypeRegistry. Call repo.RegisterComponent<{type.Name}>() " +
                        $"before registering this gizmo definition.");

                mask.SetBit(id);
            }

            _rules.Add(new CompiledGizmoRule
            {
                Definition   = definition,
                RequiredMask = mask,
                RuleIndex    = _rules.Count,
            });
        }
    }
}
