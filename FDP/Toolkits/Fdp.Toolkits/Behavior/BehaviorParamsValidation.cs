using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2377</c> — ONE side-effect-free check of a behaviour's authored params: is the behaviour known, does the JSON read STRICTLY into
    /// its params DTO (an unknown key is a problem), and does its optional <see cref="BehaviorValidatorAttribute"/> method accept it.
    /// ⛔ No resolver call and no world access — a resolver may write the world and read entities that do not exist at scenario-load prepare.
    /// 📄 Blueprint_Issues_Tracker.md CE-2377.
    /// </summary>
    public static class BehaviorParamsValidation
    {
        /// <summary>The params options (<see cref="BehaviorParams.JsonOptions"/> = <c>DefaultRelaxed</c>) with ONE named delta: an unknown key is an
        /// error (CE-2377). Made through <c>FdpJsonOptionsRegistry.Derive</c> — the one legal way to make a variant (CE-3187).</summary>
        private static readonly JsonSerializerOptions Strict =
            Fdp.Core.Serialization.FdpJsonOptionsRegistry.Derive(o => o.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

        /// <summary>The problems with <paramref name="paramsJson"/> for <paramref name="behaviorName"/> (empty = fine; null / blank params are fine).</summary>
        public static IReadOnlyList<string> Validate(BehaviorRegistry registry, string behaviorName, string? paramsJson)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var problems = new List<string>();
            if (!registry.TryGetId(behaviorName ?? "", out var id) || !registry.TryGetDefinition(id, out var def))
            {
                problems.Add($"unknown behaviour '{behaviorName}'");
                return problems;
            }
            if (string.IsNullOrWhiteSpace(paramsJson)) return problems;

            if (def.JsonParamsDtoType is { } dto)
            {
                try { JsonSerializer.Deserialize(paramsJson, dto, Strict); }
                catch (JsonException ex) { problems.Add($"params do not read into {dto.Name}: {ex.Message}"); }
                catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FormatException or ArgumentException)
                { problems.Add($"params do not read into {dto.Name}: {ex.GetType().Name}: {ex.Message}"); }
            }

            if (registry.TryGetValidator(behaviorName!, out var validator))
            {
                try { foreach (var p in validator(paramsJson)) problems.Add(p); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { problems.Add($"{behaviorName}: its validator threw {ex.GetType().Name}: {ex.Message}"); }
            }
            return problems;
        }
    }
}
