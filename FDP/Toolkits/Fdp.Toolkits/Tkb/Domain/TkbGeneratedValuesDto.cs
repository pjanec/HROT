using System.Collections.Generic;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// ⭐ Buildings programme Stage 0 — which of a template's numbers a BUILDER derived by formula rather than an author
    /// stating them, keyed by <see cref="Parameters.ParameterNames"/> (e.g. <c>Health.Max</c> → <c>"armourFront × 5
    /// (armourFront = 100)"</c>). <see cref="Parameters.ParameterResolver"/> reports such a value as
    /// <c>Generated</c> instead of <c>Explicit</c>. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §2a (provenance
    /// <c>Generated(formula, inputs)</c>).
    /// <para>⛔ Not a file descriptor: an authored TKB file states numbers, it does not derive them.</para>
    /// </summary>
    public sealed record TkbGeneratedValuesDto
    {
        /// <summary>Parameter name → the formula with its inputs.</summary>
        public Dictionary<string, string> Formulas { get; init; } = new();
    }
}
