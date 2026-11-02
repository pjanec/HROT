using System;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Scenario
{
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ MACROS S3a ③ — the three components of a macro INSTANCE (docs/DESIGN_Scenario_Macros.md §14 SR-170, SR-175, SR-178).
    // CLAUDE.md "NEW COMPONENTS": a RECORDED component is an unmanaged struct; a managed one is an immutable record replaced whole.
    // Wire twins (R-134): dtMacroInstance 158 · dtMacroExpansionStatus 159 · dtMacroOrigin 170 (Hrot.Network.NED, MacroTranslators.cs).
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ SR-170 — a macro INSTANCE: the marker entity that says "expand <see cref="Macro"/> here with <see cref="ArgsJson"/>".
    /// ⭐ Managed, IMMUTABLE (every property <c>init</c>): a change builds a NEW instance and <c>SetManagedComponent</c>s it — ⛔ never a field
    /// mutated in place (change detection is the chunk version; an in-place write is never recorded). Written once, so the recorder holds it at
    /// keyframes only. <c>DataPolicy.Default</c>: SAVED (by <c>MacroInstanceTranslator</c>) and recorded.
    /// </summary>
    [ComponentId(GlobalComponentIds.MacroInstance)]
    [DataPolicy(DataPolicy.Default)]
    public sealed record MacroInstance
    {
        /// <summary>The scenario-file component name.</summary>
        public const string ComponentName = "MacroInstance";

        /// <summary>The macro's path under <c>macros/</c> (<c>"test/fire-team"</c>) — the file is <c>macros/&lt;path&gt;/scenario.json</c>.</summary>
        public string Macro { get; init; } = string.Empty;

        /// <summary>The instance's args: a JSON OBJECT (<c>"{}"</c> = none), overlaid key by key on the macro header's param defaults (SR-28).</summary>
        public string ArgsJson { get; init; } = "{}";
    }

    /// <summary>⭐ SR-175 — where an instance's expansion stands.</summary>
    public enum MacroExpansionState : byte { None = 0, Expanding = 1, Done = 2, Failed = 3 }

    /// <summary>
    /// ⭐ SR-175 — the expander's result on the instance entity. Unmanaged. <c>NoScenario</c>: runtime state, never saved — a loaded instance
    /// re-expands. Recorded (a replay shows the verdict without re-running the expander).
    /// </summary>
    [ComponentId(GlobalComponentIds.MacroExpansionStatus)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct MacroExpansionStatus
    {
        public MacroExpansionState State;

        /// <summary>Why <see cref="MacroExpansionState.Failed"/> (empty otherwise).</summary>
        public FixedString128 Reason;
    }

    /// <summary>
    /// ⭐ SR-178 — stamped on every entity a macro expansion creates: which instance made it and which macro-local file key it came from.
    /// Unmanaged. <c>NoScenario</c> (its entity is transient anyway). Recorded.
    /// </summary>
    [ComponentId(GlobalComponentIds.MacroOrigin)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct MacroOrigin
    {
        /// <summary>The <see cref="MacroInstance"/> entity (its network id).</summary>
        public EntityRef Instance;

        /// <summary>The entity's key in the macro file (<c>EntityGuid</c> inside the macro).</summary>
        public Guid Local;
    }
}
