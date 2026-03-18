using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// Fluent API for building EntityQuery instances.
    /// Supports WithAll, WithAny, Without for component filtering.
    /// </summary>
    public sealed class QueryBuilder
    {
        private readonly EntityRepository _repository;
        private BitMask256 _includeMask;
        private BitMask256 _excludeMask;
        private BitMask256 _authorityIncludeMask;
        private BitMask256 _authorityExcludeMask;
        
        private bool _hasDisFilter;
        private ulong _disFilterValue;
        private ulong _disFilterMask;
        
        internal QueryBuilder(EntityRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _includeMask = new BitMask256();
            _excludeMask = new BitMask256();
            _authorityIncludeMask = new BitMask256();
            _authorityExcludeMask = new BitMask256();
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
        /// Raw component-type identifier in the range [0, 255].
        /// Out-of-range values are silently ignored.
        /// </param>
        public QueryBuilder WithComponentId(int componentId)
        {
            if (componentId >= 0 && componentId < 256)
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
        public EntityQuery Build()
        {
            return new EntityQuery(_repository, _includeMask, _excludeMask, _authorityIncludeMask, _authorityExcludeMask, _hasDisFilter, _disFilterValue, _disFilterMask, _lifecycleFilter);
        }
    }
}
