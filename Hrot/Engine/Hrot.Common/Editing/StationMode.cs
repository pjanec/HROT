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

        private StationMode(Func<EditingMode> current) => _current = current ?? throw new ArgumentNullException(nameof(current));

        /// <summary>The mode now.</summary>
        public EditingMode Current => _current();

        /// <summary>Installs the station's mode source on <paramref name="world"/> (replacing any earlier one).</summary>
        public static StationMode Install(EntityRepository world, Func<EditingMode> current)
        {
            var source = new StationMode(current);
            world.SetSingletonManaged(source);
            return source;
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
