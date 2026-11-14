using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.TacticalOrderMapper
{
    /// <summary>
    /// ⭐ <c>CE-3167</c> — one per-unit store an order may clear before it starts (<c>docs/UX/UX_Feature_Entity_Commanding.md</c>
    /// §2.5d, ruling 52): the order's <c>ClearsPriorIntent</c> flag does not KNOW which planners exist; each registers here.
    /// <para>🔒 The contract: <see cref="ClearFor"/> is synchronous and idempotent, runs on the brain owner only (after the
    /// authority gate), and clears INTENT — never perception (<c>TargetMemory</c>), the muscle (nav status, physics,
    /// <c>SimTransform</c>) or the behaviour slot itself (the assign that follows replaces it).</para>
    /// </summary>
    public interface IIntentClearProvider
    {
        /// <summary>For diagnostics — which providers ran.</summary>
        string Name { get; }

        /// <summary>Clear this provider's plan for <paramref name="entity"/>; a no-op when there is none.</summary>
        void ClearFor(Entity entity, EntityRepository repo);
    }

    /// <summary>
    /// ⭐ <c>CE-3167</c> — the providers an order with <c>ClearsPriorIntent</c> runs, mirroring
    /// <see cref="TacticalIntentMapperRegistry"/>: filled by the composition root before frame 1, read-only after. An empty
    /// registry clears nothing (a host without a planner degrades safely).
    /// </summary>
    public sealed class IntentClearRegistry
    {
        private readonly List<IIntentClearProvider> _providers = new();

        /// <summary>Registers <paramref name="provider"/>; registering the same instance twice is refused.</summary>
        public void Register(IIntentClearProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (_providers.Contains(provider))
                throw new InvalidOperationException($"Intent clear provider '{provider.Name}' is already registered.");
            _providers.Add(provider);
        }

        /// <summary>The registered providers, in registration order.</summary>
        public IReadOnlyList<IIntentClearProvider> Providers => _providers;

        /// <summary>Runs every provider for <paramref name="entity"/>, in registration order.</summary>
        public void ClearFor(Entity entity, EntityRepository repo)
        {
            foreach (var p in _providers) p.ClearFor(entity, repo);
        }
    }
}
