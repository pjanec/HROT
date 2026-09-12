using Fdp.Core;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// <see cref="ISimClock"/> over a live <see cref="EntityRepository"/>.
    ///
    /// <para><b>Nothing here is cached.</b> Every property re-reads the world's <c>GlobalTime</c>
    /// singleton at the moment it is asked, because the design's whole complaint about the existing
    /// dozen pause flags is that they are latched copies that drift from the thing they describe.
    /// A clock that snapshotted on construction would be the same mistake in a new file.</para>
    ///
    /// <para>A null repository, or a world with no <c>GlobalTime</c> singleton yet, reports halted
    /// with zeroed values — the state of a simulation nobody has started.</para>
    /// </summary>
    public readonly struct WorldSimClock : ISimClock
    {
        private readonly EntityRepository? _world;

        internal WorldSimClock(EntityRepository? world) => _world = world;

        /// <summary>
        /// The singleton as of right now. Guarded rather than assumed: the clock is read from UI
        /// paths that run before the first tick has pushed anything.
        /// </summary>
        private GlobalTime Current =>
            _world != null && _world.HasSingletonUnmanaged<GlobalTime>()
                ? _world.GetSingletonUnmanaged<GlobalTime>()
                : default;

        /// <inheritdoc />
        public bool IsAdvancing => Current.IsAdvancing;

        /// <inheritdoc />
        public bool IsHalted => !Current.IsAdvancing;

        /// <inheritdoc />
        public double TotalTime => Current.TotalTime;

        /// <inheritdoc />
        public float TimeScale => Current.TimeScale;

        /// <inheritdoc />
        public long FrameNumber => Current.FrameNumber;
    }
}
