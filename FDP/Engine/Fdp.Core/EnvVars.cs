using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Fdp.Core
{
    /// <summary>⭐ CE-4586 — what a variable is FOR (docs/DESIGN_Debug_Api_Http_Only.md §6): <c>/help</c> lists them grouped by this.</summary>
    public enum EnvCategory
    {
        /// <summary>Changes what the product does (a performance switch, a feature, a path) — set it when you start the process.</summary>
        Switch,
        /// <summary>Prints, traces or checks something for a person investigating; the product behaves the same. Experiment leftovers live here, said so in their purpose.</summary>
        Diagnostic,
        /// <summary>Read by a test or a rail only (a fixture's knob, where a rail writes its frames).</summary>
        Test,
        /// <summary>Read by a tool or script around the product (a generator, a measuring harness).</summary>
        Tool,
    }

    /// <summary>⭐ CE-4586 — one environment variable: its name, category, value form, default, one line of purpose and the design section that owns it.</summary>
    public sealed record EnvVar(string Name, EnvCategory Category, string Type, string Default, string Purpose, string Design);

    /// <summary>
    /// ⭐⭐⭐ CE-4586 (docs/DESIGN_Debug_Api_Http_Only.md §6) — <b>THE one table of the environment variables</b> every HROT / FDP process reads.
    /// Every variable is declared here once (a <c>public static readonly EnvVar</c> field), and every read goes through <see cref="Read"/>;
    /// <c>EveryEnvReadIsRegisteredTests</c> fails on a raw <c>GetEnvironmentVariable</c> outside this file (bar the projects that cannot reference
    /// <c>Fdp.Core</c>) and on an <c>HROT_* / FDP_*</c> name used but not declared. <c>GET /help</c> and <c>--help-api</c> render it by category
    /// (<see cref="RenderHelp"/>); <c>GET /performance</c> lists the ones this process received (<see cref="Received"/>).
    /// <para>⚠ A read is what it always was — <see cref="Environment.GetEnvironmentVariable(string)"/> — so a hot path keeps caching the parsed value in
    /// a static, as before (R-2084). Language-neutral (R-4221): numbers parse with the invariant culture.</para>
    /// <para>📌 The declarations are in <c>EnvVars.Declarations.cs</c> (one partial), grouped by area.</para>
    /// </summary>
    public static partial class EnvVars
    {
        private static EnvVar[]? s_all;

        /// <summary>Every declared variable, by name (the static <see cref="EnvVar"/> fields of this class — no second list to keep).</summary>
        public static IReadOnlyList<EnvVar> All => s_all ??= typeof(EnvVars)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(EnvVar))
            .Select(f => (EnvVar)f.GetValue(null)!)
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .ToArray();

        /// <summary>The declared variable named <paramref name="name"/>, or null.</summary>
        public static EnvVar? Find(string name)
        {
            foreach (var v in All) if (string.Equals(v.Name, name, StringComparison.Ordinal)) return v;
            return null;
        }

        /// <summary>The raw value this process received, or null — exactly <c>Environment.GetEnvironmentVariable(name)</c>.</summary>
        public static string? Read(EnvVar v) => Environment.GetEnvironmentVariable(v.Name);

        /// <summary>Set to anything non-empty.</summary>
        public static bool IsSet(EnvVar v) => !string.IsNullOrEmpty(Read(v));

        /// <summary>The value parsed as an integer (invariant culture), or <paramref name="fallback"/>.</summary>
        public static int Int(EnvVar v, int fallback)
            => int.TryParse(Read(v), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

        /// <summary>The value parsed as a double (invariant culture), or <paramref name="fallback"/>.</summary>
        public static double Double(EnvVar v, double fallback)
            => double.TryParse(Read(v), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;

        /// <summary>The variables this process was started with (name → value), declared ones only, by name.</summary>
        public static IReadOnlyList<(EnvVar Var, string Value)> Received()
        {
            var list = new List<(EnvVar, string)>();
            foreach (var v in All) if (Read(v) is { Length: > 0 } value) list.Add((v, value));
            return list;
        }

        /// <summary>The <c>/help</c> section: every variable, grouped by <see cref="EnvCategory"/> (markdown).</summary>
        public static string RenderHelp()
        {
            var sb = new StringBuilder(16 * 1024);
            sb.Append("## 8. Environment variables — read once, at process start\n\n")
              .Append("Set them when you START the process (`HROT_PARALLEL_SIM=0 dotnet Hrot.ClusterRunner.dll …`); a running process does not re-read them. ")
              .Append("`GET /performance` shows which this process received. Generated from `Fdp.Core.EnvVars`, the one table.\n");
            foreach (EnvCategory c in Enum.GetValues(typeof(EnvCategory)))
            {
                var inCat = All.Where(v => v.Category == c).ToList();
                if (inCat.Count == 0) continue;
                sb.Append("\n### ").Append(c).Append(" (").Append(inCat.Count.ToString(CultureInfo.InvariantCulture)).Append(")\n\n");
                foreach (var v in inCat)
                {
                    sb.Append("- `").Append(v.Name).Append("` (").Append(v.Type);
                    if (!string.IsNullOrEmpty(v.Default)) sb.Append(", default ").Append(v.Default);
                    sb.Append(") — ").Append(v.Purpose);
                    if (!string.IsNullOrEmpty(v.Design)) sb.Append(" · ").Append(v.Design);
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
