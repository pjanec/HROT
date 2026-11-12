using System;
using System.Runtime.CompilerServices;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <b>CE-4454 Part 2 — the per-read cross-entity check, PROTOTYPE</b> (📄 docs/DESIGN_Parallel_Simulation.md §18.2,
    /// docs/blueprints/batches/STATUS_Read_Check.md). During a parallel per-entity phase the repository's own read methods serve a read of
    /// ANOTHER entity from the last-tick copy, and a read of a type outside the declared mask throws. The open question it answers is its
    /// COST on the hot path (~10⁹ accesses per run).
    /// <list type="bullet">
    /// <item><see cref="Compiled"/> — <c>HROT_READ_CHECK=1</c>, read once. ⛔ Off, every hook is a <c>static readonly</c> test the JIT folds away:
    /// the read path is today's.</item>
    /// <item><see cref="Active"/> — the parallel phase is running (set before the phase's batches start, cleared after; the pool's own start
    /// and join order the write against the workers' reads).</item>
    /// <item><see cref="Current"/> — the entity this THREAD is processing (a per-entity job sets it per entity).</item>
    /// </list>
    /// ⚠ A prototype for measurement: a cross-entity WRITE is not redirected to a command buffer here (it throws while Active); the last-tick
    /// copy is one array per type, process-wide (one world).
    /// </summary>
    public static class ParallelReadCheck
    {
        /// <summary>The switch: <c>HROT_READ_CHECK=1</c>. Read once — off, the JIT removes every hook.</summary>
        public static readonly bool Compiled = Environment.GetEnvironmentVariable("HROT_READ_CHECK") == "1";

        /// <summary>A parallel per-entity phase is running ON THIS THREAD. ⭐ N-2 (CE-4464): per thread, not process-wide — the UI thread reading
        /// world A while the sim thread runs a parallel phase is not in that phase (a process-wide flag would make every A read a "cross-entity" one).
        /// <see cref="Jobs.ParallelEntities"/> sets it around each batch it runs.</summary>
        public static bool Active
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => t_active;
            set => t_active = value;
        }
        [ThreadStatic] private static bool t_active;

        [ThreadStatic] private static int t_current;   // entity index + 1 (0 = none: [ThreadStatic] starts at 0 on every thread)

        /// <summary>The entity this thread is processing (-1: none).</summary>
        public static int Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => t_current - 1;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => t_current = value + 1;
        }

        /// <summary>Is this access to entity <paramref name="index"/> a cross-entity one right now? (call only when <see cref="Active"/>)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOther(int index) => t_current != index + 1;

        // ── the declared mask: the types a cross-entity read may touch (unmanaged and managed ids are separate spaces) ──
        private static ulong[] s_unmanaged = new ulong[4];
        private static ulong[] s_managed = new ulong[4];

        /// <summary>Declare that cross-entity reads of unmanaged <typeparamref name="T"/> are served from the last-tick copy.</summary>
        public static void DeclareUnmanaged<T>() where T : unmanaged => Set(ref s_unmanaged, ComponentType<T>.ID);

        /// <summary>Declare that cross-entity reads of managed <typeparamref name="T"/> are served from the last-tick copy.</summary>
        public static void DeclareManaged<T>() where T : class => Set(ref s_managed, ManagedComponentType<T>.ID);

        /// <summary>Forget every declaration (tests).</summary>
        public static void ClearDeclarations() { Array.Clear(s_unmanaged); Array.Clear(s_managed); }

        private static void Set(ref ulong[] bits, int id)
        {
            if ((id >> 6) >= bits.Length) Array.Resize(ref bits, (id >> 6) + 1);
            bits[id >> 6] |= 1UL << (id & 63);
        }

        private static bool IsSet(ulong[] bits, int id) => (id >> 6) < bits.Length && (bits[id >> 6] & (1UL << (id & 63))) != 0;

        // ── the last-tick copy (one array per type) ──
        private static class LastTickUnmanaged<T> where T : unmanaged { public static T[] Rows = Array.Empty<T>(); }
        private static class LastTickManaged<T> where T : class { public static T?[] Rows = Array.Empty<T?>(); }

        /// <summary>Copy <typeparamref name="T"/>'s rows of <paramref name="repo"/> into the last-tick copy (before the phase).</summary>
        public static void CopyUnmanaged<T>(EntityRepository repo) where T : unmanaged
        {
            int n = repo.EntityIndexUntraced.MaxIssuedIndex + 1;
            ref var rows = ref LastTickUnmanaged<T>.Rows;
            if (rows.Length < n) rows = new T[Math.Max(n, rows.Length * 2)];
            var table = repo.GetComponentTable<T>();
            for (int i = 0; i < n; i++) rows[i] = table.GetRO(i);
        }

        /// <summary>Copy <typeparamref name="T"/>'s references of <paramref name="repo"/> into the last-tick copy (before the phase).</summary>
        public static void CopyManaged<T>(EntityRepository repo) where T : class
        {
            int n = repo.EntityIndexUntraced.MaxIssuedIndex + 1;
            ref var rows = ref LastTickManaged<T>.Rows;
            if (rows.Length < n) rows = new T?[Math.Max(n, rows.Length * 2)];
            for (int i = 0; i < n; i++) rows[i] = repo.LastTickManagedRow<T>(i);
        }

        // ── the slow paths (not inlined: the hot path keeps only the test) ──

        /// <summary>A cross-entity read of unmanaged <typeparamref name="T"/> during the phase: the last-tick row, or a throw when undeclared.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref readonly T OtherUnmanagedRO<T>(Entity e) where T : unmanaged
        {
            if (t_current == 0) throw NoCurrent(typeof(T), e);
            if (!IsSet(s_unmanaged, ComponentType<T>.ID)) throw Undeclared(typeof(T), e);
            return ref LastTickUnmanaged<T>.Rows[e.Index];
        }

        /// <summary>A cross-entity read of managed <typeparamref name="T"/> during the phase: the last-tick reference, or a throw when undeclared.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref readonly T OtherManagedRO<T>(Entity e) where T : class
        {
            if (t_current == 0) throw NoCurrent(typeof(T), e);
            if (!IsSet(s_managed, ManagedComponentType<T>.ID)) throw Undeclared(typeof(T), e);
            return ref Unsafe.As<T?, T>(ref LastTickManaged<T>.Rows[e.Index]);
        }

        /// <summary>A cross-entity write during the phase (the design: a command; this prototype: a throw).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Exception OtherWrite(Type t, Entity e) => t_current == 0 ? NoCurrent(t, e) :
            new InvalidOperationException($"ParallelReadCheck: a write of {t.Name} to entity {e.Index} while processing {Current} — a cross-entity write is a command (§18)");

        /// <summary>⭐ N-2 (CE-4464) — an access inside a parallel phase with NO current entity: the work is not attributed to an entity, so the
        /// per-entity rule cannot decide own vs other — always a defect of the job (F-1).</summary>
        private static Exception NoCurrent(Type t, Entity e) =>
            new InvalidOperationException($"ParallelReadCheck: an access to {t.Name} on entity {e.Index} inside a parallel phase with NO current entity — a pool batch must set the entity it processes (N-2, F-1)");

        private static Exception Undeclared(Type t, Entity e) =>
            new InvalidOperationException($"ParallelReadCheck: a read of {t.Name} on entity {e.Index} while processing {Current} — the type is not in the declared last-tick mask (§18.2)");
    }
}
