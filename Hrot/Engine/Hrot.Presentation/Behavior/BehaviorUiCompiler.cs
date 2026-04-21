using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;
using ImGuiNET;

namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// Compiled ImGui draw delegate for a behavior-parameter DTO.
    /// Signature: (currentJson, taskIndex, context) -> newJson (same reference when unchanged).
    /// </summary>
    public delegate string BehaviorUiDrawDelegate(
        string currentJson, int taskIndex, IPickInteractionContext context);

    // ── BehaviorUiRegistry ────────────────────────────────────────────────────

    /// <summary>
    /// Registry mapping behavior IDs to their compiled ImGui draw delegates.
    ///
    /// <para>Populate once at application startup via <see cref="Register{TDto}"/>;
    /// look up per frame via <see cref="TryGet"/>.</para>
    /// </summary>
    public sealed class BehaviorUiRegistry
    {
        private readonly Dictionary<string, BehaviorUiDrawDelegate> _registry = new();

        /// <summary>
        /// Compiles and registers a draw delegate for the specified behavior ID.
        /// </summary>
        /// <typeparam name="TDto">A JSON-serializable DTO that drives the ImGui rendering.</typeparam>
        /// <param name="behaviorId">Behavior identifier string (must be unique per registry).</param>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="behaviorId"/> is already registered.</exception>
        public void Register<TDto>(string behaviorId) where TDto : class, new()
        {
            if (_registry.ContainsKey(behaviorId))
                throw new InvalidOperationException(
                    $"BehaviorUiRegistry: behaviorId '{behaviorId}' is already registered.");

            _registry[behaviorId] = BehaviorUiCompiler.Compile<TDto>();
        }

        /// <summary>
        /// Attempts to retrieve the draw delegate for the given behavior ID.
        /// </summary>
        /// <returns><c>true</c> when the behavior is registered; <c>false</c> otherwise.</returns>
        public bool TryGet(string behaviorId, out BehaviorUiDrawDelegate? drawDelegate)
            => _registry.TryGetValue(behaviorId, out drawDelegate);
    }

    // ── BehaviorUiCompiler ────────────────────────────────────────────────────

    /// <summary>
    /// Compiles and caches ImGui rendering delegates for behavior-parameter DTOs.
    ///
    /// <para>All reflection (<c>GetProperties</c>, <c>GetCustomAttributes</c>) is
    /// performed exactly once per DTO type inside <see cref="Compile{TDto}"/>.
    /// The returned delegate uses expression-tree-compiled getter and setter lambdas
    /// and never calls <c>PropertyInfo.GetValue</c> or <c>PropertyInfo.SetValue</c>.</para>
    ///
    /// <para>When the ImGui render context is absent (tests, headless mode) the
    /// delegate returns <paramref name="currentJson"/> immediately without any
    /// deserialization or ImGui calls.</para>
    /// </summary>
    public static class BehaviorUiCompiler
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly ConcurrentDictionary<Type, BehaviorUiDrawDelegate> _cache = new();

        /// <summary>
        /// Number of times the expression-tree compilation path was invoked.
        /// A value of 1 after N calls to <see cref="Compile{TDto}"/> with the same type
        /// confirms that caching is effective.
        /// </summary>
        internal static int CompileCallCount { get; private set; }

        /// <summary>
        /// Returns a cached <see cref="BehaviorUiDrawDelegate"/> for
        /// <typeparamref name="TDto"/>.  On first call per type, reflection is
        /// performed and expression trees are compiled and cached.
        /// </summary>
        /// <typeparam name="TDto">A JSON-serializable DTO class with a public parameterless constructor.</typeparam>
        public static BehaviorUiDrawDelegate Compile<TDto>() where TDto : class, new()
            => _cache.GetOrAdd(typeof(TDto), _ => BuildDelegate<TDto>());

        /// <summary>
        /// Test hook: applies <paramref name="mutate"/> to a deserialized DTO and returns
        /// the re-serialized JSON.  Allows verifying JSON round-trip logic without requiring
        /// an active ImGui context.
        /// </summary>
        internal static string TestHook_ApplyChange<TDto>(string json, Action<TDto> mutate)
            where TDto : class, new()
        {
            var dto = JsonSerializer.Deserialize<TDto>(json, _jsonOptions) ?? new TDto();
            mutate(dto);
            return JsonSerializer.Serialize(dto, _jsonOptions);
        }

        // ── Private implementation ────────────────────────────────────────────

        private static BehaviorUiDrawDelegate BuildDelegate<TDto>() where TDto : class, new()
        {
            CompileCallCount++;

            // All reflection happens here at compile time, never in the returned delegate.
            var renderers = BuildPropertyRenderers<TDto>();

            return (json, taskIndex, context) =>
            {
                // Early return when not running inside an ImGui frame (e.g., unit tests).
                if (ImGui.GetCurrentContext() == IntPtr.Zero)
                    return json;

                var dto = JsonSerializer.Deserialize<TDto>(json, _jsonOptions);
                if (dto == null)
                    return json;

                bool anyChanged = false;
                foreach (var renderer in renderers)
                    anyChanged |= renderer(dto, taskIndex, context);

                return anyChanged ? JsonSerializer.Serialize(dto, _jsonOptions) : json;
            };
        }

        private static List<Func<TDto, int, IPickInteractionContext, bool>> BuildPropertyRenderers<TDto>()
            where TDto : class
        {
            var renderers = new List<Func<TDto, int, IPickInteractionContext, bool>>();

            var props = typeof(TDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .ToArray();

            foreach (var prop in props)
            {
                var pickEntity   = prop.GetCustomAttribute<MapPickableEntityAttribute>();
                var pickLocation = prop.GetCustomAttribute<MapPickableWorldLocationAttribute>();
                string propName  = prop.Name;

                if (pickEntity != null)
                {
                    // Entity pick: consume any resolved async result first, then show UI.
                    var filterPresets = pickEntity.FilterPresets;
                    var getter        = BuildLongGetter<TDto>(prop);
                    var setter        = BuildSetter<TDto, long>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        bool changed = false;

                        // 1. Consume any asynchronously resolved pick targeting this field.
                        if (ctx.TryConsumeEntityPick(taskIdx, propName, out long pickedId))
                        {
                            setter(dto, pickedId);
                            changed = true;
                        }

                        // 2. Render standard UI.
                        long val = getter(dto);
                        ImGui.Text($"{propName}: {val}");
                        ImGui.SameLine();
                        if (ctx.IsPickPendingFor(taskIdx, propName))
                            ImGui.Text("[Picking...]");
                        else if (ImGui.SmallButton($"Pick##{propName}_{taskIdx}"))
                            ctx.RequestEntityPick(taskIdx, propName, filterPresets);
                        return changed;
                    });
                }
                else if (pickLocation != null && prop.PropertyType == typeof(PickableGeoPoint))
                {
                    // World location pick via composite GeoPoint facade property.
                    // A single "Pick" button drives both lat and lon from one async operation.
                    var getter = BuildGetter<TDto, PickableGeoPoint>(prop);
                    var setter = BuildSetter<TDto, PickableGeoPoint>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        bool changed = false;

                        // 1. Consume any asynchronously resolved location pick.
                        if (ctx.TryConsumeLocationPick(taskIdx, propName, out var pickedLoc))
                        {
                            setter(dto, pickedLoc);
                            changed = true;
                        }

                        // 2. Render UI.
                        var val = getter(dto);
                        if (ctx.IsPickPendingFor(taskIdx, propName))
                        {
                            ImGui.Text($"{propName}: {val.Latitude:F4}, {val.Longitude:F4} [Picking...]");
                        }
                        else
                        {
                            if (ImGui.Button($"Pick##{propName}_{taskIdx}"))
                                ctx.RequestLocationPick(taskIdx, propName);
                            ImGui.SameLine();
                            ImGui.Text($"{val.Latitude:F4}, {val.Longitude:F4}");
                        }
                        return changed;
                    });
                }
                else if (prop.PropertyType == typeof(float))
                {
                    var getter = BuildGetter<TDto, float>(prop);
                    var setter = BuildSetter<TDto, float>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        float val = getter(dto);
                        if (ImGui.InputFloat($"{propName}##{propName}_{taskIdx}", ref val))
                        {
                            setter(dto, val);
                            return true;
                        }
                        return false;
                    });
                }
                else if (prop.PropertyType == typeof(double))
                {
                    var getter = BuildGetter<TDto, double>(prop);
                    var setter = BuildSetter<TDto, double>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        double val = getter(dto);
                        if (ImGui.InputDouble($"{propName}##{propName}_{taskIdx}", ref val))
                        {
                            setter(dto, val);
                            return true;
                        }
                        return false;
                    });
                }
                else if (prop.PropertyType == typeof(int))
                {
                    var getter = BuildGetter<TDto, int>(prop);
                    var setter = BuildSetter<TDto, int>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        int val = getter(dto);
                        if (ImGui.InputInt($"{propName}##{propName}_{taskIdx}", ref val))
                        {
                            setter(dto, val);
                            return true;
                        }
                        return false;
                    });
                }
                else if (prop.PropertyType == typeof(long))
                {
                    var getter = BuildLongGetter<TDto>(prop);
                    var setter = BuildSetter<TDto, long>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        string strVal = getter(dto).ToString();
                        if (ImGui.InputText($"{propName}##{propName}_{taskIdx}", ref strVal, 64))
                        {
                            if (long.TryParse(strVal, out long parsed))
                            {
                                setter(dto, parsed);
                                return true;
                            }
                        }
                        return false;
                    });
                }
                else if (prop.PropertyType == typeof(bool))
                {
                    var getter = BuildGetter<TDto, bool>(prop);
                    var setter = BuildSetter<TDto, bool>(prop);

                    renderers.Add((dto, taskIdx, ctx) =>
                    {
                        bool val = getter(dto);
                        if (ImGui.Checkbox($"{propName}##{propName}_{taskIdx}", ref val))
                        {
                            setter(dto, val);
                            return true;
                        }
                        return false;
                    });
                }
                // Other property types are skipped (no renderer added).
            }

            return renderers;
        }

        // ── Expression-tree helper builders ──────────────────────────────────

        private static Func<TDto, long> BuildLongGetter<TDto>(PropertyInfo prop) where TDto : class
        {
            var param    = Expression.Parameter(typeof(TDto), "dto");
            var propExpr = Expression.Property(param, prop);
            var asLong   = prop.PropertyType == typeof(long)
                ? (Expression)propExpr
                : Expression.Convert(propExpr, typeof(long));
            return Expression.Lambda<Func<TDto, long>>(asLong, param).Compile();
        }

        private static Func<TDto, TProp> BuildGetter<TDto, TProp>(PropertyInfo prop) where TDto : class
        {
            var param    = Expression.Parameter(typeof(TDto), "dto");
            var propExpr = Expression.Property(param, prop);
            return Expression.Lambda<Func<TDto, TProp>>(propExpr, param).Compile();
        }

        private static Action<TDto, TProp> BuildSetter<TDto, TProp>(PropertyInfo prop) where TDto : class
        {
            var dtoParam = Expression.Parameter(typeof(TDto), "dto");
            var valParam = Expression.Parameter(typeof(TProp), "val");
            var assign   = Expression.Assign(Expression.Property(dtoParam, prop), valParam);
            return Expression.Lambda<Action<TDto, TProp>>(assign, dtoParam, valParam).Compile();
        }
    }
}
