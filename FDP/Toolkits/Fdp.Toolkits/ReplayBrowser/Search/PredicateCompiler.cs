using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using StructEdit.Core;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// Compiles SearchPredicateDto trees into Func&lt;EntityRepository, Entity, bool&gt; delegates.
    /// </summary>
    public sealed class PredicateCompiler : IPredicateCompiler
    {
        private readonly IComponentEditService _editService;
        private readonly BehaviorRegistry _behaviorRegistry;

        public PredicateCompiler(IComponentEditService editService, BehaviorRegistry? behaviorRegistry = null)
        {
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
            _behaviorRegistry = behaviorRegistry ?? new BehaviorRegistry();
        }

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

        private Func<EntityRepository, Entity, bool> Compile(SearchPredicateDto dto)
        {
            switch (dto)
            {
                case CompoundPredicateDto compound:
                    return CompileCompound(compound);

                case PropertyMatchDto prop:
                    return CompilePropertyMatch(prop);
                
                case BehaviorParamPredicateDto behaviorParam:
                    return CompileBehaviorParamMatch(behaviorParam);

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

            if (componentType.IsValueType)
            {
                var method = typeof(PredicateCompiler).GetMethod(nameof(BuildUnmanagedMatcher), BindingFlags.NonPublic | BindingFlags.Static)!;
                return (Func<EntityRepository, Entity, bool>)method.MakeGenericMethod(componentType).Invoke(null, new object[] { prop })!;
            }
            else
            {
                var method = typeof(PredicateCompiler).GetMethod(nameof(BuildManagedMatcher), BindingFlags.NonPublic | BindingFlags.Static)!;
                return (Func<EntityRepository, Entity, bool>)method.MakeGenericMethod(componentType).Invoke(null, new object[] { prop })!;
            }
        }

        private delegate bool ComponentMatcherDelegate<T>(ref T component);

        private static Func<EntityRepository, Entity, bool> BuildUnmanagedMatcher<T>(PropertyMatchDto prop) where T : unmanaged
        {
            int typeId = ComponentTypeRegistry.GetId(typeof(T));

            var param = Expression.Parameter(typeof(T).MakeByRefType(), "comp");
            Expression fieldAccess = param;
            if (!string.IsNullOrEmpty(prop.PropertyPath))
            {
                foreach (string seg in prop.PropertyPath.Split('.'))
                    fieldAccess = Expression.PropertyOrField(fieldAccess, seg);
            }

            Expression condition = BuildConditionExpression(fieldAccess, prop.Operator, prop.Predicate);
            var matcher = Expression.Lambda<ComponentMatcherDelegate<T>>(condition, param).Compile();

            return (repo, entity) =>
            {
                if (!repo.HasComponentByTypeId(entity, typeId)) return false;

                ref readonly T comp = ref repo.GetComponentRO<T>(entity);
                return matcher(ref Unsafe.AsRef(in comp));
            };
        }

        private static Func<EntityRepository, Entity, bool> BuildManagedMatcher<T>(PropertyMatchDto prop) where T : class
        {
            var param = Expression.Parameter(typeof(T), "comp");
            Expression fieldAccess = param;
            if (!string.IsNullOrEmpty(prop.PropertyPath))
            {
                foreach (string seg in prop.PropertyPath.Split('.'))
                    fieldAccess = Expression.PropertyOrField(fieldAccess, seg);
            }

            Expression condition = BuildConditionExpression(fieldAccess, prop.Operator, prop.Predicate);
            var matcher = Expression.Lambda<Func<T, bool>>(condition, param).Compile();

            return (repo, entity) =>
            {
                if (!repo.HasManagedComponent<T>(entity)) return false;

                T comp = ((ISimulationView)repo).GetManagedComponentRO<T>(entity);
                if (comp == null) return false;
                return matcher(comp);
            };
        }

        private delegate bool BehaviorParamMatcherDelegate<T>(ref T dto);

        private Func<EntityRepository, Entity, bool> CompileBehaviorParamMatch(BehaviorParamPredicateDto dto)
        {
            if (dto.BehaviorId == 0 || string.IsNullOrEmpty(dto.PropertyPath))
                return static (_, _) => false;

            if (!_behaviorRegistry.TryGetDefinition(dto.BehaviorId, out var def))
                return static (_, _) => false;

            Type? dtoType = dto.TargetBlackboard == BlackboardTarget.Blackboard1024
                ? def.HeavyDtoType
                : def.ParamsDtoType;
            if (dtoType == null)
                return static (_, _) => false;

            var param = Expression.Parameter(dtoType.MakeByRefType(), "dto");
            Expression fieldAccess = param;
            foreach (string seg in dto.PropertyPath.Split('.'))
                fieldAccess = Expression.PropertyOrField(fieldAccess, seg);

            Expression condition = BuildConditionExpression(fieldAccess, dto.Operator, dto.Predicate);

            string methodName = dto.TargetBlackboard == BlackboardTarget.Blackboard1024
                ? nameof(BuildBehaviorParamMatcherGenericHeavy)
                : nameof(BuildBehaviorParamMatcherGenericBrain);
            var buildMethod = typeof(PredicateCompiler).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var genericBuild = buildMethod.MakeGenericMethod(dtoType);
            return (Func<EntityRepository, Entity, bool>)genericBuild.Invoke(
                null, new object[] { dto.BehaviorId, condition, param })!;
        }

        private static Func<EntityRepository, Entity, bool> BuildBehaviorParamMatcherGenericBrain<TDto>(
            int behaviorHash,
            Expression condition,
            ParameterExpression dtoParam)
            where TDto : unmanaged
        {
            var matcher = Expression.Lambda<BehaviorParamMatcherDelegate<TDto>>(condition, dtoParam).Compile();
            int stateTypeId = ComponentTypeRegistry.GetId(typeof(BehaviorState));
            int bbTypeId = ComponentTypeRegistry.GetId(typeof(BrainBlackboard));

            return (repo, entity) =>
            {
                if (!repo.HasComponentByTypeId(entity, stateTypeId)) return false;
                if (!repo.HasComponentByTypeId(entity, bbTypeId)) return false;

                ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);
                if (state.ActiveBehaviorHash != behaviorHash) return false;

                ref readonly var bb = ref repo.GetComponentRO<BrainBlackboard>(entity);
                unsafe
                {
                    fixed (byte* src = bb.BehaviorParameters)
                    {
                        ref TDto projected = ref Unsafe.AsRef<TDto>(src);
                        return matcher(ref projected);
                    }
                }
            };
        }

        private static Func<EntityRepository, Entity, bool> BuildBehaviorParamMatcherGenericHeavy<TDto>(
            int behaviorHash,
            Expression condition,
            ParameterExpression dtoParam)
            where TDto : unmanaged
        {
            var matcher = Expression.Lambda<BehaviorParamMatcherDelegate<TDto>>(condition, dtoParam).Compile();
            int stateTypeId = ComponentTypeRegistry.GetId(typeof(BehaviorState));
            int bbTypeId = ComponentTypeRegistry.GetId(typeof(Blackboard1024));

            return (repo, entity) =>
            {
                if (!repo.HasComponentByTypeId(entity, stateTypeId)) return false;
                if (!repo.HasComponentByTypeId(entity, bbTypeId)) return false;

                ref readonly var state = ref repo.GetComponentRO<BehaviorState>(entity);
                if (state.ActiveBehaviorHash != behaviorHash) return false;

                ref readonly var bb = ref repo.GetComponentRO<Blackboard1024>(entity);
                unsafe
                {
                    fixed (byte* src = bb.Memory)
                    {
                        ref TDto projected = ref Unsafe.AsRef<TDto>(src);
                        return matcher(ref projected);
                    }
                }
            };
        }

        private static Expression BuildConditionExpression(Expression fieldAccess, SearchOperator op, SearchPredicateDto? predicate)
        {
            if (op == SearchOperator.Changed) return Expression.Constant(true);

            if (predicate is NumericPredicateDto num)
            {
                Expression asDouble;
                if (fieldAccess.Type.IsEnum)
                    asDouble = Expression.Convert(Expression.Convert(fieldAccess, Enum.GetUnderlyingType(fieldAccess.Type)), typeof(double));
                else if (fieldAccess.Type == typeof(bool))
                    asDouble = Expression.Condition(fieldAccess, Expression.Constant(1.0), Expression.Constant(0.0));
                else
                    asDouble = Expression.Convert(fieldAccess, typeof(double));

                if (op == SearchOperator.Equals)
                {
                    return Expression.AndAlso(
                        Expression.GreaterThanOrEqual(asDouble, Expression.Constant(num.MinValue)),
                        Expression.LessThanOrEqual(asDouble, Expression.Constant(num.MaxValue))
                    );
                }
                if (op == SearchOperator.GreaterThan)
                    return Expression.GreaterThan(asDouble, Expression.Constant(num.MinValue));
                if (op == SearchOperator.LessThan)
                    return Expression.LessThan(asDouble, Expression.Constant(num.MaxValue));
            }
            else if (predicate is StringPredicateDto str)
            {
                var target = Expression.Constant(str.Substring ?? string.Empty, typeof(string));
                var comparison = Expression.Constant(StringComparison.OrdinalIgnoreCase);

                Expression asString;
                if (fieldAccess.Type == typeof(string))
                {
                    asString = fieldAccess;
                }
                else
                {
                    var toStringMethod = fieldAccess.Type.GetMethod("ToString", Type.EmptyTypes) ?? typeof(object).GetMethod("ToString")!;
                    asString = Expression.Call(fieldAccess, toStringMethod);
                }

                var isNotNull = Expression.NotEqual(asString, Expression.Constant(null, typeof(string)));

                Expression stringCheck;
                if (op == SearchOperator.Equals)
                    stringCheck = Expression.Call(typeof(string), "Equals", null, asString, target, comparison);
                else if (op == SearchOperator.StartsWith)
                    stringCheck = Expression.Call(asString, typeof(string).GetMethod("StartsWith", new[] { typeof(string), typeof(StringComparison) })!, target, comparison);
                else
                    stringCheck = Expression.Call(asString, typeof(string).GetMethod("Contains", new[] { typeof(string), typeof(StringComparison) })!, target, comparison);

                return Expression.AndAlso(isNotNull, stringCheck);
            }

            return Expression.Constant(true);
        }

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
            else if (dto is BehaviorParamPredicateDto behaviorParam)
            {
                if (!result.Contains(typeof(BehaviorState)))
                    result.Add(typeof(BehaviorState));
                Type targetComponentType = behaviorParam.TargetBlackboard == BlackboardTarget.Blackboard1024
                    ? typeof(Blackboard1024)
                    : typeof(BrainBlackboard);
                if (!result.Contains(targetComponentType))
                    result.Add(targetComponentType);
            }
        }
    }
}
