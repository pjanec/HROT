namespace Hrot.Gameplay;

/// <summary>
/// ⭐⭐ <c>CE-11027</c> S12-2 (<c>docs/DESIGN_Player_Join.md</c> §12.2, Q114 C / D) — the editor's IN-PROCESS gameplay transport: the deploy server and every deploy flow of one node meet here instead of on DDS, behind the SAME
/// <see cref="IGameplayWire"/>. The semantics are the ones the readers rely on (the DDS QoS the topics declare):
/// <list type="bullet">
///   <item><b>last value per key per topic</b> — offers (key <c>UnitNetId</c>), status and slots (key <c>StationNodeId</c>) are kept; a later write of a key replaces it, a dispose removes it;</item>
///   <item><b>offers, status and slots BROADCAST</b> to every client end (each end its own queue; the server end also reads the slots, as its slot reader does on DDS); <b>requests go to the server end</b> only;</item>
///   <item>a client end made <b>LATE</b> first receives the current last values (TransientLocal);</item>
///   <item><b>closing the server end</b> ends every slot: each receiver gets it not-alive ("the writer is gone", <c>PlayerSlotReaderSystem</c>);</item>
///   <item><see cref="IGameplayIn{T}.TakeInto"/> with nothing pending allocates nothing; the queues are reused.</item>
/// </list>
/// ⚠ Main thread only (the wire is). A sample is shared, not deep-copied, except an offer's kit list (the server may rebuild it).
/// </summary>
[Fdp.Core.NotState("the editor's in-process gameplay transport (queues and last values per topic); rebuilt with the node")]
public sealed class LocalGameplayHub
{
    private readonly Topic<DeployStatusTopic> _status = new(s => s.StationNodeId, null);
    private readonly Topic<DeployOfferTopic> _offers = new(o => o.UnitNetId, o => o.Kits is { Count: > 0 } k ? WithKits(o, new List<string>(k)) : o);
    private readonly Topic<PlayerSlotTopic> _slots = new(s => s.StationNodeId, null);
    private readonly Inbox<DeployRequestTopic> _requests = new();
    private LocalGameplayEnd? _server;
    private int _clients;

    [Fdp.Core.WorldDerived("static per-world registry of the node's ONE hub: filled on first use")]
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Fdp.Core.EntityRepository, LocalGameplayHub> PerWorld = new();

    /// <summary>
    /// ⭐ CE-11027 S12-4 (Q114 B) — THE hub of the node whose world is <paramref name="world"/>: the editor composes the deploy server over its server end, and a node extension (the gateway)
    /// asks for a client end per seat — both reach the same hub through here, never a second one. The assembly's own precedent (<see cref="DeployFlowSystem.Of(Fdp.Core.EntityRepository?)"/>):
    /// <c>HrotNodeContext.World</c> is init-only and a scenario load clears the world in place (<c>WorldBoundaryReset</c>), so the repository instance is the node's lifetime.
    /// </summary>
    public static LocalGameplayHub Of(Fdp.Core.EntityRepository world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return PerWorld.GetValue(world, static _ => new LocalGameplayHub());
    }

    private static DeployOfferTopic WithKits(DeployOfferTopic o, List<string> kits) { o.Kits = kits; return o; }

    /// <summary>How many client ends are open.</summary>
    public int ClientEndCount => _clients;

    /// <summary>The SERVER's end (requests in; status, offers, slots out; it also reads the slots). One at a time: asking again returns the open one; after it was closed a new one opens.</summary>
    /// <summary>⭐ CE-11027 — a server end is open (the node composes the deploy server): a node extension's seats then deploy through it. Reading it opens nothing (asked by the gateway's T-053, which read the private field by reflection).</summary>
    public bool HasServer => _server is { IsOpen: true };

    public LocalGameplayEnd ServerEnd()
    {
        if (_server is { IsOpen: true }) return _server;
        var slotInbox = _slots.AddReceiver();
        return _server = new LocalGameplayEnd(this, isServer: true, slotInbox);
    }

    /// <summary>A CLIENT end (one per flow: seat 0 of a node, a gateway seat …): requests out; status, offers and slots in. A late end first receives the current last values.</summary>
    public LocalGameplayEnd ClientEnd()
    {
        _clients++;
        var end = new LocalGameplayEnd(this, isServer: false, _slots.AddReceiver());
        end.StatusInbox = _status.AddReceiver();
        end.OfferInbox = _offers.AddReceiver();
        return end;
    }

    // ── what the ends call ──────────────────────────────────────────────────────────────────
    internal Inbox<DeployRequestTopic> Requests => _requests;
    internal Topic<DeployStatusTopic> Status => _status;
    internal Topic<DeployOfferTopic> Offers => _offers;
    internal Topic<PlayerSlotTopic> Slots => _slots;

    internal void CloseServer(LocalGameplayEnd end, Inbox<PlayerSlotTopic> slotInbox)
    {
        if (!ReferenceEquals(_server, end)) return;
        _slots.RemoveReceiver(slotInbox);
        _slots.EndAll();               // every slot not-alive to every receiver: the writer is gone
        _requests.Pending.Clear();
        _server = null;
    }

    internal void CloseClient(LocalGameplayEnd end)
    {
        _clients--;
        _slots.RemoveReceiver(end.SlotInbox);
        if (end.StatusInbox is { } s) _status.RemoveReceiver(s);
        if (end.OfferInbox is { } o) _offers.RemoveReceiver(o);
    }

    // ── the queue of one reader ─────────────────────────────────────────────────────────────
    internal sealed class Inbox<T> : IGameplayIn<T>
    {
        public readonly List<GameplaySample<T>> Pending = new();

        public int TakeInto(List<GameplaySample<T>> into)
        {
            int n = Pending.Count;
            if (n == 0) return 0;            // nothing pending: no work, no allocation
            for (int i = 0; i < n; i++) into.Add(Pending[i]);
            Pending.Clear();
            return n;
        }
    }

    // ── one topic: last value per key + every receiver's queue ──────────────────────────────
    internal sealed class Topic<T>
    {
        private readonly Func<T, int> _key;
        private readonly Func<T, T>? _clone;
        private readonly Dictionary<int, T> _last = new();
        private readonly List<Inbox<T>> _receivers = new();

        public Topic(Func<T, int> key, Func<T, T>? clone) { _key = key; _clone = clone; }

        public void Write(in T value)
        {
            var v = _clone is null ? value : _clone(value);
            _last[_key(v)] = v;
            for (int i = 0; i < _receivers.Count; i++) _receivers[i].Pending.Add(new GameplaySample<T>(v, true));
        }

        public void Dispose(in T key)
        {
            _last.Remove(_key(key));
            for (int i = 0; i < _receivers.Count; i++) _receivers[i].Pending.Add(new GameplaySample<T>(key, false));
        }

        /// <summary>A new receiver: it starts with the current last values (a late end sees the world as it is).</summary>
        public Inbox<T> AddReceiver()
        {
            var inbox = new Inbox<T>();
            foreach (var v in _last.Values) inbox.Pending.Add(new GameplaySample<T>(v, true));
            _receivers.Add(inbox);
            return inbox;
        }

        public void RemoveReceiver(Inbox<T> inbox) => _receivers.Remove(inbox);

        /// <summary>The writer is gone: every kept instance ends, each receiver told (the key rides in <c>Data</c>).</summary>
        public void EndAll()
        {
            foreach (var v in _last.Values)
                for (int i = 0; i < _receivers.Count; i++) _receivers[i].Pending.Add(new GameplaySample<T>(v, false));
            _last.Clear();
        }
    }
}

/// <summary>
/// ⭐ S12-2 — one end of a <see cref="LocalGameplayHub"/> as an <see cref="IGameplayWire"/>. A SERVER end has <c>Requests</c>, <c>Status</c>, <c>Offers</c>, <c>SlotWriter</c> and <c>SlotReader</c>; a CLIENT end has <c>RequestWriter</c>,
/// <c>StatusReader</c>, <c>OfferReader</c> and <c>SlotReader</c>; the rest are null. Disposing a client end stops its queues; disposing the SERVER end ends every slot (see the hub).
/// </summary>
public sealed class LocalGameplayEnd : IGameplayWire, IDisposable
{
    private readonly LocalGameplayHub _hub;
    private readonly bool _isServer;
    private bool _open = true;
    private readonly Out<DeployStatusTopic>? _status;
    private readonly Out<DeployOfferTopic>? _offers;
    private readonly Out<PlayerSlotTopic>? _slotOut;
    private readonly RequestOut? _requestOut;

    internal LocalGameplayHub.Inbox<PlayerSlotTopic> SlotInbox { get; }
    internal LocalGameplayHub.Inbox<DeployStatusTopic>? StatusInbox { get; set; }
    internal LocalGameplayHub.Inbox<DeployOfferTopic>? OfferInbox { get; set; }

    internal LocalGameplayEnd(LocalGameplayHub hub, bool isServer, LocalGameplayHub.Inbox<PlayerSlotTopic> slotInbox)
    {
        _hub = hub; _isServer = isServer; SlotInbox = slotInbox;
        if (isServer)
        {
            _status = new Out<DeployStatusTopic>(this, hub.Status);
            _offers = new Out<DeployOfferTopic>(this, hub.Offers);
            _slotOut = new Out<PlayerSlotTopic>(this, hub.Slots);
        }
        else _requestOut = new RequestOut(this, hub);
    }

    /// <summary>False once closed (a closed end ignores writes and yields nothing).</summary>
    public bool IsOpen => _open;

    /// <summary>The hub this end belongs to (⭐ CE-11027 S12-4: a rail asks it is the one <see cref="LocalGameplayHub.Of"/> returns).</summary>
    public LocalGameplayHub Hub => _hub;

    public IGameplayIn<DeployRequestTopic>? Requests => _isServer && _open ? _hub.Requests : null;
    public IGameplayOut<DeployStatusTopic>? Status => _status;
    public IGameplayOut<DeployOfferTopic>? Offers => _offers;
    public IGameplayOut<PlayerSlotTopic>? SlotWriter => _slotOut;
    public IGameplayOut<DeployRequestTopic>? RequestWriter => _requestOut;
    public IGameplayIn<DeployStatusTopic>? StatusReader => StatusInbox;
    public IGameplayIn<DeployOfferTopic>? OfferReader => OfferInbox;
    public IGameplayIn<PlayerSlotTopic>? SlotReader => SlotInbox;

    public void Dispose()
    {
        if (!_open) return;
        _open = false;
        if (_isServer) _hub.CloseServer(this, SlotInbox); else _hub.CloseClient(this);
    }

    private sealed class Out<T> : IGameplayOut<T>
    {
        private readonly LocalGameplayEnd _end;
        private readonly LocalGameplayHub.Topic<T> _topic;
        public Out(LocalGameplayEnd end, LocalGameplayHub.Topic<T> topic) { _end = end; _topic = topic; }
        public void Write(in T value) { if (_end._open) _topic.Write(value); }
        public void DisposeInstance(in T key) { if (_end._open) _topic.Dispose(key); }
    }

    private sealed class RequestOut : IGameplayOut<DeployRequestTopic>
    {
        private readonly LocalGameplayEnd _end;
        private readonly LocalGameplayHub _hub;
        public RequestOut(LocalGameplayEnd end, LocalGameplayHub hub) { _end = end; _hub = hub; }
        public void Write(in DeployRequestTopic value) { if (_end._open) _hub.Requests.Pending.Add(new GameplaySample<DeployRequestTopic>(value, true)); }
        public void DisposeInstance(in DeployRequestTopic key) { }   // a request is volatile: there is no instance to end
    }
}
