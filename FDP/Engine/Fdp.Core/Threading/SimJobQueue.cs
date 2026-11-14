using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Fdp.Core.Threading
{
    /// <summary>
    /// ⭐ <b>N-1 (CE-4463) — THE ONE SIM JOB QUEUE</b> (📄 docs/DESIGN_Threaded_Work_Inventory.md §8, D-5; §3 "SimJobs").
    /// Every UI → simulation effect in the threaded node (UI writes, run controls, HTTP work, the UI side of the orchestration bus) is POSTED here
    /// from any thread and RUN on the sim thread, between two ticks, in post order, exactly once. The existing write code runs unchanged — it is
    /// only retargeted from the presentation world A to the simulation world S (D-5). ⭐ One queue per kernel (N-13).
    /// <para>A job that must tell the UI something (an edit outcome, a refused drop) RETURNS a continuation: it reaches the presentation world
    /// through <see cref="IPresentationOutbox"/> — the publish queue that travels with the staging buffers (N-3), drained into A in order.</para>
    /// ⛔ Allocation: a post allocates its delegate (a user action, never per frame). <see cref="Drain"/> allocates nothing.
    /// </summary>
    public sealed class SimJobQueue
    {
        /// <summary>A job: runs on the sim thread with the simulation world. Returns a continuation for the presentation world, or null.</summary>
        public delegate Action<EntityRepository>? SimJob(EntityRepository sim);

        private readonly struct Item
        {
            public readonly SimJob Job; public readonly string Label; public readonly long Seq;
            public Item(SimJob job, string label, long seq) { Job = job; Label = label; Seq = seq; }
        }

        private readonly ConcurrentQueue<Item> _queue = new();
        private long _posted, _executed, _faults;
        private volatile int _simThread = -1;

        /// <summary>Where a job's continuation goes (the staging publisher's publish queue). Null ⇒ run inline on the world the job ran on.</summary>
        public IPresentationOutbox? Outbox { get; set; }

        /// <summary>Jobs posted / run / that threw since creation.</summary>
        public long Posted => Interlocked.Read(ref _posted);
        public long Executed => Interlocked.Read(ref _executed);
        public long Faults => Interlocked.Read(ref _faults);
        public int Pending => _queue.Count;

        /// <summary>The label of the last job that threw (diagnostics).</summary>
        public string? LastFault { get; private set; }

        /// <summary>The thread <see cref="Drain"/> runs on (the sim thread), once known.</summary>
        public int SimThreadId => _simThread;

        /// <summary>Is the caller the sim thread?</summary>
        public bool OnSimThread => _simThread == Environment.CurrentManagedThreadId;

        /// <summary>Posts <paramref name="job"/> (any thread). Returns its sequence number.</summary>
        public long Post(SimJob job, string label)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            long seq = Interlocked.Increment(ref _posted);
            _queue.Enqueue(new Item(job, label ?? "", seq));
            return seq;
        }

        /// <summary>Posts a job with no reply.</summary>
        public long Post(Action<EntityRepository> job, string label)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            return Post(s => { job(s); return null; }, label);
        }

        /// <summary>Posts a job whose RESULT a background caller awaits (an HTTP route): the task completes on the sim thread when it ran.</summary>
        public Task<T> PostAndWait<T>(Func<EntityRepository, T> job, string label)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(s =>
            {
                try { tcs.TrySetResult(job(s)); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                return null;
            }, label);
            return tcs.Task;
        }

        /// <summary>
        /// The sim thread, between two ticks: runs every job posted BEFORE this call, in post order (a job posted by a job runs at the next drain
        /// — never an endless drain). A job that throws is counted and logged; the drain goes on. Returns the number of jobs run.
        /// </summary>
        public int Drain(EntityRepository sim)
        {
            _simThread = Environment.CurrentManagedThreadId;
            int n = _queue.Count, ran = 0;
            for (int i = 0; i < n && _queue.TryDequeue(out var item); i++)
            {
                Action<EntityRepository>? reply = null;
                try { reply = item.Job(sim); }
                catch (Exception ex)
                {
                    long f = Interlocked.Increment(ref _faults);
                    LastFault = item.Label;
                    if (f <= 20) Console.WriteLine($"[SimJobs] job #{item.Seq} '{item.Label}' threw: {ex}");
                }
                Interlocked.Increment(ref _executed);
                ran++;
                if (reply != null)
                {
                    if (Outbox != null) Outbox.Post(reply);
                    else reply(sim);   // no presentation world of its own (single-world): the reply lands on the same world
                }
            }
            return ran;
        }
    }

    /// <summary>The publish queue's entry point for a continuation bound for the presentation world (N-3 implements it).</summary>
    public interface IPresentationOutbox
    {
        /// <summary>Queues <paramref name="onPresentation"/>; it runs on the UI thread against world A, in order, exactly once.</summary>
        void Post(Action<EntityRepository> onPresentation);
    }

    /// <summary>
    /// ⭐ The link a THREADED node installs on its presentation world A: the sim job queue and the simulation world. A call site holding A writes
    /// through <see cref="SimJobs.Run(EntityRepository, Action{EntityRepository}, string)"/>; with no link (the switch off, a headless node, a
    /// single-world host) the job runs at once on the world it was given — today's behaviour, unchanged.
    /// </summary>
    [ComponentId(GlobalComponentIds.SimLink)]
    [DataPolicy(DataPolicy.Transient | DataPolicy.NodeLocal)]
    public sealed class SimLink
    {
        public SimJobQueue Jobs { get; }
        /// <summary>The simulation world (⛔ never touched from the UI thread — post a job).</summary>
        public EntityRepository Sim { get; }
        public SimLink(SimJobQueue jobs, EntityRepository sim) { Jobs = jobs; Sim = sim; }
    }

    /// <summary>
    /// ⭐ N-1 — the one-line form every UI write site uses (📄 inventory §1): <c>SimJobs.Run(world, s =&gt; …existing code on s…, "label")</c>.
    /// Threaded (the presentation world carries a <see cref="SimLink"/>): posted, runs on the sim thread against S. Otherwise: runs now against
    /// <paramref name="world"/> (identical to today).
    /// </summary>
    public static class SimJobs
    {
        /// <summary>The link on <paramref name="world"/>, or null (not threaded / not a presentation world).</summary>
        public static SimLink? LinkOf(EntityRepository? world)
            => world != null && world.HasSingletonManaged<SimLink>() ? world.GetSingletonManaged<SimLink>() : null;

        /// <summary>Is <paramref name="world"/> a threaded node's presentation world (writes must be posted)?</summary>
        public static bool IsThreaded(EntityRepository? world) => LinkOf(world) != null;

        /// <summary>Runs <paramref name="job"/> against the simulation world: posted when threaded, now otherwise.</summary>
        public static void Run(EntityRepository world, Action<EntityRepository> job, string label)
        {
            var link = LinkOf(world);
            if (link == null) { job(world); return; }
            link.Jobs.Post(job, label);
        }

        /// <summary>Runs <paramref name="job"/> against the simulation world; its continuation runs against the presentation world — later (threaded,
        /// from the publish queue) or at once (single-world, against the same world).</summary>
        public static void Run(EntityRepository world, SimJobQueue.SimJob job, string label)
        {
            var link = LinkOf(world);
            if (link == null) { job(world)?.Invoke(world); return; }
            link.Jobs.Post(job, label);
        }
    }
}
