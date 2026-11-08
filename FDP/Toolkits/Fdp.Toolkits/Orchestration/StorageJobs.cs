using System;
using System.Text.Json.Serialization;
using System.Threading;
using Fdp.Core;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>⭐ CE-4330 ② (📄 docs/DESIGN_Recording_Storage.md §7.2, R-4174) — what a storage job does with ONE exercise.</summary>
    public enum StorageJobKind : byte
    {
        /// <summary>The nodes' recordings → the NAS (<c>ExportArchive</c>).</summary>
        Archive = 0,
        /// <summary>The NAS → the nodes' staging (<c>ImportArchive</c>).</summary>
        Import = 1,
        /// <summary>An UNARCHIVED recording removed from the nodes (<c>DeleteExercise</c>).</summary>
        Discard = 2,
        /// <summary>An archived exercise removed from the NAS (<c>DeleteExercise</c> with <c>FromArchive</c>, §8 ⑦).</summary>
        Delete = 3,
    }

    /// <summary>⭐ CE-4330 ② — the state diagram of §7.2.</summary>
    public enum StorageJobState : byte
    {
        Queued = 0,
        Running = 1,
        WaitingForNode = 2,
        Done = 3,
        Failed = 4,
        Cancelled = 5,
    }

    /// <summary>
    /// ⭐⭐ CE-4330 ② (§7.2) — ONE storage request as the master's queue holds it and every station sees it (fact ①, <see cref="StorageJobsUpdateEvent"/>).
    /// <see cref="JobId"/> is the request id of the request that created it (a later request for the same exercise JOINS it).
    /// <see cref="GivingWay"/>: the transfer runs at a lower rate while a Live runs (Q5, §7.4). <see cref="Reason"/>: why it failed / waits, for the status bar.
    /// ⭐ Immutable — a change is a new instance.
    /// </summary>
    public sealed record StorageJob(Guid JobId, StorageJobKind Kind, ExerciseId ExerciseId, StorageJobState State,
                                    int FilesDone = 0, int FilesTotal = 0, long BytesDone = 0, long BytesTotal = 0,
                                    int WaitingFor = 0, string AskedBy = "", string? Reason = null, bool GivingWay = false)
    {
        /// <summary>Queued, running or waiting — it holds its exercise (a second request joins it; a discard is refused).</summary>
        [JsonIgnore]
        public bool IsActive => State is StorageJobState.Queued or StorageJobState.Running or StorageJobState.WaitingForNode;
    }

    /// <summary>⭐ CE-4330 ② — the master's job list for the DDS writer (one producer: the master).</summary>
    [EventId(9076)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct StorageJobsPublishedEvent
    {
        public StorageJob[] Jobs;
    }

    /// <summary>⭐ CE-4330 ③ fact ① — the cluster's storage jobs, folded by every host (<see cref="ClusterView.StorageJobs"/>).</summary>
    [EventId(9077)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct StorageJobsUpdateEvent
    {
        public StorageJob[] Jobs;
    }

    /// <summary>
    /// ⭐ CE-4330 ② — a running transfer's counters: written by the gateway's worker threads as files land, read by the master on its frame to put
    /// the numbers on the job (fact ①). Lock-free; a reader may see the files count one step ahead of the bytes, never torn values.
    /// <see cref="GiveWay"/> is the master's word to the transfer: a Live runs, go slower (Q5).
    /// </summary>
    public sealed class StorageTransferProgress
    {
        private int _filesDone, _filesTotal;
        private long _bytesDone, _bytesTotal;
        private int _giveWay;

        public int FilesDone => Volatile.Read(ref _filesDone);
        public int FilesTotal => Volatile.Read(ref _filesTotal);
        public long BytesDone => Interlocked.Read(ref _bytesDone);
        public long BytesTotal => Interlocked.Read(ref _bytesTotal);

        /// <summary>Set by the master while a Live runs; the transfer throttles while it is true.</summary>
        public bool GiveWay
        {
            get => Volatile.Read(ref _giveWay) != 0;
            set => Volatile.Write(ref _giveWay, value ? 1 : 0);
        }

        public void SetTotals(int files, long bytes)
        {
            Volatile.Write(ref _filesTotal, files);
            Interlocked.Exchange(ref _bytesTotal, bytes);
        }

        public void AddBytes(long bytes) => Interlocked.Add(ref _bytesDone, bytes);

        public void FileDone() => Interlocked.Increment(ref _filesDone);
    }
}
