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
        private readonly ConcurrentQueue<(Func<object?> Job, TaskCompletionSource<object?> Tcs)> _queue
            = new ConcurrentQueue<(Func<object?>, TaskCompletionSource<object?>)>();

        /// <summary>
        /// Enqueues <paramref name="job"/> to run on the main thread and returns a Task that
        /// resolves (or faults) when <see cref="DrainAll"/> executes it.
        /// </summary>
        public Task<T> RunOnMainThread<T>(Func<T> job)
        {
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
