using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐ <c>CE-4060</c> (docs/DESIGN_Film_Production.md §4a) — a scenario's checks and their verdicts as ONE "documentation take": a key per check, each a
    /// STILL of that check's moment, framed by the <see cref="Drafter"/>'s ③ "the moment" camera (<see cref="Drafter.MomentCamera"/>) — ⛔ no framing logic here,
    /// this class only maps a check to a moment (its time, its actor and subject). The take is of THIS recording (fixed times, never promoted); rendering its
    /// still keys is the director's <c>/director/stills</c> (§4a, P3).
    /// </summary>
    public static class DocumentationTakes
    {
        /// <summary>The documentation take's name (<c>scenarios/&lt;s&gt;/takes/_doc.take.json</c>).</summary>
        public const string TakeName = "_doc";

        /// <summary>The <see cref="Take.Extra"/> key holding <c>[{still, caption}]</c> — a <see cref="CameraKey"/> carries a <c>Still</c> name but no caption.</summary>
        public const string CaptionsKey = "captions";

        private const int SlugMax = 40;

        /// <summary>
        /// The documentation take of <paramref name="checks"/> judged as <paramref name="verdicts"/> over the recording <paramref name="index"/>.
        /// </summary>
        /// <param name="idOf">A unit NAME (no wildcard) to its network id; null when the recording has no such unit.</param>
        /// <param name="nameOf">A network id to the unit's name (what the drafter's follow / over-the-shoulder cameras name).</param>
        /// <param name="unitNames">
        /// Every unit name of the scenario: the candidates of a wildcard (<c>"*Rifleman"</c> = the first by ordinal name order that contains <c>Rifleman</c>)
        /// and the cast a moment with no resolvable unit is framed over.
        /// </param>
        /// <param name="options">The drafter's options (lens, aspect, tick rate, kind of a unit); the take's name is always <see cref="TakeName"/>.</param>
        public static Take From(
            ScenarioChecks checks, IReadOnlyList<Verdict> verdicts, TimelineIndex index,
            Func<string, long?> idOf, Func<long, string?> nameOf, IReadOnlyCollection<string> unitNames, DraftOptions options)
        {
            var byName = verdicts.GroupBy(v => v.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var sorted = unitNames.OrderBy(n => n, StringComparer.Ordinal).ToList();

            long? Resolve(string? name)
            {
                if (name is null) return null;
                if (!name.StartsWith('*')) return idOf(name);
                var part = name[1..];
                foreach (var candidate in sorted)
                    if (candidate.Contains(part, StringComparison.Ordinal) && idOf(candidate) is { } id) return id;
                return null;
            }

            // a time that names another check or an action (a window's end "after-the-wound+5") is that one's time in this run
            double? NamedTime(string n) =>
                byName.TryGetValue(n, out var v) && v.Time is { } t ? t
                : checks.Do.FirstOrDefault(a => a.Name == n && a.At.IsFixed) is { Name: not null } act ? act.At.Offset
                : null;

            var moments = new List<(double Time, Expectation Check, Verdict Verdict)>();
            foreach (var e in checks.Expect)
            {
                if (!byName.TryGetValue(e.Name, out var v)) continue;   // never judged: nothing to document
                if (MomentTime(e, v, checks.RunFor, NamedTime) is { } t) moments.Add((t, e, v));
            }
            moments = moments.OrderBy(m => m.Time).ToList();   // stable: checks that share a time keep their order in checks.json

            var everyone = unitNames.Select(n => idOf(n)).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
            var keys = new List<CameraKey>();
            var captions = new JsonArray();
            for (int n = 0; n < moments.Count; n++)
            {
                var (t, check, verdict) = moments[n];
                var spec = check.Cue.Spec;
                string? actorName = spec.By ?? spec.Who;
                string? subjectName = spec.Target ?? check.Cue.Hit ?? (spec.By is not null ? spec.Who : null);   // a Who with a By is the one acted upon
                long? actor = Resolve(actorName), subject = Resolve(subjectName);
                if (actor is null) { actor = subject; subject = null; }   // only the subject is known: the shot is about it

                bool wholeScene = actor is null;
                CameraSpec camera;
                if (wholeScene)
                    camera = Drafter.GroupCamera(index, t, everyone, options);
                else
                {
                    var moment = new DraftMoment("check", -1, Math.Max(0, EventAnchors.FrameAt(index, t)), t, actor!.Value, subject, 1d);
                    camera = Drafter.MomentCamera(index, moment, nameOf, options);
                }

                string still = n.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "-" + Slug(check.Name);
                keys.Add(new CameraKey { At = new AtExpr(t), Camera = camera, Fov = camera.Fov, In = KeyTransition.Cut, Still = still });
                captions.Add(new JsonObject
                {
                    ["still"] = still,
                    ["caption"] = check.Name + (wholeScene ? " (no unit of this check found: the whole scenario)" : "") + (verdict.Ok ? " ✓" : " ✗")   // ✓ / ✗: glyphs the baked fonts carry (CE-1374 — ✅ / ⛔ drew "?"),
                });
            }

            return new Take
            {
                Scenario = checks.Scenario,
                Name = TakeName,
                TickHz = options.TickHz,
                EndAt = new AtExpr(Math.Max(checks.RunFor, keys.Count > 0 ? keys[^1].At.Offset : 0d)),
                Keys = keys,
                Extra = new JsonObject { [CaptionsKey] = captions },
            };
        }

        /// <summary>§4a's first table: the sim time of the check's moment; null when the check has none (a <c>never</c> that held).</summary>
        private static double? MomentTime(Expectation e, Verdict v, double runFor, Func<string, double?> namedTime)
        {
            switch (v.Kind)
            {
                case "never": return v.Ok ? null : v.Time;                                  // it happened: when
                case "atEnd": return runFor;                                                // judged on the last sample
                default:
                    if (v.Ok) return v.Time;                                                // it held: when
                    return v.Outside                                                        // late or early: when it DID happen
                        ?? e.Before?.Resolve(namedTime)                                     // never: where it should have happened by
                        ?? runFor;
            }
        }

        /// <summary>Lowercase ASCII letters and digits, any other run → one <c>-</c>, trimmed, at most 40 characters.</summary>
        private static string Slug(string name)
        {
            var sb = new StringBuilder();
            foreach (var ch in name.ToLowerInvariant())
            {
                if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9')) sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            var slug = sb.ToString().Trim('-');
            if (slug.Length > SlugMax) slug = slug[..SlugMax].TrimEnd('-');
            return slug.Length > 0 ? slug : "check";
        }
    }
}
