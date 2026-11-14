using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// Thread-safe queue that lets background threads schedule work on the main/simulation thread.
    /// Enqueue from any thread via <see cref="RunOnMainThread{T}"/>; drain from the main thread via <see cref="DrainAll"/>.
    /// </summary>
    public sealed class MainThreadJobQueue
    {
        /// <summary>⭐ N-8 (CE-4470) — the threaded editor's SECOND queue, drained on the UI thread, for the routes that drive the UI (panels, the film
        /// director, the map layers, editor commands, perspectives …). This queue is then drained on the SIM thread between ticks (every other
        /// route sees all of S at a tick boundary). Null ⇒ one queue (every host that is not threaded — unchanged).</summary>
        public MainThreadJobQueue? UiQueue { get; set; }

        private static readonly System.Threading.AsyncLocal<bool> s_uiSide = new();

        /// <summary>⭐ N-8 — the request being served is a UI-side route (set by the host per request; it flows into the route's awaits).</summary>
        public static bool CurrentRequestIsUiSide { get => s_uiSide.Value; set => s_uiSide.Value = value; }

        private readonly ConcurrentQueue<(Func<object?> Job, TaskCompletionSource<object?> Tcs)> _queue
            = new ConcurrentQueue<(Func<object?>, TaskCompletionSource<object?>)>();

        /// <summary>
        /// Enqueues <paramref name="job"/> to run on the main thread and returns a Task that
        /// resolves (or faults) when <see cref="DrainAll"/> executes it.
        /// </summary>
        public Task<T> RunOnMainThread<T>(Func<T> job)
        {
            if (UiQueue != null && s_uiSide.Value) return UiQueue.RunOnMainThread(job);   // ⭐ N-8 — a UI route, in the threaded editor
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue((() => job(), tcs));
            return tcs.Task.ContinueWith(
                t => t.IsFaulted ? throw t.Exception!.InnerException! : (T)t.Result!,
                TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Runs every queued job on the calling (main) thread.
        /// Never throws — exceptions from jobs fault the corresponding Task instead.
        /// </summary>
        public void DrainAll()
        {
            while (_queue.TryDequeue(out var item))
            {
                try
                {
                    var result = item.Job();
                    item.Tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    item.Tcs.TrySetException(ex);
                }
            }
        }
    }
}
