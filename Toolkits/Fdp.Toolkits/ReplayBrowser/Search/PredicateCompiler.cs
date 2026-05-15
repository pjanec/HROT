using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using StructEdit.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Compiles SearchPredicateDto trees into Func&lt;EntityRepository, Entity, bool&gt; delegates.
    /// </summary>
    public sealed class PredicateCompiler : IPredicateCompiler
    {
        private readonly IComponentEditService _editService;

        // Cache of per-type boxed component getters: (EntityRepository, Entity) -> object?
        private static readonly ConcurrentDictionary<Type, Func<EntityRepository, Entity, object?>> _componentGetters
            = new ConcurrentDictionary<Type, Func<EntityRepository, Entity, object?>>();

        // Generic helper method referenced via reflection to box unmanaged component values.
        private static readonly MethodInfo _getComponentBoxedMethod =
            typeof(PredicateCompiler).GetMethod(
                nameof(GetComponentBoxed),
                BindingFlags.NonPublic | BindingFlags.Static)!;

        public PredicateCompiler(IComponentEditService editService)
        {
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        }

        // ── IPredicateCompiler ───────────────────────────────────────────────

        /// <inheritdoc/>
        public Func<EntityRepository, Entity, bool> CompileComponentPredicate(SearchPredicateDto root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            return Compile(root);
        }

        /// <inheritdoc/>
        public IReadOnlyList<Type> ExtractMandatoryComponents(SearchPredicateDto root)
        {
            var result = new List<Type>();
            CollectMandatoryComponents(root, result);
            return result;
        }

        // ── Recursive compilation ────────────────────────────────────────────

        private Func<EntityRepository, Entity, bool> Compile(SearchPredicateDto dto)
        {
            switch (dto)
            {
                case CompoundPredicateDto compound:
                    return CompileCompound(compound);

                case PropertyMatchDto prop:
                    return CompilePropertyMatch(prop);

                // Specialized loop predicates: pass-through; handled by the service.
                case StructuralPredicateDto _:
                case SpatialBoundingPredicateDto _:
                case LifecyclePredicateDto _:
                case TransientEventPredicateDto _:
                    return static (_, _) => true;

                // Value predicates used standalone: pass-through (normally nested inside PropertyMatch).
                default:
                    return static (_, _) => true;
            }
        }

        private Func<EntityRepository, Entity, bool> CompileCompound(CompoundPredicateDto compound)
        {
            // Compile all child predicates upfront.
            var childFuncs = new Func<EntityRepository, Entity, bool>[compound.Conditions.Count];
            for (int i = 0; i < compound.Conditions.Count; i++)
                childFuncs[i] = Compile(compound.Conditions[i]);

            if (compound.Operator == LogicalOperator.And)
            {
                return (repo, entity) =>
                {
                    for (int i = 0; i < childFuncs.Length; i++)
                        if (!childFuncs[i](repo, entity)) return false;
                    return true;
                };
            }
            else // Or
            {
                return (repo, entity) =>
                {
                    for (int i = 0; i < childFuncs.Length; i++)
                        if (childFuncs[i](repo, entity)) return true;
                    return false;
                };
            }
        }

        private Func<EntityRepository, Entity, bool> CompilePropertyMatch(PropertyMatchDto prop)
        {
            Type componentType = prop.ComponentType;
            int typeId = ComponentTypeRegistry.GetId(componentType);

            // Compile the IPropertyEvaluator for this component+path.
            var evaluator = new PropertyEvaluator(_editService, componentType, prop.PropertyPath);

            // Compile the operator function based on the sub-predicate.
            Func<string, bool> operatorFn = CompileOperatorFn(prop.Operator, prop.Predicate);

            // Build cached getter for this component type.
            Func<EntityRepository, Entity, object?> getter = GetOrBuildGetter(componentType);

            return (repo, entity) =>
            {
                // Guard: entity must have the component.
                if (!repo.HasComponentByTypeId(entity, typeId)) return false;

                // Get the component as object and evaluate.
                object? component = getter(repo, entity);
                if (component == null) return false;

                string value = evaluator.GetValueAsString(component);
                return operatorFn(value);
            };
        }

        // ── Operator compilation ─────────────────────────────────────────────

        private static Func<string, bool> CompileOperatorFn(SearchOperator op, SearchPredicateDto? valuePredicate)
        {
            switch (op)
            {
                case SearchOperator.Changed:
                    // "Changed" fires whenever the component mutates; value is irrelevant.
                    return static _ => true;

                case SearchOperator.Equals:
                    if (valuePredicate is NumericPredicateDto numEq)
                    {
                        double minV = numEq.MinValue;
                        double maxV = numEq.MaxValue;
                        return value =>
                            double.TryParse(value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double d)
                            && d >= minV && d <= maxV;
                    }
                    if (valuePredicate is StringPredicateDto strEq)
                    {
                        string sub = strEq.Substring;
                        return value => string.Equals(value, sub, StringComparison.OrdinalIgnoreCase);
                    }
                    // Fallback: treat as always-match if no sub-predicate.
                    return static _ => true;

                case SearchOperator.Contains:
                    if (valuePredicate is StringPredicateDto strCon)
                    {
                        string sub = strCon.Substring;
                        return value => value.Contains(sub, StringComparison.OrdinalIgnoreCase);
                    }
                    return static _ => true;

                case SearchOperator.StartsWith:
                    if (valuePredicate is StringPredicateDto strSw)
                    {
                        string sub = strSw.Substring;
                        return value => value.StartsWith(sub, StringComparison.OrdinalIgnoreCase);
                    }
                    return static _ => true;

                case SearchOperator.GreaterThan:
                    if (valuePredicate is NumericPredicateDto numGt)
                    {
                        double min = numGt.MinValue;
                        return value =>
                            double.TryParse(value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double d)
                            && d > min;
                    }
                    return static _ => true;

                case SearchOperator.LessThan:
                    if (valuePredicate is NumericPredicateDto numLt)
                    {
                        double max = numLt.MaxValue;
                        return value =>
                            double.TryParse(value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double d)
                            && d < max;
                    }
                    return static _ => true;

                default:
                    return static _ => true;
            }
        }

        // ── ExtractMandatoryComponents ───────────────────────────────────────

        private static void CollectMandatoryComponents(SearchPredicateDto dto, List<Type> result)
        {
            if (dto is CompoundPredicateDto compound && compound.Operator == LogicalOperator.And)
            {
                foreach (var condition in compound.Conditions)
                {
                    if (condition is PropertyMatchDto propMatch)
                    {
                        if (!result.Contains(propMatch.ComponentType))
                            result.Add(propMatch.ComponentType);
                    }
                    else if (condition is CompoundPredicateDto nested)
                    {
                        CollectMandatoryComponents(nested, result);
                    }
                }
            }
            else if (dto is PropertyMatchDto single)
            {
                if (!result.Contains(single.ComponentType))
                    result.Add(single.ComponentType);
            }
        }

        // ── Per-type component getter ─────────────────────────────────────────

        private static Func<EntityRepository, Entity, object?> GetOrBuildGetter(Type componentType)
        {
            return _componentGetters.GetOrAdd(componentType, BuildGetter);
        }

        private static Func<EntityRepository, Entity, object?> BuildGetter(Type componentType)
        {
            if (componentType.IsValueType)
            {
                // Unmanaged struct: call GetComponent<T>(entity) via a cached generic delegate.
                // GetComponentBoxed<T> is a static helper that boxes the ref readonly T value.
                MethodInfo concreteMethod = _getComponentBoxedMethod.MakeGenericMethod(componentType);

                // CreateDelegate requires exact signature match: Func<EntityRepository, Entity, object?>
                return (Func<EntityRepository, Entity, object?>)
                    Delegate.CreateDelegate(typeof(Func<EntityRepository, Entity, object?>), concreteMethod);
            }
            else
            {
                // Managed component: use GetManagedComponentByTypeId which returns object directly.
                int typeId = ComponentTypeRegistry.GetId(componentType);
                return (repo, entity) => repo.GetManagedComponentByTypeId(entity, typeId);
            }
        }

        // GetComponentBoxed is a static generic helper that can be bound via CreateDelegate.
        // The `where T : unmanaged` constraint is satisfied at MakeGenericMethod call time.
#pragma warning disable IDE0051 // Used via reflection
        private static object? GetComponentBoxed<T>(EntityRepository repo, Entity entity)
            where T : unmanaged
        {
            return repo.GetComponent<T>(entity);
        }
#pragma warning restore IDE0051
    }
}
