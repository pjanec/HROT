using System;
using System.Collections.Concurrent;
using System.Reflection;
using Fdp.Kernel;

namespace FDP.Toolkit.NetworkSpawning
{
    /// <summary>
    /// Utility to set components on an entity when the component type is only known at runtime.
    /// Used by NetworkSpawningSystem to apply the List&lt;object&gt; of initial components.
    /// </summary>
    public static class EntityComponentReflector
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo> _setComponentCache = new();

        /// <summary>
        /// Sets a component on an entity using reflection to find the correct generic SetComponent&lt;T&gt; method.
        /// </summary>
        /// <param name="world">The entity repository.</param>
        /// <param name="entity">The target entity.</param>
        /// <param name="component">The component instance. If null, does nothing.</param>
        public static void SetComponent(EntityRepository world, Entity entity, object component)
        {
            if (component == null) return;
            if (world == null) throw new ArgumentNullException(nameof(world));

            var type = component.GetType();
            
            var method = _setComponentCache.GetOrAdd(type, t =>
            {
                var genericMethod = typeof(EntityRepository).GetMethod("SetComponent", new[] { typeof(Entity), Type.MakeGenericMethodParameter(0) });
                
                // Fallback search if the signature above doesn't match exactly (e.g. constraints)
                if (genericMethod == null)
                {
                    // Search all methods named SetComponent
                    foreach (var m in typeof(EntityRepository).GetMethods())
                    {
                        if (m.Name == "SetComponent" && m.IsGenericMethodDefinition)
                        {
                            var parameters = m.GetParameters();
                            if (parameters.Length == 2 && 
                                parameters[0].ParameterType == typeof(Entity))
                            {
                                genericMethod = m;
                                break;
                            }
                        }
                    }
                }

                if (genericMethod == null)
                    throw new InvalidOperationException($"Could not find SetComponent<T> on EntityRepository.");

                return genericMethod.MakeGenericMethod(t);
            });

            try
            {
                method.Invoke(world, new object[] { entity, component });
            }
            catch (TargetInvocationException ex)
            {
                // Unwrap the exception to make stack traces cleaner
                if (ex.InnerException != null)
                    throw ex.InnerException;
                throw;
            }
        }
    }
}
