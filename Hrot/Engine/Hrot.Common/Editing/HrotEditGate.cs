using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;

namespace Hrot.Common.Editing
{
    /// <summary>
    /// ⭐ Edit framework F1 (📄 docs/UX/DESIGN_Edit_Framework.md §2.1–§2.2, R-4126) — the station's gate: refuses an edit before it is sent when the
    /// station's MODE or editing LEVEL forbids it, with the whole reason. The two are read through providers so the host decides where they come from (the
    /// mode from the run state / replay, the level from the seat — seats H1/H2).
    /// <para>F1 rules: Review mode refuses every edit (a recording is read-only — ⭐ and <c>Edits.Submit</c> already refuses it on every host from
    /// <c>ReplayState</c>, gate or not); an Observer or a no-editing seat refuses every edit. ⚠ Installed by the seat (H1/H2, B-15/B-16) — until then no host
    /// installs it and every station edits at level Full. ⭐ Finer rules (what an
    /// Instructor or a side Controller may do) belong to the action catalogue's <c>Level</c> per action (B-11, H2), never a second table here.</para>
    /// </summary>
    public sealed class HrotEditGate : IEditGate, IEditGateMode
    {
        private readonly Func<EditingMode> _mode;
        private readonly Func<EditingLevel> _level;

        public HrotEditGate(Func<EditingMode> mode, Func<EditingLevel>? level = null)
        {
            _mode = mode ?? throw new ArgumentNullException(nameof(mode));
            _level = level ?? (static () => EditingLevel.Full);
        }

        public EditingMode Mode => _mode();
        public EditingLevel Level => _level();
        public string ModeText => Mode.ToString();

        /// <inheritdoc/>
        public string? Refuse(EntityRepository repo, EditOp op) => Reason(Mode, Level);

        /// <summary>The refusal for a station in <paramref name="mode"/> at <paramref name="level"/>; null when it may edit.</summary>
        public static string? Reason(EditingMode mode, EditingLevel level)
        {
            if (mode == EditingMode.Review) return Edits.ReplayRefusal;
            return level switch
            {
                EditingLevel.Observer => "this station is an observer",
                EditingLevel.None => "this station's seat does not edit",
                _ => null,
            };
        }

        /// <summary>Installs a gate on <paramref name="world"/>.</summary>
        public static HrotEditGate Install(EntityRepository world, Func<EditingMode> mode, Func<EditingLevel>? level = null)
        {
            var gate = new HrotEditGate(mode, level);
            EditGate.Install(world, gate);
            return gate;
        }
    }
}
