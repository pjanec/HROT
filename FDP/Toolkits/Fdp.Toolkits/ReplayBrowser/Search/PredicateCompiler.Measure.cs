using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ rules V-3 (📄 docs/DESIGN_Scenario_Rules.md §00.10) — the NUMBER behind a leaf, for the rules view: a <c>PropertyMatch</c>'s property value, the scenario time of a <c>ScenarioTime</c>, the count of a
    /// <c>CountMatching</c>. It is the other half of the same arms the predicate uses — the field path is walked by the one <see cref="FieldAccess"/>, the time by <see cref="ScenarioNow"/>, the count by
    /// <c>MatchCounter</c> — so the shown number can never be a second reading of what the condition tested.
    /// </summary>
    public sealed partial class PredicateCompiler
    {
        /// <summary>The member chain <paramref name="path"/> (dot-separated; empty = the root itself) read from <paramref name="root"/>: the ONE field accessor of the PropertyMatch arms.</summary>
        internal static Expression FieldAccess(Expression root, string? path)
        {
            Expression access = root;
            if (!string.IsNullOrEmpty(path))
                foreach (string seg in path.Split('.')) access = Expression.PropertyOrField(access, seg);
            return access;
        }

        /// <inheritdoc/>
        public Func<EntityRepository, Entity, double>? CompileLeafMeasure(SearchPredicateDto leaf)
        {
            switch (leaf)
            {
                case PropertyMatchDto prop when prop.ComponentType != null:
                {
                    var build = typeof(PredicateCompiler).GetMethod(prop.ComponentType.IsValueType ? nameof(BuildUnmanagedMeasure) : nameof(BuildManagedMeasure),
                        BindingFlags.NonPublic | BindingFlags.Static)!;
                    return (Func<EntityRepository, Entity, double>?)build.MakeGenericMethod(prop.ComponentType).Invoke(null, new object[] { prop });
                }
                case ScenarioTimePredicateDto:
                    return static (repo, _) => ScenarioNow(repo);
                case CountMatchingPredicateDto count:
                {
                    var counter = BuildCounter(count, PredicateMode.State);
                    return (repo, _) => counter.Count(repo, int.MaxValue - 1);
                }
                case InputAbovePredicateDto inputAbove when !string.IsNullOrEmpty(inputAbove.Input):
                {
                    string name = inputAbove.Input;
                    return (repo, e) => UtilityInputReaderStore.TryRead(repo, name, e, Entity.Null, out float v) ? v : 0d;   // ⭐ the same reader the leaf tests
                }
                case OnEntityPredicateDto onEntity:
                {
                    var inner = onEntity.Predicate is null ? null : CompileLeafMeasure(onEntity.Predicate);
                    if (inner is null) return null;
                    var reference = onEntity.Ref.Ref;
                    return (repo, _) =>
                    {
                        var target = reference.Resolve(repo);
                        return !target.IsNull && repo.IsAlive(target) ? inner(repo, target) : 0d;
                    };
                }
                default:
                    return null;   // spatial · lifecycle · structural · event · a compound: no number — the caller shows the 0/1
            }
        }

        /// <summary>Converts a numeric / enum / bool field to <c>double</c>; any other type has no number (null).</summary>
        private static Expression? ToDouble(Expression field)
        {
            var t = field.Type;
            if (t == typeof(double)) return field;
            if (t == typeof(bool)) return Expression.Condition(field, Expression.Constant(1d), Expression.Constant(0d));
            if (t.IsEnum) return Expression.Convert(Expression.Convert(field, Enum.GetUnderlyingType(t)), typeof(double));
            if (t.IsPrimitive && t != typeof(char) && t != typeof(IntPtr) && t != typeof(UIntPtr)) return Expression.Convert(field, typeof(double));
            return null;
        }

        private delegate double ComponentMeasureDelegate<T>(ref T component);

        private static Func<EntityRepository, Entity, double>? BuildUnmanagedMeasure<T>(PropertyMatchDto prop) where T : unmanaged
        {
            int typeId = ComponentTypeRegistry.GetId(typeof(T));
            var param = Expression.Parameter(typeof(T).MakeByRefType(), "comp");
            var number = ToDouble(FieldAccess(param, prop.PropertyPath));
            if (number is null) return null;
            var read = Expression.Lambda<ComponentMeasureDelegate<T>>(number, param).Compile();
            return (repo, entity) =>
            {
                if (!repo.HasComponentByTypeId(entity, typeId)) return 0d;
                ref readonly T comp = ref repo.GetComponentRO<T>(entity);
                return read(ref Unsafe.AsRef(in comp));
            };
        }

        private static Func<EntityRepository, Entity, double>? BuildManagedMeasure<T>(PropertyMatchDto prop) where T : class
        {
            var param = Expression.Parameter(typeof(T), "comp");
            var number = ToDouble(FieldAccess(param, prop.PropertyPath));
            if (number is null) return null;
            var read = Expression.Lambda<Func<T, double>>(number, param).Compile();
            return (repo, entity) =>
            {
                if (!repo.HasManagedComponent<T>(entity)) return 0d;
                T comp = ((ISimulationView)repo).GetManagedComponentRO<T>(entity);
                return comp == null ? 0d : read(comp);
            };
        }
    }
}
