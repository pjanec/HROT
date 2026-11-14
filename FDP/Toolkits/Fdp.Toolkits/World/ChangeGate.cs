using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.World
{
    /// <summary>What <see cref="ChangeGate.Next"/> tells a system about this frame.</summary>
    public enum ChangeKind
    {
        /// <summary>Nothing the system declared as an input moved since the last call ⇒ the system may skip its pass.</summary>
        Unchanged,
        /// <summary>Some watched component chunk moved ⇒ do the pass (or, when EVERY watched type is a REQUIRED component of the system's query, visit just
        /// <see cref="ChangeResult.Entities"/>).</summary>
        Delta,
        /// <summary>Do the whole pass (also the answer whenever the gate is in doubt).</summary>
        Full,
    }

    /// <summary>Why the last answer was <see cref="ChangeKind.Full"/> — for diagnostics and rails.</summary>
    public enum FullReason
    {
        None,
        /// <summary>The gate has declared no input ⇒ it behaves exactly as no gate (today's behaviour).</summary>
        Undeclared,
        /// <summary><c>HROT_CHANGE_GATE=0</c> (or <see cref="ChangeGate.Enabled"/> false).</summary>
        Disabled,
        FirstRun,
        /// <summary>Another world, or a view that is not an <see cref="EntityRepository"/>.</summary>
        WorldReplaced,
        /// <summary><c>ClearGeneration</c> moved: the world was replaced in place (replay seek, checkpoint restore, dry-run exit).</summary>
        Cleared,
        /// <summary><c>GlobalVersion</c> went backwards (a seek / restore).</summary>
        ClockBackwards,
        /// <summary>A <see cref="ChangeGate.WatchExternal"/> version moved (viewport, settings…).</summary>
        External,
        /// <summary>An entity was created / destroyed or a component added / removed / the DIS type set somewhere in the world.</summary>
        Structural,
        /// <summary><c>GlobalVersion</c> did not advance since the last call (a world nobody ticks): a write made since is indistinguishable from the ones
        /// already seen ⇒ nothing can be skipped. The kernel ticks every frame (live, paused, replay), so this is a test / odd-host answer.</summary>
        StaticClock,
    }

    /// <summary>The answer of one <see cref="ChangeGate.Next"/>.</summary>
    public readonly struct ChangeResult
    {
        public ChangeKind Kind { get; }
        /// <summary>The version a <c>QueryDelta</c> must be asked with (<see cref="ChangeKind.Delta"/> only).</summary>
        public uint Since { get; }
        internal ChangeResult(ChangeKind kind, uint since) { Kind = kind; Since = since; }

        /// <summary>The entities of <paramref name="query"/> whose chunk of a component the QUERY requires moved (<see cref="ChangeKind.Delta"/>).
        /// ⚠ Exact only when every watched type is a required <c>With&lt;&gt;</c> of <paramref name="query"/> — <c>QueryDelta</c> looks at the query's
        /// own tables, so a change to an OPTIONAL input is not in it: such a system treats Delta as Full. ⚠ It yields whole hot chunks — the caller's
        /// own "did it differ?" test is the fine filter.</summary>
        public EntityRepository.DeltaQueryEnumerable Entities(EntityRepository world, EntityQuery query) => world.QueryDelta(query, Since);
    }

    /// <summary>
    /// ⭐ CE-1480 G1 (docs/DESIGN_Change_Gated_Systems.md) — ONE gate every per-frame system asks "did anything I read change?" with its OWN
    /// parameters, instead of each keeping its own last-version fields. Generalises <c>CycloneNetworkCleanupSystem</c>'s pattern
    /// (<c>QueryDelta</c> since the last run; a full pass on a replaced world / <c>ClearGeneration</c> / a clock that went back).
    /// <list type="bullet">
    ///   <item><see cref="Watch"/> — component types whose table chunk versions are the per-entity inputs.</item>
    ///   <item><see cref="WatchExternal"/> — a global input as a monotonic number (a viewport hash, a settings version); a change ⇒ <see cref="ChangeKind.Full"/>.</item>
    /// </list>
    /// <para><b>Non-breaking:</b> a gate that declares nothing answers Full every time ⇒ the owner runs exactly as before. <c>HROT_CHANGE_GATE=0</c> forces Full everywhere.</para>
    /// <para><b>In doubt ⇒ Full:</b> first run · another world · <c>ClearGeneration</c> moved · <c>GlobalVersion</c> went backwards · an external moved ·
    /// <c>StructuralChanges</c> moved (an entity created / destroyed, a component added or removed — invisible to a chunk version when it is a REMOVAL).</para>
    /// <para><b>Deterministic:</b> reads world versions only — never the wall clock (CE-4044).</para>
    /// <para><b>Limit:</b> a write stamped with the version a system last ran at, made AFTER that run, is re-seen once (the gate looks back one version).
    /// A world whose version does not advance between two calls is always Full (<see cref="FullReason.StaticClock"/>). A managed component is seen only through <c>SetManagedComponent</c> (an in-place mutation never moves a chunk version).</para>
    /// <para>Hot path: 0 B in steady state (<see cref="Next"/> allocates nothing).</para>
    /// </summary>
    public sealed class ChangeGate
    {
        /// <summary><c>HROT_CHANGE_GATE=0</c> ⇒ off (A/B, bisect); read once.</summary>
        public static bool Enabled { get; set; } = EnvVars.Read(EnvVars.HrotChangeGate) != "0";

        private Type[] _types = Array.Empty<Type>();
        private Func<long>[] _externals = Array.Empty<Func<long>>();
        private long[] _lastExternal = Array.Empty<long>();

        private EntityRepository? _world;
        private uint _clearGeneration;
        private uint _lastVersion;
        private long _lastStructural;
        private bool _ran;

        /// <summary>Totals since construction (rails / diagnostics).</summary>
        public int Fulls { get; private set; }
        public int Deltas { get; private set; }
        public int Unchangeds { get; private set; }
        public FullReason LastFullReason { get; private set; }

        /// <summary>True once <see cref="Watch"/> or <see cref="WatchExternal"/> declared something.</summary>
        public bool Declared => _types.Length > 0 || _externals.Length > 0;

        /// <summary>Declares component types whose chunk versions are inputs.</summary>
        public ChangeGate Watch(params Type[] componentTypes)
        {
            var merged = new Type[_types.Length + componentTypes.Length];
            _types.CopyTo(merged, 0);
            componentTypes.CopyTo(merged, _types.Length);
            _types = merged;
            return this;
        }

        /// <summary>Declares a global input as a number that changes whenever the input does (compared with the value seen at the last call).</summary>
        public ChangeGate WatchExternal(Func<long> version)
        {
            Array.Resize(ref _externals, _externals.Length + 1);
            Array.Resize(ref _lastExternal, _lastExternal.Length + 1);
            _externals[^1] = version;
            return this;
        }

        /// <summary>Forgets what was seen: the next <see cref="Next"/> is Full (a system that rebuilt its own state).</summary>
        public void Reset() { _ran = false; _world = null; }

        /// <summary>One answer per frame. Call it exactly once per system run.</summary>
        public ChangeResult Next(ISimulationView view)
        {
            if (!Declared) return Full(FullReason.Undeclared);
            if (!Enabled) return Full(FullReason.Disabled);
            if (view is not EntityRepository world) { _ran = false; return Full(FullReason.WorldReplaced); }

            uint version = world.GlobalVersion;
            long structural = world.StructuralChanges;
            bool externalMoved = false;
            for (int i = 0; i < _externals.Length; i++)
            {
                long v = _externals[i]();
                if (!_ran || v != _lastExternal[i]) externalMoved = true;
                _lastExternal[i] = v;
            }

            FullReason reason = FullReason.None;
            if (!_ran) reason = FullReason.FirstRun;
            else if (!ReferenceEquals(_world, world)) reason = FullReason.WorldReplaced;
            else if (world.ClearGeneration != _clearGeneration) reason = FullReason.Cleared;
            else if (version < _lastVersion) reason = FullReason.ClockBackwards;
            else if (externalMoved) reason = FullReason.External;
            else if (structural != _lastStructural) reason = FullReason.Structural;
            else if (version == _lastVersion) reason = FullReason.StaticClock;

            uint last = _lastVersion;

            _world = world;
            _clearGeneration = world.ClearGeneration;
            _lastVersion = version;
            _lastStructural = structural;
            _ran = true;

            if (reason != FullReason.None) return Full(reason);

            // ⚠ ">= last run": a change stamped later in the frame the last run saw carries that same version — look back one.
            uint since = last == 0 ? 0 : last - 1;

            bool any = false;
            for (int i = 0; i < _types.Length; i++)
                if (world.HasComponentChanged(_types[i], since)) { any = true; break; }

            if (!any) { Unchangeds++; return new ChangeResult(ChangeKind.Unchanged, since); }
            Deltas++;
            return new ChangeResult(ChangeKind.Delta, since);
        }

        private ChangeResult Full(FullReason reason)
        {
            Fulls++;
            LastFullReason = reason;
            return new ChangeResult(ChangeKind.Full, 0);
        }
    }
}
