using System;

namespace Fdp.Core
{
    /// <summary>
    /// Fluent API for building EntityQuery instances.
    /// Supports WithAll, WithAny, Without for component filtering.
    /// </summary>
    public sealed class QueryBuilder
    {
        private readonly EntityRepository _repository;
        private BitMask512 _includeMask;
        private BitMask512 _excludeMask;
        private BitMask512 _authorityIncludeMask;
        private BitMask512 _authorityExcludeMask;
        
        private bool _hasDisFilter;
        private ulong _disFilterValue;
        private ulong _disFilterMask;
        
        // ⭐ CE-2400 (R-2084) — the repository's one reusable builder is RENTED by Query() and RETURNED by Build(); a nested or concurrent
        //   Query() gets a fresh one. ⚠ So a builder must not be mutated after its Build() — build again from a new Query().
        internal readonly bool Pooled;

        internal QueryBuilder(EntityRepository repository, bool pooled) : this(repository) { Pooled = pooled; }

        /// <summary>Back to the state <see cref="EntityRepository.Query"/> hands out (the pooled builder only).</summary>
        internal void Reset()
        {
            _includeMask = default; _excludeMask = default; _authorityIncludeMask = default; _authorityExcludeMask = default;
            _hasDisFilter = false; _disFilterValue = 0; _disFilterMask = 0; _lifecycleFilter = EntityLifecycle.Active;
        }

        internal QueryBuilder(EntityRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _includeMask = new BitMask512();
            _excludeMask = new BitMask512();
            _authorityIncludeMask = new BitMask512();
            _authorityExcludeMask = new BitMask512();
        }
        
        /// <summary>
        /// Requires entity to have component T.
        /// Can be chained multiple times for AND logic.
        /// </summary>
        public QueryBuilder With<T>() where T : unmanaged
        {
            _includeMask.SetBit(ComponentType<T>.ID);
            return this;
        }
        
        /// <summary>
        /// Requires entity to NOT have component T.
        /// Can be chained multiple times.
        /// </summary>
        public QueryBuilder Without<T>() where T : unmanaged
        {
            _excludeMask.SetBit(ComponentType<T>.ID);
            return this;
        }

        /// <summary>
        /// Requires entity to have the component registered with the specified raw
        /// <paramref name="componentId"/> (a <see cref="GlobalComponentIds"/> constant).
        /// Use when the consumer project cannot directly reference the component's type due
        /// to assembly dependency constraints (e.g. CarKinem filtering on
        /// <c>GlobalComponentIds.PhysicsCollider</c> without depending on
        /// <c>FDP.Toolkit.Physics</c>).
        /// </summary>
        /// <param name="componentId">
        /// Raw component-type identifier in the range [0, 511].
        /// Out-of-range values are silently ignored.
        /// </param>
        public QueryBuilder WithComponentId(int componentId)
        {
            if (componentId >= 0 && componentId < FdpConfig.MAX_COMPONENT_TYPES)
                _includeMask.SetBit(componentId);
            return this;
        }

        /// <summary>
        /// Requires entity to have managed component T.
        /// Can be chained multiple times for AND logic.
        /// </summary>
        public QueryBuilder WithManaged<T>() where T : class
        {
            _includeMask.SetBit(ManagedComponentType<T>.ID);
            return this;
        }

        /// <summary>
        /// Requires entity to NOT have managed component T.
        /// Can be chained multiple times.
        /// </summary>
        public QueryBuilder WithoutManaged<T>() where T : class
        {
            _excludeMask.SetBit(ManagedComponentType<T>.ID);
            return this;
        }
        
        /// <summary>
        /// Requires entity to have Local Authority over component T.
        /// Implicitly requires With&lt;T&gt;().
        /// </summary>
        public QueryBuilder WithOwned<T>() where T : unmanaged
        {
            // First, require the component itself
            With<T>();
            _authorityIncludeMask.SetBit(ComponentType<T>.ID);
            return this;
        }

        /// <summary>
        /// Requires entity to NOT have Local Authority over component T.
        /// Does not affect component presence (can have T but remote).
        /// </summary>
        public QueryBuilder WithoutOwned<T>() where T : unmanaged
        {
            _authorityExcludeMask.SetBit(ComponentType<T>.ID);
            return this;
        }
        
        /// <summary>
        /// Filters entities by specific DIS Entity Type.
        /// Uses high-performance bitwise masking.
        /// </summary>
        /// <param name="type">The target value(s) for the fields you care about.</param>
        /// <param name="mask">Bitmask indicating which bytes to compare (0x00 ignores, 0xFF compares).</param>
        public QueryBuilder WithDisType(DISEntityType type, ulong mask)
        {
            _hasDisFilter = true;
            _disFilterValue = type.Value;
            _disFilterMask = mask;
            return this;
        }

        private EntityLifecycle _lifecycleFilter = EntityLifecycle.Active;

        /// <summary>
        /// Filter query to specific lifecycle state.
        /// Default: Active (excludes Constructing and TearDown).
        /// </summary>
        public QueryBuilder WithLifecycle(EntityLifecycle state)
        {
            _lifecycleFilter = state;
            return this;
        }
        
        /// <summary>
        /// Include entities currently being constructed.
        /// Use when module needs to setup staging entities.
        /// </summary>
        public QueryBuilder IncludeConstructing()
        {
            _lifecycleFilter = EntityLifecycle.Constructing;
            return this;
        }
        
        /// <summary>
        /// Include entities in teardown phase.
        /// Use when module needs to cleanup before destruction.
        /// </summary>
        public QueryBuilder IncludeTearDown()
        {
            _lifecycleFilter = EntityLifecycle.TearDown;
            return this;
        }
        
        /// <summary>
        /// Include all entities regardless of lifecycle state.
        /// </summary>
        public QueryBuilder IncludeAll()
        {
            _lifecycleFilter = EntityLifecycle.All; // Special value
            return this;
        }
        
        /// <summary>
        /// Builds the EntityQuery.
        /// Query is immutable after build.
        /// </summary>
        /// <remarks>⭐ CE-2400 (R-2084) — an <see cref="EntityQuery"/> is immutable, so the repository keeps the one it built for each distinct
        /// filter and hands it out again: a system that builds its query every frame (most do) allocates nothing. The same filter on the same
        /// repository may therefore return the SAME instance. ⚠ After Build(), do not mutate this builder (it may be handed to the next Query()).</remarks>
        public EntityQuery Build()
        {
            var key = new EntityQuery.Key(_includeMask, _excludeMask, _authorityIncludeMask, _authorityExcludeMask, _hasDisFilter, _disFilterValue, _disFilterMask, _lifecycleFilter);
            var q = _repository.GetOrCreateQuery(in key);
            if (Pooled) _repository.ReturnQueryBuilder(this);
            return q;
        }
    }
}
