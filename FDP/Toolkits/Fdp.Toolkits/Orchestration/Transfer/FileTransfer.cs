using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fdp.Toolkit.Orchestration.Transfer
{
    /// <summary>
    /// ⭐⭐ <c>P6</c> / <c>R-4002</c> — <b>how the bytes reach a node: one provider, the transport behind it.</b>
    /// The orchestrator's PUSH sites (NAS → node: scenario slices, the TKB, replay archives) hand a request to a provider and
    /// read its <see cref="IFileTransfer.Status"/>; which transport moves the bytes — a share the node advertises, or DDS —
    /// is the provider's business. ⛔ The PULL sites (node → NAS: save, archive, checkpoint) are not behind it.
    /// 📄 <c>docs/blueprints/Architect_Question_91_Player_Nodes.md</c> §4-E2.
    /// </summary>
    public interface IFileTransferProvider
    {
        /// <summary>The transport's name, for logs and status messages (<c>"share"</c>, <c>"dds"</c>).</summary>
        string Transport { get; }

        /// <summary>True when this provider can deliver to <paramref name="target"/>.</summary>
        bool CanReach(TransferTarget target);

        /// <summary>⭐ Starts the transfer and returns AT ONCE — never blocks the caller for the copy.</summary>
        IFileTransfer Start(TransferRequest request);
    }

    /// <summary>One transfer in flight. ⭐ <see cref="Status"/> is an immutable snapshot, safe to read from any thread.</summary>
    public interface IFileTransfer
    {
        Guid Id { get; }

        /// <summary>The latest progress snapshot — the caller polls it (no callbacks onto pool threads).</summary>
        TransferStatus Status { get; }

        /// <summary>Completes when every target is done, failed or cancelled. ⛔ Never faults — read <see cref="Status"/>.</summary>
        Task Completion { get; }

        void Cancel();
    }

    /// <summary>
    /// A node the transfer delivers to. <paramref name="Root"/> is that node's staging root AS THE SENDER REACHES IT — the
    /// share it advertised, or (co-located) the path the orchestrator already uses. ⚠ Empty means "no share": only a
    /// transport that delivers into the node's own root (DDS) can reach it.
    /// </summary>
    public sealed record TransferTarget(int NodeId, string Root);

    /// <summary>
    /// One file. <paramref name="RelativePath"/> is relative to the target's staging root, so the same item means the same
    /// file on every target of a bundled transfer.
    /// </summary>
    public sealed record TransferItem(string SourcePath, string RelativePath);

    /// <summary>
    /// ⭐ Several targets may share ONE transfer (user K: <i>"bundle nodes that might be waiting at the initialization time
    /// so single transfer serves them all at once"</i>) — every target receives every item.
    /// </summary>
    public sealed record TransferRequest(
        IReadOnlyList<TransferTarget> Targets,
        IReadOnlyList<TransferItem> Items,
        Guid CorrelationId = default);

    /// <summary>
    /// ⭐ An immutable progress snapshot. Counts are per (target, file); a file the target already holds — same length and
    /// last-write time, the ONE freshness rule (<c>DESIGN_Asset_Management.md</c> §1 ②) — is SKIPPED, not re-sent.
    /// <see cref="Code"/> is the existing <see cref="OrchestrationStatusCode"/>, so it feeds the operator's
    /// <c>ClusterOpStatus</c> unchanged.
    /// </summary>
    public sealed record TransferStatus(
        OrchestrationStatusCode Code,
        string Message,
        long BytesDone,
        long BytesTotal,
        int FilesDone,
        int FilesSkipped,
        int FilesFailed,
        int FilesTotal,
        IReadOnlyList<int> FailedNodeIds)
    {
        public bool IsFinished => Code != OrchestrationStatusCode.InProgress && Code != OrchestrationStatusCode.Pending;
    }
}

namespace Fdp.Toolkit.Orchestration.Transfer
{
    /// <summary>
    /// ⭐ <c>P6</c> <c>N5</c> — the share a node advertises in its greeting: <c>hrot.transfer.share=&lt;path&gt;</c>, a <c>key=value</c>
    /// token on the existing capability message (the form <c>hrot.asset.root.*</c> already uses). A node that advertises none is
    /// reached over DDS.
    /// </summary>
    public static class TransferTokens
    {
        public const string SharePrefix = "hrot.transfer.share=";

        /// <summary>
        /// ⭐ Advertised by a node that RUNS a DDS file receiver. ⛔ A node advertising neither this nor a share is never sent
        /// anything over DDS — it would wait forever for an answer it cannot give (measured live: a load parked indefinitely).
        /// </summary>
        public const string DdsReceiver = "hrot.transfer.dds";

        public static bool HasDdsReceiver(System.Collections.Generic.IEnumerable<string>? tokens)
            => tokens != null && System.Linq.Enumerable.Contains(tokens, DdsReceiver);

        /// <summary>The environment variable a deployment sets to <c>none</c> to make a node advertise NO share (a remote Player).</summary>
        public const string ShareEnvironmentVariable = "HROT_TRANSFER_SHARE";

        public static string Share(string path) => SharePrefix + path;

        /// <summary>The advertised share, or <see langword="null"/> when the node advertised none.</summary>
        public static string? ParseShare(System.Collections.Generic.IEnumerable<string>? tokens)
        {
            if (tokens == null) return null;
            foreach (var t in tokens)
                if (t != null && t.StartsWith(SharePrefix, System.StringComparison.Ordinal) && t.Length > SharePrefix.Length)
                    return t.Substring(SharePrefix.Length);
            return null;
        }

        /// <summary>
        /// The share token THIS process advertises: its own staging root — co-located nodes keep the copy they use today —
        /// unless <see cref="ShareEnvironmentVariable"/> names another path, or <c>none</c>.
        /// </summary>
        public static string? ForThisProcess(string nodeStagingRoot)
        {
            var configured = System.Environment.GetEnvironmentVariable(ShareEnvironmentVariable);
            if (string.Equals(configured, "none", System.StringComparison.OrdinalIgnoreCase)) return null;
            return Share(string.IsNullOrWhiteSpace(configured) ? nodeStagingRoot : configured!);
        }
    }
}
