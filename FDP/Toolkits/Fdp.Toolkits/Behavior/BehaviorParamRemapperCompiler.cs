using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Compiles and caches a remapping delegate for a behavior-param DTO.
    ///
    /// <para>
    /// The returned delegate replaces all network identifier values in the DTO's
    /// JSON representation according to a caller-supplied old-to-new ID map.
    /// Properties annotated with <see cref="RemapNetworkIdAttribute"/> are remapped;
    /// all other properties are preserved unchanged.
    /// </para>
    ///
    /// <para>
    /// Reflection (<c>GetProperties</c>, <c>GetCustomAttributes</c>) is performed
    /// exactly once per DTO type inside <see cref="Compile{TDto}"/>.
    /// The returned delegate uses expression-tree-compiled getter and setter lambdas
    /// and never calls <c>PropertyInfo.GetValue</c> or <c>PropertyInfo.SetValue</c>.
    /// </para>
    ///
    /// <para>Thread-safe: <see cref="Compile{TDto}"/> may be called concurrently
    /// from multiple threads; the cache is backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
    /// </summary>
    public static class BehaviorParamRemapperCompiler
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        // Cache stores Func<string?, Dictionary<long, long>, string?> boxed as object.
        private static readonly ConcurrentDictionary<Type, object> _cache = new();

        /// <summary>
        /// Number of times the expression-tree compilation path was invoked.
        /// A value of 1 after N calls to <see cref="Compile{TDto}"/> with the same type
        /// confirms that caching is effective.
        /// </summary>
        internal static int CompileCallCount { get; private set; }

        /// <summary>
        /// Returns a cached <c>Func&lt;string?, Dictionary&lt;long, long&gt;, string?&gt;</c>
        /// that remaps all <see cref="RemapNetworkIdAttribute"/>-tagged properties in
        /// <typeparamref name="TDto"/>'s serialized JSON.
        ///
        /// <list type="bullet">
        ///   <item>If <typeparamref name="TDto"/> has no remappable properties the identity
        ///     delegate <c>(json, _) =&gt; json</c> is returned.</item>
        ///   <item>If the input JSON is <c>null</c> or empty it is returned unchanged.
        ///   </item>
        ///   <item>If a network ID is absent from <paramref name="map"/> it is left
        ///     unchanged.</item>
        /// </list>
        /// </summary>
        /// <typeparam name="TDto">A JSON-serializable DTO class with a public parameterless
        ///   constructor.</typeparam>
        public static Func<string?, Dictionary<long, long>, string?> Compile<TDto>()
            where TDto : class, new()
        {
            return (Func<string?, Dictionary<long, long>, string?>)
                _cache.GetOrAdd(typeof(TDto), _ => BuildDelegate<TDto>());
        }

        private static Func<string?, Dictionary<long, long>, string?> BuildDelegate<TDto>()
            where TDto : class, new()
        {
            CompileCallCount++;

            // Reflection performed once here, never in the returned delegate.
            var remappable = typeof(TDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.GetCustomAttribute<RemapNetworkIdAttribute>(inherit: true) != null
                    && (p.PropertyType == typeof(long) || p.PropertyType == typeof(int))
                    && p.CanRead
                    && p.CanWrite)
                .ToArray();

            if (remappable.Length == 0)
            {
                // No remappable properties: return identity delegate.
                return (json, _) => json;
            }

            // Build expression-tree-compiled getter/setter pairs — no PropertyInfo.GetValue/SetValue.
            var accessors = new (Func<TDto, long> getter, Action<TDto, long> setter)[remappable.Length];
            for (int i = 0; i < remappable.Length; i++)
            {
                var prop = remappable[i];

                // getter: (TDto dto) => (long)dto.Property
                var getParam  = Expression.Parameter(typeof(TDto), "dto");
                var propRead  = Expression.Property(getParam, prop);
                var asLong    = prop.PropertyType == typeof(long)
                    ? (Expression)propRead
                    : Expression.Convert(propRead, typeof(long));
                var getter    = Expression.Lambda<Func<TDto, long>>(asLong, getParam).Compile();

                // setter: (TDto dto, long newVal) => dto.Property = (propType)newVal
                var setParam    = Expression.Parameter(typeof(TDto), "dto");
                var newValParam = Expression.Parameter(typeof(long), "newVal");
                var setValueExpr = prop.PropertyType == typeof(long)
                    ? (Expression)newValParam
                    : Expression.Convert(newValParam, prop.PropertyType);
                var assignExpr  = Expression.Assign(Expression.Property(setParam, prop), setValueExpr);
                var setter      = Expression.Lambda<Action<TDto, long>>(assignExpr, setParam, newValParam).Compile();

                accessors[i] = (getter, setter);
            }

            // The returned delegate closes over the compiled accessors — no reflection at runtime.
            return (json, map) =>
            {
                if (string.IsNullOrEmpty(json))
                    return json;

                var dto = JsonSerializer.Deserialize<TDto>(json, _jsonOptions);
                if (dto == null)
                    return json;

                foreach (var (getter, setter) in accessors)
                {
                    long oldId = getter(dto);
                    if (map.TryGetValue(oldId, out long newId))
                        setter(dto, newId);
                }

                return JsonSerializer.Serialize(dto, _jsonOptions);
            };
        }
    }
}
