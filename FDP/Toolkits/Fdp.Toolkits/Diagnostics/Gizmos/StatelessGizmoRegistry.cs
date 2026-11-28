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
        public BitMask512 RequiredMask;
        public IGizmoVisibilityPolicy VisibilityPolicy;
        /// <summary>Position of this rule in the registry; indexes the per-frame
        /// global-visibility cache.</summary>
        public int RuleIndex;
        /// <summary>⭐ CE-4388 (D-3) — the projector's <see cref="GizmoProjectorAttribute.MaxMetresPerPixel"/> (0 = any zoom).</summary>
        public float MaxMetresPerPixel;
        /// <summary>⭐ CE-8610 — the projector's <see cref="GizmoProjectorAttribute.MinMetresPerPixel"/> (0 = no lower bound).</summary>
        public float MinMetresPerPixel;
        /// <summary>⭐ CE-4388 (D-4) — the projector's <see cref="GizmoProjectorAttribute.DrawsOffScreen"/>.</summary>
        public bool DrawsOffScreen;
        /// <summary>⭐ Simulated devices S1d-2 (R2-10 / R3-13) — the projector's <see cref="GizmoProjectorAttribute.Hud"/>: scoped per entity, never cut, never held.</summary>
        public bool Hud;
    }

    /// <summary>
    /// A compiled global gizmo rule: the projector and the visibility policy.
    /// Global rules run once per frame without any entity context. Internal so that only the
    /// gizmo systems (and tests via InternalsVisibleTo) can read it.
    /// </summary>
    internal struct CompiledGlobalRule
    {
        public IGlobalStatelessGizmo Projector;
        public IGizmoVisibilityPolicy VisibilityPolicy;
    }

    /// <summary>
    /// Startup-time registry of all stateless gizmo projectors.
    /// <see cref="Register"/> must only be called during application initialisation,
    /// before the first ECS frame tick. Not thread-safe.
    /// </summary>
    [Fdp.Core.NotState("compiled rules registered at startup")]
    public sealed class StatelessGizmoRegistry
    {
        private readonly List<CompiledStatelessRule> _rules      = new List<CompiledStatelessRule>();
        private readonly List<CompiledGlobalRule>    _globalRules = new List<CompiledGlobalRule>();

        /// <summary>Read-only view of all compiled rules, in registration order.
        /// Internal: consumed by <see cref="Systems.StatelessGizmoSystem"/> in the same assembly.</summary>
        internal IReadOnlyList<CompiledStatelessRule> Rules => _rules;

        /// <summary>Read-only view of all compiled global rules, in registration order.
        /// Internal: consumed by <see cref="Systems.StatelessGizmoSystem"/> in the same assembly.</summary>
        internal IReadOnlyList<CompiledGlobalRule> GlobalRules => _globalRules;

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

            var mask = default(BitMask512);

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

            var view = ViewSettingsOf(projector);
            // ⭐⭐ Simulated devices S1d-2 (DESIGN_Simulated_Devices §3.6 R2-10 / R3-13) — the HUD family's policy, FIXED here, where every registration
            //   (reflection and by hand) passes: drawn off screen, no zoom band, its own visibility policy (the device TYPE, the operator, the authority —
            //   R7-3) whatever the host's resolver offered. ⛔ A host cannot cull a trainee's display by its map's zoom or its culling setting.
            if (view.Hud)
            {
                view.MaxMetresPerPixel = 0f; view.MinMetresPerPixel = 0f; view.DrawsOffScreen = true;
                if (projector is IGizmoVisibilityPolicy own) visibilityPolicy = own;
            }
            _rules.Add(new CompiledStatelessRule
            {
                Projector        = projector,
                RequiredMask     = mask,
                VisibilityPolicy = visibilityPolicy ?? AlwaysVisiblePolicy.Instance,
                RuleIndex        = _rules.Count,
                // ⭐ CE-4388 — the view filter's per-rule settings, read once from the projector's attribute
                MaxMetresPerPixel = view.MaxMetresPerPixel,
                MinMetresPerPixel = view.MinMetresPerPixel,
                DrawsOffScreen    = view.DrawsOffScreen,
                Hud               = view.Hud,
            });
        }

        private static (float MaxMetresPerPixel, float MinMetresPerPixel, bool DrawsOffScreen, bool Hud) ViewSettingsOf(IStatelessGizmo projector)
        {
            var attr = (GizmoProjectorAttribute?)Attribute.GetCustomAttribute(projector.GetType(), typeof(GizmoProjectorAttribute));
            return attr == null ? (0f, 0f, false, false) : (attr.MaxMetresPerPixel, attr.MinMetresPerPixel, attr.DrawsOffScreen, attr.Hud);
        }

        /// <summary>
        /// Registers a global gizmo projector that runs once per frame with no entity context.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="projector"/> is null.</exception>
        public void RegisterGlobal(
            IGlobalStatelessGizmo projector,
            IGizmoVisibilityPolicy? visibilityPolicy = null)
        {
            if (projector == null) throw new ArgumentNullException(nameof(projector));

            _globalRules.Add(new CompiledGlobalRule
            {
                Projector        = projector,
                VisibilityPolicy = visibilityPolicy ?? AlwaysVisiblePolicy.Instance,
            });
        }
    }
}
