using System;
using System.Collections.Generic;
using Fdp.Toolkit.Replication;

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
    /// Delegates are compiled once per contract type by <see cref="EntityRefRemap.CompileJson"/> — every
    /// <see cref="EntityRef"/> inside the contract (nested, in lists) — and cached globally.
    /// 📄 <c>docs/blueprints/DESIGN_Entity_Reference.md</c> D3.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <c>CE-2054</c> (S8o, <c>DESIGN_Unified_Behaviour_Run.md</c>) — <b>given a <see cref="BehaviorRegistry"/>, a
    /// behaviour's contract TYPE comes from the registry</b> (<see cref="BehaviorDefinition.JsonParamsDtoType"/>, which
    /// already settles a curated contract against a generated one, <c>CE-235</c>), looked up LAZILY so a blueprint
    /// authored or hot-reloaded after startup is covered too. ⛔ Without it a blueprint behaviour — whose contract is its
    /// generated <c>Params</c> struct, never a <c>[Behavior(Params = …)]</c> — was looked up by name, found nothing, and kept
    /// the scenario file's ids. The <see cref="Register{TDto}"/> table is the fallback for a host with no registry.
    /// </para>
    /// </summary>
    public sealed class ScenarioBehaviorRemapper
    {
        private readonly Dictionary<string, Type> _registry = new();   // behaviour name → its params contract (the fallback table)
        private readonly BehaviorRegistry? _behaviors;

        /// <param name="behaviors">
        /// ⭐ The runtime behaviour registry, the source of every behaviour's contract type. ⛔ A caller that HAS one must
        /// pass it (the silent-default pattern): without it only <see cref="Register{TDto}"/>'d names are remapped.
        /// </param>
        public ScenarioBehaviorRemapper(BehaviorRegistry? behaviors = null)
        {
            _behaviors = behaviors;
        }

        /// <summary>
        /// Registers a remapping delegate for the specified <paramref name="behaviorId"/>.
        /// The delegate is compiled from <typeparamref name="TDto"/> by <see cref="EntityRefRemap.CompileJson"/>.
        /// </summary>
        /// <typeparam name="TDto">The behaviour's JSON contract (class or struct); its <see cref="EntityRef"/> members are
        ///   remapped.</typeparam>
        /// <param name="behaviorId">The string identifier used in mission plan tasks.</param>
        /// <exception cref="InvalidOperationException">
        ///   Thrown if <paramref name="behaviorId"/> has already been registered.
        /// </exception>
        public void Register<TDto>(string behaviorId)
            where TDto : new()   // CE-2023 ③ — a struct contract too ("S8n")
        {
            if (_registry.ContainsKey(behaviorId))
                throw new InvalidOperationException(
                    $"ScenarioBehaviorRemapper: behaviorId '{behaviorId}' is already registered.");

            _registry[behaviorId] = typeof(TDto);
        }

        /// <summary>
        /// Remaps network IDs in <paramref name="json"/> for the specified
        /// <paramref name="behaviorId"/>.
        ///
        /// <para>The contract type is the registry's when a <see cref="BehaviorRegistry"/> was supplied and knows the
        /// behaviour; otherwise the <see cref="Register{TDto}"/>'d one. If neither knows <paramref name="behaviorId"/>,
        /// <paramref name="json"/> is returned unchanged (no exception).</para>
        /// </summary>
        /// <param name="behaviorId">Behavior type identifier.</param>
        /// <param name="json">Serialized behavior-param JSON, or <c>null</c>.</param>
        /// <param name="idMap">Old-to-new network ID map built during two-pass extraction.</param>
        /// <returns>Remapped JSON, or the original <paramref name="json"/> when no mapping
        ///   applies.</returns>
        public string? RemapJson(string behaviorId, string? json, IReadOnlyDictionary<int, int> idMap)
            => ContractOf(behaviorId) is { } contract ? EntityRefRemap.CompileJson(contract)(json, idMap) : json;

        /// <summary>
        /// ⭐ S0 item 3 (<c>docs/DESIGN_Scenario_Rules.md</c> SR-126) — RESOLVES every authored reference (<c>"@Key"</c> / a GUID) in
        /// <paramref name="json"/> for <paramref name="behaviorId"/>'s contract, in <paramref name="scope"/>: the answer is filled, the form
        /// kept (<see cref="RefResolve"/> over the one walker). ⛔ A key that names nothing throws — a scenario load fails loudly.
        /// Unknown behaviour: <paramref name="json"/> unchanged.
        /// </summary>
        public string? ResolveJson(string behaviorId, string? json, IRefScope scope)
            => ContractOf(behaviorId) is { } contract ? EntityRefRemap.WalkJson(contract, json, new RefResolve(scope)) : json;

        /// <summary>The behaviour's params contract: the registry's when it knows the behaviour, else the <see cref="Register{TDto}"/>'d one.</summary>
        private Type? ContractOf(string? behaviorId)
        {
            if (behaviorId == null) return null;
            if (_behaviors != null
                && _behaviors.TryGetId(behaviorId, out int id)
                && _behaviors.TryGetDefinition(id, out var definition)
                && definition.JsonParamsDtoType is { } contract)
                return contract;
            return _registry.TryGetValue(behaviorId, out var registered) ? registered : null;
        }
    }
}
