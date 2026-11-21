using Fdp.Core;

namespace Fdp.ModuleHost.Providers
{
    /// <summary>
    /// ⭐ CE-3293 S4 (R-3036 rule 6) — a snapshot provider is a reader of the live world's event history: the history keeps every
    /// event until the provider has flushed it into a snapshot. A provider the topology replaces (a convoy promoted or widened)
    /// is not disposed — an in-flight view may still be leased from it — so it releases its reader instead; otherwise it would
    /// pin the history until the ring overflowed.
    /// </summary>
    internal interface IEventHistoryReader
    {
        /// <summary>Stops holding the history (re-registers if it flushes again).</summary>
        void ReleaseEventReader();
    }

    /// <summary>The lazily registered reader handle every provider keeps.</summary>
    internal struct EventReaderHandle
    {
        private int _id;   // 0 = none; else reader id + 1

        public void Flush(EventAccumulator history, FdpEventBus replica)
        {
            if (_id == 0) _id = history.RegisterReader() + 1;
            history.FlushUnreadTo(replica, _id - 1);
        }

        public void Release(EventAccumulator history)
        {
            if (_id == 0) return;
            history.UnregisterReader(_id - 1);
            _id = 0;
        }
    }
}
