using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ CE-4326 (📄 docs/UX/DESIGN_Scenario_Presets.md §9 / §9a, R-4168) — a Preview WITH a preset, applied IN PLACE on the world the Preview runs
    /// (the edit world, unsaved edits included — the dry run's keyframe was taken just before, so its rewind puts everything back).
    /// </summary>
    /// <remarks>
    /// ⭐⭐ P1 — ONE decision, two writers: every choice is the load's own (<see cref="ScenarioVariants.IsPresent"/>, <see cref="ScenarioVariants.Pick"/>,
    /// <see cref="ScenarioParams.Resolve"/>); this class only WRITES them into a live world, where <see cref="ScenarioVariants.Apply"/> writes them into the
    /// scenario JSON. The roll key is the entity's file key = its <see cref="EntityGuid"/> (CE-4153); the authored forms are its <see cref="VariantForms"/>.
    /// ⭐ Managed components are REPLACED whole (never mutated): the keyframe holds them by reference.
    /// ⭐ Default (as authored) is never applied — a plain Preview pays nothing.
    /// </remarks>
    public static class PresetInPlace
    {
        /// <summary>What an application changed (the Preview's note and the rails read it). <see cref="Bound"/> — entities whose
        /// <see cref="ParamBindings"/> took the preset's values; <see cref="Unbound"/> — entities carrying bindings the host could not apply in place
        /// (no scenario serializer handed over — the Preview note names them).</summary>
        public readonly record struct Result(int Removed, int Placed, int MacrosChosen, string? Refused, int Bound = 0, int Unbound = 0);

        /// <summary>
        /// Applies the run setup <paramref name="argsJson"/> (a JSON object: params and the reserved <c>$variant</c>) to <paramref name="world"/>.
        /// A setup the scenario refuses (an undeclared arg, a malformed form) changes nothing and says why in <see cref="Result.Refused"/>.
        /// </summary>
        /// <param name="serializer">⭐ §9a.2 — the host's scenario serializer: the param bindings (③, paths over the scenario file's component JSON) are
        /// applied by a round trip of each bound entity through it — the SAME binder the load runs on the file. Null ⇒ nothing bound, counted in
        /// <see cref="Result.Unbound"/>.</param>
        public static Result Apply(EntityRepository world, string? argsJson, ScenarioSerializer? serializer = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(argsJson)) return default;
            int variant = ScenarioVariants.VariantOf(argsJson);
            ScenarioParams.Prepared prepared;
            try { prepared = ScenarioParams.Resolve(ScenarioDescription.HeaderOf(world), argsJson, null, "the preview's preset", variant); }
            catch (ScenarioParams.ArgsRefused r) { return new Result(0, 0, 0, r.Message); }

            var result = variant != 0 ? ApplyVariant(world, variant) : default;   // ⭐ the load's order: variant first (a removed entity binds nothing)
            try
            {
                var (bound, unbound) = ApplyParams(world, serializer, prepared.Source);
                result = result with { Bound = bound, Unbound = unbound };
            }
            catch (InvalidOperationException ex) { return result with { Refused = ex.Message }; }   // a load would refuse it too (a bad path, a missing param)
            SetResidentArgs(world, prepared.EffectiveJson);   // the expander reads the run's variant from here (a re-expanded macro rolls with it)
            return result;
        }

        private static Result ApplyVariant(EntityRepository world, int variant)
        {
            if (!world.IsComponentTypeRegistered<EntityGuid>() || !world.GetRegisteredComponentTypes().ContainsKey(typeof(VariantForms))) return default;
            var absent = new List<Entity>();
            int placed = 0, macros = 0;
            foreach (var e in world.Query().WithManaged<VariantForms>().With<EntityGuid>().Build())
            {
                var forms = world.GetManagedComponentRO<VariantForms>(e);
                if (forms == null) continue;
                string key = world.GetComponentRO<EntityGuid>(e).Value.ToString();
                if (forms.Presence is double p && !ScenarioVariants.IsPresent(variant, Guid.Empty, key, p)) { absent.Add(e); continue; }

                int positionChosen = forms.PositionChosen, macroChosen = forms.MacroChosen;
                if (forms.PositionsJson != null && ScenarioVariants.PositionsOf(JsonNode.Parse(forms.PositionsJson)) is { Count: > 0 } places)
                {
                    positionChosen = ScenarioVariants.Pick(places.ConvertAll(x => x.Weight), variant, Guid.Empty, key, "position");
                    var place = places[positionChosen];
                    var t = world.HasComponent<SimTransform>(e) ? world.GetComponentRO<SimTransform>(e) : new SimTransform { Rotation = new WorldRotation(0, 0, 0, 1) };
                    t.Position = new WorldPoint((float)place.At[0], (float)place.At[1], (float)place.At[2]);
                    if (place.Heading is double h) t.Rotation = WorldRotation.HeadingDegToQuaternion((float)h);
                    if (world.HasComponent<SimTransform>(e)) world.SetComponent(e, t); else world.AddComponent(e, t);
                    placed++;
                }
                if (forms.MacroChoicesJson != null && world.HasManagedComponent<MacroInstance>(e)
                    && ScenarioVariants.ChoicesOf(JsonNode.Parse(forms.MacroChoicesJson)) is { Count: > 0 } choices)
                {
                    var weights = new List<double>(choices.Count);
                    foreach (var c in choices) weights.Add(c.Weight);
                    macroChosen = ScenarioVariants.Pick(weights, variant, Guid.Empty, key, "macro");
                    var mi = world.GetManagedComponentRO<MacroInstance>(e)!;
                    if (!string.Equals(mi.Macro, choices[macroChosen].Macro, StringComparison.Ordinal))
                    {
                        world.SetManagedComponent(e, mi with { Macro = choices[macroChosen].Macro });   // the reconcile re-expands it by GUID (SR-150)
                        macros++;
                    }
                }
                world.SetManagedComponent(e, forms with { Variant = variant, PositionChosen = positionChosen, MacroChosen = macroChosen });
            }
            foreach (var e in absent) world.DestroyEntity(e);
            return new Result(absent.Count, placed, macros, null);
        }

        /// <summary>
        /// ⭐ §9a.2 — ③ in place: each entity with <see cref="ParamBindings"/> is serialized (the saveable components, as a save writes them), bound by
        /// <see cref="ParamBinder.ApplyBindings"/> (the load's own call), and ONLY the components whose JSON changed are injected back — as NEW values, so a
        /// managed component the keyframe holds by reference is never touched (the rewind restores it).
        /// </summary>
        private static (int Bound, int Unbound) ApplyParams(EntityRepository world, ScenarioSerializer? serializer, IParamSource source)
        {
            if (!world.IsComponentTypeRegistered<EntityGuid>() || !world.GetRegisteredComponentTypes().ContainsKey(typeof(ParamBindings))) return default;
            var targets = new List<Entity>();
            foreach (var e in world.Query().WithManaged<ParamBindings>().With<EntityGuid>().Build()) targets.Add(e);
            if (targets.Count == 0) return default;
            if (serializer == null) return (0, targets.Count);

            var resolver = ScenarioSerializer.ResolverFor(world);
            var mask = world.GetSaveableMask();
            int bound = 0;
            foreach (var e in targets)
            {
                var node = serializer.SerializeEntity(world, e, resolver, mask);
                var before = (JsonObject)node.DeepClone();
                if (!ParamBinder.ApplyBindings(node, source)) continue;
                var changed = new JsonObject();
                foreach (var (k, v) in node)
                    if (!JsonNode.DeepEquals(before[k], v)) changed[k] = v?.DeepClone();
                string key = world.GetComponentRO<EntityGuid>(e).Value.ToString();
                serializer.DeserializeWith(world, new JsonObject { ["Entities"] = new JsonObject { [key] = changed } }, resolver,
                                           new Dictionary<string, Entity> { [key] = e });
                bound++;
            }
            return (bound, 0);
        }

        private static void SetResidentArgs(EntityRepository world, string effective)
        {
            if (!world.HasSingletonManaged<ScenarioDescriptionResident>()) return;
            var r = world.GetSingletonManaged<ScenarioDescriptionResident>()!;
            world.SetSingletonManaged(new ScenarioDescriptionResident { Text = r.Text, Name = r.Name, Hash = r.Hash, Args = effective, HeaderJson = r.HeaderJson });
        }
    }
}
