using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
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
        private readonly BlueprintRegistry? _blueprintRegistry;

        public PredicateCompiler(IComponentEditService editService, BehaviorRegistry? behaviorRegistry = null, BlueprintRegistry? blueprintRegistry = null)
        {
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
            _behaviorRegistry = behaviorRegistry ?? new BehaviorRegistry();
            _blueprintRegistry = blueprintRegistry;
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

                case ExternalHitTagPredicateDto _:
                    // ExternalHitTag predicates are never evaluated via the component-data path.
                    // DataBreakpointManager.OnExternalHit handles them directly.
                    return static (_, _) => false;

                case TraceBufferScanPredicateDto traceScan:
                    return CompileTraceBufferScan(traceScan);

                case BlueprintVariablePredicateDto blueprintVar:
                    return CompileBlueprintVariablePredicate(blueprintVar);

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
            if (componentType == null)
                return static (_, _) => false;

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

        private Func<EntityRepository, Entity, bool> CompileTraceBufferScan(TraceBufferScanPredicateDto scan)
        {
            if (scan.ComponentType == null) return static (_, _) => false;

            var buildMethod = typeof(PredicateCompiler)
                .GetMethod(nameof(BuildTraceBufferScanMatcher), BindingFlags.NonPublic | BindingFlags.Static)!;
            return (Func<EntityRepository, Entity, bool>)buildMethod
                .MakeGenericMethod(scan.ComponentType)
                .Invoke(null, new object[] { scan })!;
        }

        private static unsafe Func<EntityRepository, Entity, bool> BuildTraceBufferScanMatcher<T>(
            TraceBufferScanPredicateDto scan)
            where T : unmanaged
        {
            int    typeId       = ComponentTypeRegistry.GetId(typeof(T));
            byte   opCode       = scan.OpCode;
            ushort indexField   = scan.IndexField;
            bool   matchIndex   = scan.MatchIndexField;
            byte   statusField  = scan.StatusField;
            bool   matchStatus  = scan.MatchStatusField;
            ushort triggerEvtId = scan.TriggerEventId;
            bool   matchTrigger = scan.MatchTriggerEventId;

            return (repo, entity) =>
            {
                if (!repo.HasComponentByTypeId(entity, typeId)) return false;

                ref readonly T comp = ref repo.GetComponentRO<T>(entity);
                unsafe
                {
                    // Both BTreeTraceWorkingMemory1024 and HsmTraceWorkingMemory1024 share
                    // identical 8-byte headers:
                    //   offset 0: WritePos     (ushort)
                    //   offset 2: RecordCount  (ushort)
                    //   offset 4: LastInstanceId (uint)
                    //   offset 8: Buffer start (16-byte stride records)
                    byte*  ptr         = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in comp));
                    ushort recordCount = *(ushort*)(ptr + 2);
                    byte*  buf         = ptr + 8;

                    for (int i = 0; i < recordCount; i++)
                    {
                        byte* rec = buf + i * 16;
                        if (rec[0] != opCode)                                    continue;
                        if (matchIndex  && *(ushort*)(rec + 8)  != indexField)   continue;
                        if (matchStatus && rec[10]               != statusField)  continue;
                        if (matchTrigger && *(ushort*)(rec + 12) != triggerEvtId) continue;
                        return true;
                    }
                    return false;
                }
            };
        }

        private Func<EntityRepository, Entity, bool> CompileBlueprintVariablePredicate(BlueprintVariablePredicateDto dto)
        {
            if (string.IsNullOrEmpty(dto.VariableName) || _blueprintRegistry == null)
                return static (_, _) => false;

            int blueprintId = BlueprintIdHash.Compute(dto.TargetBlueprintAssetId);

            if (!_blueprintRegistry.TryGetById(blueprintId, out var def) || def == null)
                return static (_, _) => false;

            if (!def.StateFields.TryGetValue(dto.VariableName, out var fieldDesc) || fieldDesc == null)
                return static (_, _) => false;

            var method = typeof(PredicateCompiler)
                .GetMethod(nameof(BuildBlueprintVariableMatcher), BindingFlags.NonPublic | BindingFlags.Static)!;
            return (Func<EntityRepository, Entity, bool>)method
                .MakeGenericMethod(fieldDesc.ClrType)
                .Invoke(null, new object[] { blueprintId, fieldDesc.OffsetBytes, dto })!;
        }

        private static unsafe Func<EntityRepository, Entity, bool> BuildBlueprintVariableMatcher<TField>(
            int blueprintId,
            int fieldOffset,
            BlueprintVariablePredicateDto dto)
            where TField : unmanaged
        {
            // Build a compiled comparison expression for the field type.
            var param = Expression.Parameter(typeof(TField).MakeByRefType(), "field");
            Expression condition = BuildConditionExpression(param, dto.Operator, dto.Predicate);
            var matcher = Expression.Lambda<ComponentMatcherDelegate<TField>>(condition, param).Compile();

            // Bake tier component type IDs at compile time.
            // GetId returns -1 for unregistered types (BB16384 in test repos) -> HasComponentByTypeId returns false.
            int typeId1024  = ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard1024));
            int typeId4096  = ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard4096));
            int typeId16384 = ComponentTypeRegistry.GetId(typeof(BlueprintBlackboard16384));

            return (repo, entity) =>
            {
                unsafe
                {
                    byte* memory = null;

                    if (repo.HasComponentByTypeId(entity, typeId1024))
                    {
                        ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard1024>(entity);
                        memory = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
                    }
                    else if (repo.HasComponentByTypeId(entity, typeId4096))
                    {
                        ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard4096>(entity);
                        memory = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
                    }
                    else if (repo.HasComponentByTypeId(entity, typeId16384))
                    {
                        ref readonly var bb = ref repo.GetComponentRO<BlueprintBlackboard16384>(entity);
                        memory = (byte*)Unsafe.AsPointer(ref Unsafe.AsRef(in bb));
                    }

                    if (memory == null) return false;

                    if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
                        return false;

                    ref TField fieldRef = ref Unsafe.AsRef<TField>(memory + payloadOffset + fieldOffset);
                    return matcher(ref fieldRef);
                }
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

        /// <summary>
        /// Adds <paramref name="componentType"/> to <paramref name="result"/> unless it is null or
        /// already present.
        ///
        /// <para>
        /// ⚠ The null check is load-bearing. <c>PropertyMatchDto.ComponentType</c> is declared
        /// <c>null!</c> and is genuinely null in two ordinary situations: a predicate the designer
        /// created but has not yet given a component type (the panel's "Add" builds a bare
        /// <c>new PropertyMatchDto()</c>), and one deserialized from a type name that no longer
        /// resolves — <c>TypeNameJsonConverter.Read</c> returns null for those by design. Letting
        /// either into this list put a null into <c>CompiledComponentPredicate.MandatoryComponents</c>,
        /// which <c>DataBreakpointSystem</c> then passed to <c>ComponentTypeRegistry.GetId</c> every
        /// frame. A single empty "New Breakpoint", once saved to the debug session, killed the
        /// editor on every subsequent launch.
        /// </para>
        /// </summary>
        private static void AddIfResolvable(Type? componentType, List<Type> result)
        {
            if (componentType is null) return;
            if (result.Contains(componentType)) return;
            result.Add(componentType);
        }

        private static void CollectMandatoryComponents(SearchPredicateDto dto, List<Type> result)
        {
            if (dto is CompoundPredicateDto compound && compound.Operator == LogicalOperator.And)
            {
                foreach (var condition in compound.Conditions)
                {
                    if (condition is PropertyMatchDto propMatch)
                    {
                        AddIfResolvable(propMatch.ComponentType, result);
                    }
                    else if (condition is CompoundPredicateDto nested)
                    {
                        CollectMandatoryComponents(nested, result);
                    }
                }
            }
            else if (dto is PropertyMatchDto single)
            {
                AddIfResolvable(single.ComponentType, result);
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
            else if (dto is TraceBufferScanPredicateDto traceScan)
            {
                if (traceScan.ComponentType != null && !result.Contains(traceScan.ComponentType))
                    result.Add(traceScan.ComponentType);
            }
        }
    }
}
