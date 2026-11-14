using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Common.Editing
{
    /// <summary>
    /// ⭐⭐ B-11 A3 (docs/UX/DESIGN_Action_Catalogue.md §8; §3 row 9, §5 Q1 "one derived EditingMode, computed in one place") — THE station's
    /// <see cref="EditingMode"/>, read by the edit gate AND the action catalogue, so a menu and the gate can never disagree about the mode.
    /// <para>Installed on the world by <c>SharedApplicationBootstrapper</c> (every bootstrapped host, by construction), from the cluster's state
    /// and the world's replay flag. A world with no source (a host outside the bootstrapper, a rail) answers <see cref="Fallback"/>.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.StationMode)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]   // a service object, not world state (CE-3299 D7)
    public sealed class StationMode
    {
        private readonly Func<EditingMode> _current;
        private readonly Func<Hrot.Common.Infrastructure.Seat> _seat;
        private readonly Func<ForceId?> _side;

        private StationMode(Func<EditingMode> current, Func<Hrot.Common.Infrastructure.Seat>? seat, Func<ForceId?>? side)
        {
            _current = current ?? throw new ArgumentNullException(nameof(current));
            _seat = seat ?? (static () => Hrot.Common.Infrastructure.Seat.Author);
            _side = side ?? (static () => null);
        }

        /// <summary>The mode now.</summary>
        public EditingMode Current => _current();

        /// <summary>⭐ Seats H2 (📄 docs/UX/DESIGN_Seats_And_Hats.md §3.2) — the station's SEAT now (<c>station.seat</c>, read live).</summary>
        public Hrot.Common.Infrastructure.Seat Seat => _seat();

        /// <summary>⭐ Seats H2 — the seat's editing level (D4): what the gate AND the action catalogue bound by.</summary>
        public EditingLevel Level => HrotEditGate.LevelOf(_seat());

        /// <summary>⭐ Seats H2 — the seat's side scope: a side controller's / player's side, null = every side.</summary>
        public ForceId? Side => _side();

        /// <summary>Installs the station's mode (and seat) source on <paramref name="world"/> (replacing any earlier one). No seat ⇒ Author, every side.</summary>
        public static StationMode Install(EntityRepository world, Func<EditingMode> current,
                                          Func<Hrot.Common.Infrastructure.Seat>? seat = null, Func<ForceId?>? side = null)
        {
            var source = new StationMode(current, seat, side);
            world.SetSingletonManaged(source);
            return source;
        }

        /// <summary>⭐ Seats H2 — the station's level on <paramref name="view"/>; <see cref="EditingLevel.Full"/> where no source is installed (today's behaviour).</summary>
        public static EditingLevel LevelOf(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<StationMode>() ? repo.GetSingletonManaged<StationMode>()!.Level : EditingLevel.Full;

        /// <summary>⭐ Seats H6 — the station's seat on <paramref name="view"/>; <see cref="Hrot.Common.Infrastructure.Seat.Author"/> where no source is installed.</summary>
        public static Hrot.Common.Infrastructure.Seat SeatOf(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<StationMode>() ? repo.GetSingletonManaged<StationMode>()!.Seat : Hrot.Common.Infrastructure.Seat.Author;

        /// <summary>⭐ Seats H2 — the station's side scope on <paramref name="view"/>; null (every side) where no source is installed.</summary>
        public static ForceId? SideOf(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<StationMode>() ? repo.GetSingletonManaged<StationMode>()!.Side : null;

        /// <summary>⭐ Seats H2 — a seat config's side text as a <see cref="ForceId"/>: the force names (Friend · Hostile · Neutral) or their colours
        /// (Blue · Red · Green, the design's words); empty or unknown ⇒ null (every side).</summary>
        public static ForceId? ParseSide(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            if (t.Equals("Blue", StringComparison.OrdinalIgnoreCase)) return ForceId.Friend;
            if (t.Equals("Red", StringComparison.OrdinalIgnoreCase)) return ForceId.Hostile;
            if (t.Equals("Green", StringComparison.OrdinalIgnoreCase)) return ForceId.Neutral;
            return Enum.TryParse<ForceId>(t, ignoreCase: true, out var f) && Enum.IsDefined(f) ? f : null;
        }

        /// <summary>
        /// ⭐ The one rule: a world showing a recording is <see cref="EditingMode.Review"/>; otherwise the cluster's state says it — Preview and Live
        /// by their own states, a Replay state Review, and Idle or Edit <see cref="EditingMode.Edit"/>.
        /// </summary>
        public static EditingMode From(ClusterState state, bool showsRecording)
        {
            if (showsRecording) return EditingMode.Review;
            return state switch
            {
                ClusterState.LoadingPreview or ClusterState.OperatingPreview or ClusterState.UnloadingPreview => EditingMode.Preview,
                ClusterState.LoadingLive or ClusterState.OperatingLive or ClusterState.UnloadingLive => EditingMode.Live,
                ClusterState.LoadingReplay or ClusterState.OperatingReplay or ClusterState.UnloadingReplay => EditingMode.Review,
                _ => EditingMode.Edit,
            };
        }

        /// <summary>The station's mode on <paramref name="view"/>: its installed source, else <see cref="Fallback"/>.</summary>
        public static EditingMode Of(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonManaged<StationMode>() ? repo.GetSingletonManaged<StationMode>()!.Current : Fallback(view);

        /// <summary>
        /// ⚠ A world with no installed source: Review while it shows a recording, else <see cref="EditingMode.Live"/> where the map offers orders
        /// (every map host — today's behaviour, A1), else <see cref="EditingMode.Edit"/>.
        /// </summary>
        public static EditingMode Fallback(ISimulationView view)
            => Fdp.Toolkit.Replay.ReplayState.IsActive(view) ? EditingMode.Review
             : Hrot.Common.Orders.OrdersOffered.IsOfferedIn(view) ? EditingMode.Live
             : EditingMode.Edit;
    }
}
