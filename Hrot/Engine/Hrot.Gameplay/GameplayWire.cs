using CycloneDDS.Runtime;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ DESIGN_Player_Join.md §9 (S1-a) — the gameplay wire of ONE node: a LAZY <see cref="DdsReader{T}"/> / <see cref="DdsWriter{T}"/> per topic over the
/// node's participant. ⛔ Nothing is created until first use, and the systems that use it are gated by the <c>gameplay.deploy-server</c> feature, so a node
/// with the pack composed and the feature OFF puts NO gameplay topic on the bus (the <c>TrafficMirrorSystem</c> precedent). A null participant (the
/// networkless editor) ⇒ every accessor returns null: a no-op. Main thread only.
/// </summary>
public sealed class GameplayWire : IDisposable
{
    private readonly DdsParticipant? _participant;
    private DdsReader<DeployRequestTopic>? _requests;
    private DdsWriter<DeployStatusTopic>? _status;
    private DdsReader<PlayerSlotTopic>? _slotReader;
    private DdsWriter<PlayerSlotTopic>? _slotWriter;
    private DdsWriter<DeployOfferTopic>? _offers;
    // ⭐ the PLAYER's ends of the same topics (S1 part B)
    private DdsWriter<DeployRequestTopic>? _requestWriter;
    private DdsReader<DeployStatusTopic>? _statusReader;
    private DdsReader<DeployOfferTopic>? _offerReader;

    public GameplayWire(DdsParticipant? participant) => _participant = participant;

    /// <summary>True when there is a participant to put topics on.</summary>
    public bool HasParticipant => _participant is not null;

    public DdsReader<DeployRequestTopic>? Requests => _participant is null ? null : _requests ??= new DdsReader<DeployRequestTopic>(_participant);
    public DdsWriter<DeployStatusTopic>? Status => _participant is null ? null : _status ??= new DdsWriter<DeployStatusTopic>(_participant);
    public DdsReader<PlayerSlotTopic>? SlotReader => _participant is null ? null : _slotReader ??= new DdsReader<PlayerSlotTopic>(_participant);
    public DdsWriter<PlayerSlotTopic>? SlotWriter => _participant is null ? null : _slotWriter ??= new DdsWriter<PlayerSlotTopic>(_participant);
    public DdsWriter<DeployOfferTopic>? Offers => _participant is null ? null : _offers ??= new DdsWriter<DeployOfferTopic>(_participant);

    /// <summary>The Player's request writer — created by the first request, so a Player that never deploys (no server) never has one.</summary>
    public DdsWriter<DeployRequestTopic>? RequestWriter => _participant is null ? null : _requestWriter ??= new DdsWriter<DeployRequestTopic>(_participant);
    public DdsReader<DeployStatusTopic>? StatusReader => _participant is null ? null : _statusReader ??= new DdsReader<DeployStatusTopic>(_participant);
    public DdsReader<DeployOfferTopic>? OfferReader => _participant is null ? null : _offerReader ??= new DdsReader<DeployOfferTopic>(_participant);

    // ⭐ The rail's evidence: what was actually created.
    public bool RequestWriterCreated => _requestWriter is not null;
    public bool StatusReaderCreated => _statusReader is not null;
    public bool OfferReaderCreated => _offerReader is not null;
    public bool RequestsCreated => _requests is not null;
    public bool StatusCreated => _status is not null;
    public bool SlotReaderCreated => _slotReader is not null;
    public bool SlotWriterCreated => _slotWriter is not null;
    public bool OffersCreated => _offers is not null;

    /// <summary>How many gameplay readers/writers exist (0 until first use).</summary>
    public int CreatedCount => (RequestsCreated ? 1 : 0) + (StatusCreated ? 1 : 0) + (SlotReaderCreated ? 1 : 0) + (SlotWriterCreated ? 1 : 0) + (OffersCreated ? 1 : 0)
        + (RequestWriterCreated ? 1 : 0) + (StatusReaderCreated ? 1 : 0) + (OfferReaderCreated ? 1 : 0);

    public void Dispose()
    {
        _requests?.Dispose(); _status?.Dispose(); _slotReader?.Dispose(); _slotWriter?.Dispose(); _offers?.Dispose();
        _requestWriter?.Dispose(); _statusReader?.Dispose(); _offerReader?.Dispose();
        _requestWriter = null; _statusReader = null; _offerReader = null;
        _requests = null; _status = null; _slotReader = null; _slotWriter = null; _offers = null;
    }
}
