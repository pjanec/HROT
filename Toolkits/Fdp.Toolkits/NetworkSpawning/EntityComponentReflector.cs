using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Fdp.Kernel;

namespace Fdp.Toolkit.NetworkSpawning
{
    /// <summary>
    /// Utility to set components on an entity when the component type is only known at runtime.
    /// Used by NetworkSpawningSystem to apply the List&lt;object&gt; of initial components.
    ///
    /// <para>
    /// Each distinct component type incurs a one-time cost on the first call: a strongly-typed
    /// <c>Action&lt;EntityRepository, Entity, object&gt;</c> delegate is compiled via
    /// <see cref="System.Linq.Expressions"/> and cached.  All subsequent calls for the same
    /// type invoke the cached delegate directly — no <c>new object[]</c> allocation and no
    /// <see cref="MethodBase.Invoke"/> overhead on the hot path.
    /// </para>
    ///
    /// <para>
    /// The compiled delegate calls <c>EntityRepository.SetComponent&lt;T&gt;(Entity, T)</c>,
    /// which internally dispatches to the correct (unmanaged or managed) storage path based on
    /// whether <c>T</c> is a struct or a class.
    /// </para>
    /// </summary>
    public static class EntityComponentReflector
    {
        // Compiled setter delegates, keyed by concrete component type.
        private static readonly ConcurrentDictionary<Type, Action<EntityRepository, Entity, object>>
            _setterCache = new();

        // Cached MethodInfo for EntityRepository.SetComponent<T>(Entity, T) generic definition.
        private static readonly MethodInfo _genericSetComponent = FindSetComponentMethod();

        private static MethodInfo FindSetComponentMethod()
        {
            // Look for the SetComponent<T>(Entity entity, T component) overload.
            foreach (var m in typeof(EntityRepository).GetMethods(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "SetComponent" || !m.IsGenericMethodDefinition) continue;

                var parms = m.GetParameters();
                if (parms.Length == 2 && parms[0].ParameterType == typeof(Entity))
                    return m;
            }

            throw new InvalidOperationException(
                "Could not locate SetComponent<T>(Entity, T) on EntityRepository.");
        }

        /// <summary>
        /// Sets a component on an entity using a cached compiled delegate to avoid
        /// per-call reflection overhead and <c>object[]</c> allocations.
        /// </summary>
        /// <param name="world">The entity repository.</param>
        /// <param name="entity">The target entity.</param>
        /// <param name="component">The component instance. If <c>null</c>, does nothing.</param>
        public static void SetComponent(EntityRepository world, Entity entity, object component)
        {
            if (component == null) return;
            if (world == null) throw new ArgumentNullException(nameof(world));

            var setter = _setterCache.GetOrAdd(component.GetType(), BuildSetter);
            setter(world, entity, component);
        }

        // ── Delegate compilation ─────────────────────────────────────────────

        private static Action<EntityRepository, Entity, object> BuildSetter(Type componentType)
        {
            // Parameters for the lambda: (EntityRepository world, Entity entity, object component)
            var repoParam   = Expression.Parameter(typeof(EntityRepository), "world");
            var entityParam = Expression.Parameter(typeof(Entity), "entity");
            var objParam    = Expression.Parameter(typeof(object), "component");

            // Cast the boxed object parameter to the concrete component type.
            var castComp = Expression.Convert(objParam, componentType);

            // world.SetComponent<ComponentType>(entity, (ComponentType)component)
            var concreteMethod = _genericSetComponent.MakeGenericMethod(componentType);
            var call = Expression.Call(repoParam, concreteMethod, entityParam, castComp);

            return Expression
                .Lambda<Action<EntityRepository, Entity, object>>(
                    call, repoParam, entityParam, objParam)
                .Compile();
        }
    }
}
