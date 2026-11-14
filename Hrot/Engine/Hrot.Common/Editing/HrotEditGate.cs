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
    /// installed by the shared bootstrapper on every host, the level read from the <c>station.seat</c> feature (H1). ⭐ Finer rules (what an
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
        /// <remarks>⭐ The level bounds EDITING kinds only — an interaction (possess, embark) or an order is a station's ordinary use, judged by the
        /// action catalogue's per-action level (H2), never refused here for a Player.</remarks>
        public string? Refuse(EntityRepository repo, EditOp op)
            => op.Kind is EditKind.Interact or EditKind.Command ? (Mode == EditingMode.Review ? Edits.ReplayRefusal : null) : Reason(Mode, Level);

        /// <summary>⭐ Seats H2 — the Instructor seat's refusal in Edit mode.</summary>
        public const string InstructorDoesNotAuthor = "this station is an instructor: it runs the exercise, it does not author the scenario";

        /// <summary>The refusal for a station in <paramref name="mode"/> at <paramref name="level"/>; null when it may edit.</summary>
        public static string? Reason(EditingMode mode, EditingLevel level)
        {
            if (mode == EditingMode.Review) return Edits.ReplayRefusal;
            return level switch
            {
                // ⭐ Seats H2 (R-4164: "An Instructor in Edit mode sees the scenario but cannot change it") — the instructor edits a RUNNING exercise
                //   (injects, heal / damage, bring in units), never the scenario itself
                EditingLevel.Instructor when mode == EditingMode.Edit => InstructorDoesNotAuthor,
                // ⭐ Seats H2 (§2.2: Controller = "orders to its side") — a side controller orders (an EditKind.Command), it does not edit
                EditingLevel.Controller => "this station is a side controller: it gives orders, it does not edit",
                EditingLevel.Observer => "this station is an observer",
                EditingLevel.None => "this station's seat does not edit",
                _ => null,
            };
        }

        /// <summary>D4 — a seat's editing level (Seats and hats §2.2).</summary>
        public static EditingLevel LevelOf(Hrot.Common.Infrastructure.Seat seat) => seat switch
        {
            Hrot.Common.Infrastructure.Seat.Author => EditingLevel.Full,
            Hrot.Common.Infrastructure.Seat.Instructor => EditingLevel.Instructor,
            Hrot.Common.Infrastructure.Seat.SideController => EditingLevel.Controller,
            Hrot.Common.Infrastructure.Seat.Observer => EditingLevel.Observer,
            _ => EditingLevel.None,   // Player, Reviewer
        };

        /// <summary>Installs a gate on <paramref name="world"/>.</summary>
        public static HrotEditGate Install(EntityRepository world, Func<EditingMode> mode, Func<EditingLevel>? level = null)
        {
            var gate = new HrotEditGate(mode, level);
            EditGate.Install(world, gate);
            return gate;
        }
    }
}
