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
        public static readonly bool Compiled = Environment.GetEnvironmentVariable("HROT_READ_CHECK") is "1" or "2";

        /// <summary>⭐ N-11 — <c>HROT_READ_CHECK=2</c>: the SURVEY — every check RECORDS its finding (kind, type, first call site) instead of throwing,
        /// and the access proceeds as with the checks off; <see cref="SurveyReport"/> lists them. One run names every undeclared read and every
        /// direct cross write of a content set, where the throwing mode stops at the first.</summary>
        public static readonly bool Survey = Environment.GetEnvironmentVariable("HROT_READ_CHECK") == "2";

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> s_survey = new();

        /// <summary>Records a finding (survey mode): its first call site, outside the engine core.</summary>
        public static void Note(string kind, Type t)
        {
            string key = kind + " " + t.FullName;
            if (s_survey.ContainsKey(key)) return;
            string site = new System.Diagnostics.StackTrace(2, false).ToString();
            if (!s_survey.TryAdd(key, site) || s_surveyOut == null) return;
            lock (s_survey) System.IO.File.AppendAllText(s_surveyOut, "SURVEY " + key + "\n" + site + "\n");   // a headless node: one line per new finding
        }

        private static readonly string? s_surveyOut = Environment.GetEnvironmentVariable("HROT_READ_CHECK_OUT");

        /// <summary>Every finding so far, "kind type" → first call site (survey mode).</summary>
        public static System.Collections.Generic.IReadOnlyDictionary<string, string> SurveyReport() => s_survey;

        /// <summary>Forget the findings (tests).</summary>
        public static void ClearSurvey() => s_survey.Clear();

        // ⭐⭐ N-10 (CE-4472; R-4212 / R-4214 ④⑤, docs/DESIGN_Parallel_Simulation.md §18–§18.2) — the per-entity rule IN THE REPOSITORY, always on
        //   (no longer a measuring prototype): while a per-entity parallel phase runs anywhere in the process (AnyPhase — one static load, so serial
        //   code stays near free: §18.3 measured), a read of ANOTHER entity's declared type on a batch thread is served from the repository's
        //   LAST-TICK view (EntityRepository.CaptureLastTick, taken serially at the Simulation phase start); a structural change or a bus publish
        //   made on a batch thread is deferred into that batch's command buffer (F-6, F-2), merged in batch order after the phase. Compiled
        //   (HROT_READ_CHECK=1, Debug / tests) adds the CHECKS: an undeclared cross read, a cross write and an access with no current entity throw.
        private static int s_phases;

        /// <summary>A per-entity parallel phase is running somewhere in the process (the cheap first test of every hook).</summary>
        public static bool AnyPhase
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => s_phases != 0;
        }

        /// <summary>A per-entity phase starts (the caller, before its batches are handed out — the pool's start orders it).</summary>
        public static void EnterPhase() { System.Threading.Interlocked.Increment(ref s_phaseSeq); System.Threading.Interlocked.Increment(ref s_phases); }

        /// <summary>The phase ended (after every batch is done).</summary>
        public static void ExitPhase() => System.Threading.Interlocked.Decrement(ref s_phases);

        [ThreadStatic] private static Jobs.BatchContext? t_batch;
        [ThreadStatic] private static EntityRepository? t_batchRepo;

        /// <summary>The batch this thread is running (its command buffer takes the deferred effects), or null.</summary>
        public static Jobs.BatchContext? CurrentBatch
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => t_batch;
        }

        /// <summary>The batch thread enters / leaves a batch of a phase over <paramref name="repo"/>.</summary>
        internal static void SetBatch(Jobs.BatchContext? batch, EntityRepository? repo, Jobs.PhaseReadSet? stable)
        { t_batch = batch; t_batchRepo = repo; t_stable = stable; }

        [ThreadStatic] private static Jobs.PhaseReadSet? t_stable;   // the running job's stable reads (N-11)

        /// <summary>Is a batch of a phase over <paramref name="repo"/> running on THIS thread (a structural change of THAT world is deferred)?</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Defers(EntityRepository repo) => s_phases != 0 && t_batch != null && ReferenceEquals(t_batchRepo, repo);

        /// <summary>Is a batch of a phase over the world whose bus is <paramref name="bus"/> running on THIS thread (a publish is deferred)?</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool DefersBus(FdpEventBus bus) => s_phases != 0 && t_batch != null && ReferenceEquals(t_batchRepo?.Bus, bus);

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

        // ⭐ F-11 (CE-4474; R-4212 ③) — the entities the running job owns BESIDES the one it processes: a squad-level job owns its squad's members
        //   (squad → members), exclusive within the phase (the caller guarantees no entity is in two jobs' sets). Cleared before every entity.
        [ThreadStatic] private static int[]? t_owned;
        [ThreadStatic] private static int t_ownedCount;

        /// <summary>⭐ F-11 — the job processing the current entity also OWNS <paramref name="e"/> until the next entity: read and written directly,
        /// like its own parts. ⛔ Only for a set no other job of the phase touches (the squad system checks its rosters are disjoint first).</summary>
        public static void OwnAlso(Entity e)
        {
            if (t_current == 0) throw new InvalidOperationException("ParallelReadCheck.OwnAlso: no entity is being processed on this thread (F-11)");
            var owned = t_owned ??= new int[16];
            if (t_ownedCount == owned.Length) { Array.Resize(ref owned, owned.Length * 2); t_owned = owned; }
            owned[t_ownedCount++] = e.Index;
        }

        /// <summary>Does the running job own <paramref name="index"/> besides its current entity (F-11)?</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool OwnsAlso(int index)
        {
            for (int i = 0; i < t_ownedCount; i++) if (t_owned![i] == index) return true;
            return false;
        }

        /// <summary>The job moves to its next entity (or leaves the batch): its extra ownership ends.</summary>
        internal static void ClearOwned() => t_ownedCount = 0;

        /// <summary>Is this access to entity <paramref name="index"/> a cross-entity one right now? (call only when <see cref="Active"/>)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOther(int index) => t_current != index + 1;

        // ── the declared mask: the types a cross-entity read may touch (one id space for unmanaged and managed — ComponentType.cs) ──
        private static ulong[] s_unmanaged = new ulong[4];
        private static ulong[] s_managed = new ulong[4];

        /// <summary>Declare that cross-entity reads of unmanaged <typeparamref name="T"/> are served from the last-tick copy.</summary>
        public static void DeclareUnmanaged<T>() where T : unmanaged => Set(ref s_unmanaged, ComponentType<T>.ID);

        /// <summary>Declare that cross-entity reads of managed <typeparamref name="T"/> are served from the last-tick copy.</summary>
        public static void DeclareManaged<T>() where T : class => Set(ref s_managed, ManagedComponentType<T>.ID);

        /// <summary>Forget every declaration (tests).</summary>
        public static void ClearDeclarations() { Array.Clear(s_unmanaged); Array.Clear(s_managed); s_declaredMask = default; HasDeclarations = false; }

        private static BitMask512 s_declaredMask;

        /// <summary>⭐ N-10 — every declared type (unmanaged and managed, by registry id): the last-tick view copies exactly these.</summary>
        public static BitMask512 DeclaredMask => s_declaredMask;

        /// <summary>Has anything been declared (no declaration ⇒ no last-tick view to take)?</summary>
        public static bool HasDeclarations { get; private set; }

        private static void Set(ref ulong[] bits, int id)
        {
            s_declaredMask.SetBit(id);
            HasDeclarations = true;
            if ((id >> 6) >= bits.Length) Array.Resize(ref bits, (id >> 6) + 1);
            bits[id >> 6] |= 1UL << (id & 63);
        }

        private static bool IsSet(ulong[] bits, int id) => (id >> 6) < bits.Length && (bits[id >> 6] & (1UL << (id & 63))) != 0;

        // ── the last-tick view: per repository (EntityRepository.CaptureLastTick) ──

        /// <summary>The prototype's / the bench's entry: takes <paramref name="repo"/>'s last-tick view of every declared type.</summary>
        public static void CopyUnmanaged<T>(EntityRepository repo) where T : unmanaged => repo.CaptureLastTick();

        /// <summary>The prototype's / the bench's entry: takes <paramref name="repo"/>'s last-tick view of every declared type.</summary>
        public static void CopyManaged<T>(EntityRepository repo) where T : class => repo.CaptureLastTick();

        // ── the slow paths (not inlined: the hot path keeps only the test) ──

        /// <summary>A cross-entity read of unmanaged <typeparamref name="T"/> during a phase: the LAST-TICK row when declared; else (checks off) the
        /// live row as before, (checks on) a throw.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref readonly T OtherUnmanagedRO<T>(EntityRepository repo, Entity e) where T : unmanaged
        {
            if (t_current == 0 && Compiled) { if (Survey) Note("no-current", typeof(T)); else throw NoCurrent(typeof(T), e); }
            if (repo.IsOwnPartOfCurrent(e.Index)) return ref repo.LiveUnmanagedRO<T>(e);   // N-11 — its own part: live
            if (t_stable != null && t_stable.Has(ComponentType<T>.ID)) return ref repo.LiveUnmanagedRO<T>(e);   // N-11 — stable in this phase: live
            if (IsSet(s_unmanaged, ComponentType<T>.ID) && repo.LastTickWorld is { } last && last.HasUnmanagedComponentUntraced<T>(e))
                return ref last.LiveUnmanagedRO<T>(e);
            if (Compiled) { if (Survey) Note("undeclared-read", typeof(T)); else throw Undeclared(typeof(T), e); }
            return ref repo.LiveUnmanagedRO<T>(e);
        }

        /// <summary>A cross-entity read of managed <typeparamref name="T"/> during a phase: the LAST-TICK reference when declared; else (checks off)
        /// the live one, (checks on) a throw.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ref readonly T OtherManagedRO<T>(EntityRepository repo, Entity e) where T : class
        {
            if (t_current == 0 && Compiled) { if (Survey) Note("no-current", typeof(T)); else throw NoCurrent(typeof(T), e); }
            if (repo.IsOwnPartOfCurrent(e.Index)) return ref repo.LiveManagedRefRO<T>(e);   // N-11 — its own part: live
            if (t_stable != null && t_stable.Has(ManagedComponentType<T>.ID)) return ref repo.LiveManagedRefRO<T>(e);   // N-11 — stable in this phase
            if (IsSet(s_managed, ManagedComponentType<T>.ID) && repo.LastTickWorld is { } last) return ref last.LiveManagedRefRO<T>(e);
            if (Compiled) { if (Survey) Note("undeclared-read", typeof(T)); else throw Undeclared(typeof(T), e); }
            return ref repo.LiveManagedRefRO<T>(e);
        }

        /// <summary>⭐ N-11 — a direct write to another entity during a phase (checks on): allowed on the processed entity's own part, else
        /// a defect (another entity's value changes only by a command).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void CheckOtherWrite(EntityRepository repo, Type t, Entity e)
        {
            if (t_current != 0 && repo.IsOwnPartOfCurrent(e.Index)) return;
            if (Survey) { Note("cross-write", t); return; }
            throw OtherWrite(t, e);
        }

        private static int s_phaseSeq;

        /// <summary>⭐ N-11 — changes at every phase start: "pending this phase" records (a deferred child creation) compare against it.</summary>
        public static int PhaseSequence => System.Threading.Volatile.Read(ref s_phaseSeq);

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
