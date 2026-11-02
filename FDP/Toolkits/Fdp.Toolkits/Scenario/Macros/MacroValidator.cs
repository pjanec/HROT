using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ Macros S3a ⑥ — THE VALIDATOR (docs/DESIGN_Scenario_Macros.md SR-149, SR-161, SR-162, SR-156): run on a scenario's JSON (or a macro's)
    /// BEFORE anything is created — inside the load's PREPARE, so a bad macro aborts the load on every node with nothing half-built.
    /// It walks every macro instance, recursively through the macro files, and reports every problem with its path
    /// (<c>"Blue Platoon / Squad 2: macro 'FireTeam' not found"</c>):
    /// <list type="bullet">
    ///   <item>the file exists and is a macro (<c>header.Kind</c>); no cycle; nesting ≤ <see cref="MaxDepth"/>;</item>
    ///   <item>every ARG names a param the macro declares;</item>
    ///   <item>inside each macro file, every binding — a <c>"^name"</c> value, a node's <c>Bind</c> map, a <see cref="ParamBindings"/> path — names
    ///     a declared param (SR-162); a <c>bind</c> key inside a params blob is refused (SR-161: the typed form would drop it);</item>
    ///   <item>a pinned macro (<c>Relocatable: false</c>) stands at its authored anchor (SR-156).</item>
    /// </list>
    /// ⛔ Pure JSON: no world, no extractor. ⚠ It does not check a value's TYPE against its target field (SR-162's second half) — the binder
    /// reports that loudly at expansion.
    /// </summary>
    public static class MacroValidator
    {
        public const int MaxDepth = 8;

        /// <summary>Every problem in <paramref name="json"/> (a scenario or a macro); empty = valid.</summary>
        public static IReadOnlyList<string> Validate(string json, IMacroLibrary library, string scope = "the scenario")
        {
            var errors = new List<string>();
            JsonObject? dom;
            try { dom = JsonNode.Parse(json) as JsonObject; }
            catch (JsonException ex) { return new[] { $"{scope}: not JSON ({ex.Message})" }; }
            if (dom is null) return new[] { $"{scope}: not a JSON object" };

            // a scenario's own bindings name the scenario's params (SR-41); a macro's, the macro's
            var header = ScenarioHeaderJson.Read(dom);
            CheckFile(dom, header, scope, library, new List<string>(), errors, isMacro: header?.IsMacro == true);
            return errors;
        }

        private static void CheckFile(JsonObject dom, ScenarioHeader? header, string here, IMacroLibrary library, List<string> path,
                                      List<string> errors, bool isMacro)
        {
            var declared = new HashSet<string>((header?.Params ?? Array.Empty<ScenarioParamDecl>()).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            if ((dom["entities"] ?? dom["Entities"]) is not JsonObject entities) return;

            foreach (var (key, node) in entities)
            {
                if (node is not JsonObject entity) continue;
                string who = $"{here} / {NameOf(entity, key)}";

                // ── bindings in this file name a declared param (SR-162) — a scenario with no params may still bind nothing
                foreach (var (bpath, param) in ParamBinder.BindingsOf(entity))
                    if (!declared.Contains(param)) errors.Add($"{who}: ParamBindings '{bpath}' names '{param}', not a param of {Describe(isMacro)}");
                foreach (var (where, param) in ParamForms(entity))
                    if (!declared.Contains(param)) errors.Add($"{who}: '{where}' is bound to '^{param}', not a param of {Describe(isMacro)}");

                // ── a nested instance: its file, its args, its depth, its pin — then into it
                if (FindKey(entity, MacroInstance.ComponentName) is { } mk && entity[mk] is JsonObject mi)
                    CheckInstance(entity, mi, who, library, path, errors);
            }
        }

        private static void CheckInstance(JsonObject entity, JsonObject mi, string who, IMacroLibrary library, List<string> path, List<string> errors)
        {
            // ⭐ macros S3b B2 — a one-of-N instance: EVERY choice is checked (any variant may pick it)
            var choices = ScenarioVariants.ChoicesOf(mi["Macro"] ?? mi["macro"]);
            if (choices.Count == 0) { errors.Add($"{who}: the instance names no macro"); return; }
            if (choices.Count > 1) { foreach (var c in choices) CheckOne(entity, mi, c.Macro, $"{who} (choice '{c.Macro}')", library, path, errors); return; }
            CheckOne(entity, mi, choices[0].Macro, who, library, path, errors);
        }

        private static void CheckOne(JsonObject entity, JsonObject mi, string macro, string who, IMacroLibrary library, List<string> path, List<string> errors)
        {
            if (path.Contains(macro, StringComparer.Ordinal)) { errors.Add($"{who}: macro cycle {string.Join(" → ", path)} → {macro}"); return; }
            if (path.Count >= MaxDepth) { errors.Add($"{who}: macros nested deeper than {MaxDepth}"); return; }
            if (!library.TryRead(macro, out var json, out var why)) { errors.Add($"{who}: {why}"); return; }

            JsonObject? dom;
            try { dom = JsonNode.Parse(json) as JsonObject; }
            catch (JsonException ex) { errors.Add($"{who}: macro '{macro}' is not JSON ({ex.Message})"); return; }
            var header = dom is null ? null : ScenarioHeaderJson.Read(dom);
            if (dom is null || header is null || !header.IsMacro) { errors.Add($"{who}: '{macro}' is not a macro (its header has no Kind \"macro\")"); return; }

            var declared = new HashSet<string>((header.Params ?? Array.Empty<ScenarioParamDecl>()).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            if (mi["Args"] is JsonObject args)
                foreach (var (name, _) in args)
                    if (!declared.Contains(name)) errors.Add($"{who}: arg '{name}' is not a param of macro '{macro}' (declared: {string.Join(", ", declared)})");

            if (!header.Relocatable && header.Anchor is { } anchor && PositionOf(entity) is { } at
                && ((at.X - anchor.Position.X) * (at.X - anchor.Position.X) + (at.Y - anchor.Position.Y) * (at.Y - anchor.Position.Y)) > 0.25f)
                errors.Add($"{who}: macro '{macro}' is pinned at its anchor ({anchor.Position}) and may not be placed elsewhere");

            path.Add(macro);
            try { CheckFile(dom, header, $"{who} [{macro}]", library, path, errors, isMacro: true); }
            finally { path.RemoveAt(path.Count - 1); }
        }

        /// <summary>Every <c>"^name"</c> value and every <c>Bind</c> map entry in an entity's JSON — inside params blobs too — plus a
        /// <c>bind</c> key at a params blob's root (refused, SR-161, reported as the param <c>"bind"</c> → never declared).</summary>
        private static IEnumerable<(string Where, string Param)> ParamForms(JsonObject entity)
        {
            var found = new List<(string, string)>();
            foreach (var (comp, node) in entity)
                if (comp != ParamBindings.ComponentName) Walk(node, comp, found, inBlob: false);
            return found;
        }

        private static void Walk(JsonNode? node, string where, List<(string, string)> found, bool inBlob)
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var (k, v) in o)
                    {
                        if (k.Equals("Bind", StringComparison.OrdinalIgnoreCase))
                        {
                            if (v is JsonObject bind)
                                foreach (var (prop, p) in bind)
                                    if (p is JsonValue pv && pv.TryGetValue<string>(out var ps)) found.Add(($"{where}.Bind.{prop}", ps));
                            continue;
                        }
                        Walk(v, $"{where}.{k}", found, inBlob);
                    }
                    break;
                case JsonArray a:
                    for (int i = 0; i < a.Count; i++) Walk(a[i], $"{where}[{i}]", found, inBlob);
                    break;
                case JsonValue val when val.TryGetValue<string>(out var text):
                    if (text.Length > 1 && text[0] == '^') found.Add((where, text.Substring(1)));
                    else if (!inBlob && text.TrimStart().StartsWith('{'))
                    {
                        JsonNode? blob = null;
                        try { blob = JsonNode.Parse(text); } catch (JsonException) { }
                        if (blob is JsonObject bo)
                        {
                            foreach (var (k, _) in bo)
                                if (k.Equals("bind", StringComparison.Ordinal))   // the untyped SR-129 form: a typed form drops it on save
                                    found.Add(($"{where} (a 'bind' key inside params is refused — use ParamBindings, SR-161)", "\u0000bind"));
                            Walk(bo, where, found, inBlob: true);
                        }
                    }
                    break;
            }
        }

        private static WorldPoint? PositionOf(JsonObject entity)
            => FindKey(entity, "SimTransform") is { } k && entity[k] is JsonObject st && st["Position"] is JsonArray p && p.Count >= 2
               ? new WorldPoint(p[0]!.GetValue<float>(), p[1]!.GetValue<float>(), p.Count > 2 ? p[2]!.GetValue<float>() : 0f) : null;

        private static string NameOf(JsonObject entity, string key)
            => FindKey(entity, "EntityInfo") is { } k && entity[k] is JsonObject info && info["Name"] is JsonValue n && n.TryGetValue<string>(out var s) && s.Length > 0
               ? s : key;

        private static string Describe(bool isMacro) => isMacro ? "the macro" : "the scenario";

        private static string? FindKey(JsonObject obj, string name)
        {
            foreach (var (k, _) in obj) if (k.Equals(name, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }
    }
}
