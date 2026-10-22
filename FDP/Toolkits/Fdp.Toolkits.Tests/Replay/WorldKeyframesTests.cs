using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Replay;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Fdp.Toolkits.Tests.Replay
{
    [ComponentId(493)]
    internal struct KeyframeTestPos { public float X; }

    /// <summary>A content singleton: never world state (D7).</summary>
    [ComponentId(494)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    internal sealed class KeyframeTestContent { public string Name = ""; }

    /// <summary>A world-state singleton (unmanaged, so its table is versioned): recorded like a component.</summary>
    [ComponentId(495)]
    internal struct KeyframeTestWorldState { public int Value; }

    /// <summary>A managed world-state singleton — see <c>ManagedSingleton_SetBySetSingletonManaged_IsNotInAKeyframe</c>.</summary>
    [ComponentId(496)]
    internal sealed record KeyframeTestManagedState(int Value);

    /// <summary>
    /// ⭐ <c>B1</c> — <see cref="WorldKeyframes"/> (capture a world to RAM, apply it in place), <see cref="AfterRestore"/>,
    /// <see cref="WorldEpoch.Restored"/> and the singleton half of <c>D7</c>.
    /// 📄 docs/DESIGN_Checkpoint_And_Dry_Run_Restore.md §3, D4, D7, D8.
    /// </summary>
    public sealed class WorldKeyframesTests
    {
        private static EntityRepository NewWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<KeyframeTestPos>();
            return repo;
        }

        [Fact]
        public void Apply_PutsTheCapturedWorldBack_InPlace()
        {
            using var repo = NewWorld();
            var a = repo.CreateEntity(); repo.SetComponent(a, new KeyframeTestPos { X = 1f });
            var b = repo.CreateEntity(); repo.SetComponent(b, new KeyframeTestPos { X = 2f });
            repo.Tick();

            var kf = WorldKeyframes.Capture(repo);

            repo.Tick();
            repo.SetComponent(a, new KeyframeTestPos { X = 99f });
            repo.DestroyEntity(b);
            var c = repo.CreateEntity(); repo.SetComponent(c, new KeyframeTestPos { X = 3f });

            WorldKeyframes.Apply(repo, kf);

            Assert.Equal(2, repo.EntityCount);
            Assert.True(repo.IsAlive(a));
            Assert.True(repo.IsAlive(b));              // destroyed during the run, back with its handle
            Assert.False(repo.IsAlive(c));             // created during the run, gone
            Assert.Equal(1f, repo.GetComponent<KeyframeTestPos>(a).X);
            Assert.Equal(2f, repo.GetComponent<KeyframeTestPos>(b).X);
        }

        /// <summary>⭐ D8 — versions only grow: a version that went BACK would make every change-since check skip the restore.</summary>
        [Fact]
        public void Apply_NeverMovesTheVersionBack()
        {
            using var repo = NewWorld();
            repo.SetComponent(repo.CreateEntity(), new KeyframeTestPos { X = 1f });
            repo.Tick();
            var kf = WorldKeyframes.Capture(repo);
            for (int i = 0; i < 10; i++) repo.Tick();
            uint before = repo.GlobalVersion;

            WorldKeyframes.Apply(repo, kf);

            Assert.True(repo.GlobalVersion > before, $"version {repo.GlobalVersion} must exceed the live {before}");
        }

        /// <summary>⭐ R-3043 + D7 — an apply keeps the LOADED content: a NoReplay singleton is neither recorded nor replaced.</summary>
        [Fact]
        public void Apply_KeepsANoReplaySingleton_AndRestoresAWorldStateOne()
        {
            using var repo = NewWorld();
            var loaded = new KeyframeTestContent { Name = "loaded" };
            repo.SetSingletonManaged(loaded);
            repo.SetSingletonUnmanaged(new KeyframeTestWorldState { Value = 1 });
            repo.Tick();

            var kf = WorldKeyframes.Capture(repo);
            repo.Tick();
            var reloaded = new KeyframeTestContent { Name = "reloaded" };
            repo.SetSingletonManaged(reloaded);
            repo.SetSingletonUnmanaged(new KeyframeTestWorldState { Value = 7 });

            WorldKeyframes.Apply(repo, kf);

            Assert.Same(reloaded, repo.GetSingletonManaged<KeyframeTestContent>());   // not recorded ⇒ not touched
            Assert.Equal(1, repo.GetSingletonUnmanaged<KeyframeTestWorldState>().Value);          // world state ⇒ restored
        }

        /// <summary>
        /// 📐 MEASURED, pinned (restore design §1c): <c>SetSingletonManaged</c> stamps no table version and the recorder writes a
        /// singleton only when its version is past the keyframe's base (0) ⇒ <b>a managed singleton set that way is in NO
        /// keyframe</b>, recordable or not — it survives an apply untouched. ⚠ §6a M1's "recorded" list was the registry's flag,
        /// not keyframe bytes. If this reddens, managed singletons started being recorded: re-check every one of them (D7).
        /// </summary>
        [Fact]
        public void ManagedSingleton_SetBySetSingletonManaged_IsNotInAKeyframe()
        {
            using var repo = NewWorld();
            repo.SetSingletonManaged(new KeyframeTestManagedState(1));
            repo.Tick();
            var kf = WorldKeyframes.Capture(repo);
            repo.SetSingletonManaged(new KeyframeTestManagedState(7));

            WorldKeyframes.Apply(repo, kf);

            Assert.True(ComponentTypeRegistry.IsRecordable(ManagedComponentType<KeyframeTestManagedState>.ID));
            Assert.Equal(7, repo.GetSingletonManaged<KeyframeTestManagedState>()!.Value);
        }

        /// <summary>
        /// 🔴 D7's root cause (§6a M1): a singleton is registered only by its first use (SetSingleton*), and that path set every
        /// type recordable whatever its [DataPolicy] said — WorldEpoch ([Transient]) and the TKB went into every keyframe.
        /// </summary>
        [Fact]
        public void FirstRegistration_HonoursTheDataPolicyAttribute()
        {
            using var repo = NewWorld();
            repo.SetSingletonManaged(new KeyframeTestContent());
            repo.SetSingletonUnmanaged(new KeyframeTestWorldState());
            WorldEpoch.Advance(repo);

            Assert.False(ComponentTypeRegistry.IsRecordable(ManagedComponentType<KeyframeTestContent>.ID));
            Assert.False(ComponentTypeRegistry.IsRecordable(ManagedComponentType<WorldEpoch>.ID));
            Assert.False(ComponentTypeRegistry.IsRecordable(ManagedComponentType<NetworkEntityMap>.ID));
            Assert.False(ComponentTypeRegistry.IsRecordable(ManagedComponentType<Fdp.Interfaces.ITkbDatabase>.ID));
            Assert.True(ComponentTypeRegistry.IsRecordable(ComponentType<KeyframeTestWorldState>.ID));
        }

        /// <summary>⭐ D4 — a restore moves Moved (every per-id cache clears) but NOT Ended (nothing is deleted on the wire).</summary>
        [Fact]
        public void AfterRestore_MovesMoved_ButNotEnded()
        {
            using var repo = NewWorld();
            WorldEpoch.Advance(repo);
            int moved = 0, ended = 0;
            WorldEpoch.Moved(repo, ref moved);
            WorldEpoch.Ended(repo, ref ended);

            var kf = WorldKeyframes.Capture(repo);
            WorldKeyframes.Apply(repo, kf);
            AfterRestore.Run(repo);

            Assert.True(WorldEpoch.Moved(repo, ref moved));
            Assert.False(WorldEpoch.Ended(repo, ref ended));
            Assert.Equal(1, WorldEpoch.Of(repo));        // the same world

            WorldEpoch.Advance(repo);                     // a real boundary moves both
            Assert.True(WorldEpoch.Moved(repo, ref moved));
            Assert.True(WorldEpoch.Ended(repo, ref ended));
        }

        /// <summary>⭐ AfterRestore rebuilds the entity map from the restored world (it indexes entities the apply replaced).</summary>
        [Fact]
        public void AfterRestore_RebuildsTheEntityMap()
        {
            using var repo = NewWorld();
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var map = new NetworkEntityMap();
            repo.SetSingletonManaged(map);
            var e = repo.CreateEntity();
            repo.SetComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1001 });
            map.Register(1001, e);
            repo.Tick();
            var kf = WorldKeyframes.Capture(repo);

            repo.Tick();
            var extra = repo.CreateEntity();
            repo.SetComponent(extra, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1002 });
            map.Register(1002, extra);

            WorldKeyframes.Apply(repo, kf);
            AfterRestore.Run(repo);

            Assert.True(map.TryGetEntity(1001, out var back) && back == e);
            Assert.False(map.TryGetEntity(1002, out _));
        }
    }
}
