using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Registry of behavior-param JSON remapping delegates.
    ///
    /// <para>
    /// Before scenario load, each behavior whose param JSON may contain network IDs
    /// is registered via <see cref="Register{TDto}"/>.  During extraction,
    /// <see cref="RemapJson"/> is called for every behavior task in the staging world;
    /// registered behaviors have their IDs replaced according to the old-to-new
    /// network-ID map; unknown behavior IDs pass through unchanged.
    /// </para>
    ///
    /// <para>
    /// Delegates are compiled once per DTO type by
    /// <see cref="BehaviorParamRemapperCompiler"/> and are cached globally across
    /// all <see cref="ScenarioBehaviorRemapper"/> instances.
    /// </para>
    /// </summary>
    public sealed class ScenarioBehaviorRemapper
    {
        private readonly Dictionary<string, Func<string?, Dictionary<long, long>, string?>> _registry = new();

        /// <summary>
        /// Registers a remapping delegate for the specified <paramref name="behaviorId"/>.
        /// The delegate is compiled from <typeparamref name="TDto"/> by
        /// <see cref="BehaviorParamRemapperCompiler.Compile{TDto}"/>.
        /// </summary>
        /// <typeparam name="TDto">Behavior-param DTO class with <c>[RemapNetworkId]</c>
        ///   properties.</typeparam>
        /// <param name="behaviorId">The string identifier used in mission plan tasks.</param>
        /// <exception cref="InvalidOperationException">
        ///   Thrown if <paramref name="behaviorId"/> has already been registered.
        /// </exception>
        public void Register<TDto>(string behaviorId)
            where TDto : class, new()
        {
            if (_registry.ContainsKey(behaviorId))
                throw new InvalidOperationException(
                    $"ScenarioBehaviorRemapper: behaviorId '{behaviorId}' is already registered.");

            _registry[behaviorId] = BehaviorParamRemapperCompiler.Compile<TDto>();
        }

        /// <summary>
        /// Remaps network IDs in <paramref name="json"/> for the specified
        /// <paramref name="behaviorId"/>.
        ///
        /// <para>If <paramref name="behaviorId"/> has not been registered, <paramref name="json"/>
        /// is returned unchanged (no exception).</para>
        /// </summary>
        /// <param name="behaviorId">Behavior type identifier.</param>
        /// <param name="json">Serialized behavior-param JSON, or <c>null</c>.</param>
        /// <param name="idMap">Old-to-new network ID map built during two-pass extraction.</param>
        /// <returns>Remapped JSON, or the original <paramref name="json"/> when no mapping
        ///   applies.</returns>
        public string? RemapJson(string behaviorId, string? json, Dictionary<long, long> idMap)
        {
            return _registry.TryGetValue(behaviorId, out var remap)
                ? remap(json, idMap)
                : json;
        }
    }
}
