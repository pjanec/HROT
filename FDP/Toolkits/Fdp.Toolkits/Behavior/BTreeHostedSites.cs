using System;
using System.Collections.Generic;
using Fbt;
using Fdp.Toolkit.Behavior.Shared;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>Which NODE of a BTree hosts a child, and under which tree-state slot.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §33. The twin of <see cref="HsmHostedSubtrees"/>,
/// keyed by the BLOB INSTANCE (S5b — see the table's remarks).
///
/// <para>⭐⭐⭐ <b>WHY IT WALKS THE BLOB, and this is the whole reason hand-written trees work.</b>
/// 🔒 User, <c>2026-09-27</c>: *"i need to support hand written c# btree subtrees as well."*
/// 📐 The blob is the ONE artefact both authoring routes produce: a <c>*.btree.json</c> asset goes
/// editor → generated C# → <c>Compile()</c>, and a hand-written tree goes <c>BTreeBuilder</c> →
/// <c>Compile()</c>. ⛔ Anything computed at EMIT time is invisible to the hand-written route **by
/// construction**, because there is no emitter — <c>BTreeDefinitionGenerator</c> produces only a
/// <c>Get&lt;Name&gt;()</c> catalog. ⇒ walking the blob is the only place that serves both.</para>
///
/// <para>⚠ <b>Startup-only</b>, like the registries beside it: <see cref="Plan"/> and
/// <see cref="Bind"/> run during the <c>[BlueprintRegistrar]</c> scan; reads are lock-free after.</para>
/// </summary>
public static class BTreeHostedSites
{
    /// <summary>One hosting node, resolved to the slot its child's cursor lives in.</summary>
    /// <param name="NodeIndex">The node's index in <c>blob.Nodes</c> — what the interpreter speaks in.</param>
    /// <param name="ChildName">The child behaviour's REGISTRY name.</param>
    /// <param name="TreeStateSlotKey">From <c>OccurrenceSlotKey.ComputeTreeStateKey(host, site, child)</c>.</param>
    /// <param name="Binding">⭐ <c>CE-431</c> — which bytes of the host's block seed the child; <c>default</c> = unbound.</param>
    public readonly record struct Entry(int NodeIndex, string ChildName, int TreeStateSlotKey,
                                        HostedSubtree.SiteBinding Binding = default);

    /// <summary>The result of walking one blob: what to declare, and what to bind.</summary>
    /// <param name="Slots">Stateful manifest entries — one per hosting node. ⛔ These MUST reach the
    /// <c>BehaviorDefinition</c>, or <see cref="HostedSubtree.Tick"/> throws on the undeclared slot.</param>
    public readonly record struct Plan(IReadOnlyList<Entry> Entries, IReadOnlyList<StatefulSlotInfo> Slots);

    // blob (BY REFERENCE) -> nodeIndex -> (slot key, site binding).
    // ⛔⛔ S5b / CE-2000 — it used to be keyed by blob.StructureHash, which hashes node TYPES and child COUNTS only
    //   (TreeCompiler.CalculateStructureHash). Two trees of the same shape — a host `Sequence(Subtree)` and its child
    //   `Sequence(Subtree)` — shared ONE map, and the last Bind won: the child's site then looked up the host's key.
    //   ⭐ The interpreter hands the host its own blob instance (Interpreter._blob) and every registrar binds that same
    //   instance, so the reference IS the identity. ⚠ A hot-reloaded tree is a new blob ⇒ a new entry; the old one is
    //   unreachable (a small, startup-scale leak, not a per-tick cost).
    private static readonly Dictionary<BehaviorTreeBlob, Dictionary<int, (int Key, HostedSubtree.SiteBinding Binding)>> _byBlob
        = new(ReferenceEqualityComparer.Instance);
    /// <summary>
    /// ⭐ <c>CE-442</c> — every WRITE takes this lock, so two registrars running at once (parallel test
    /// classes today) cannot corrupt the table. ⛔ Reads stay unlocked: registration finishes before the
    /// first tick, and the read side is on the per-tick path. Same rule as <c>HsmActionDispatcher</c>.
    /// </summary>
    private static readonly object WriteLock = new();


    /// <summary>
    /// ⭐⭐ <b>Derives a stable asset identity from a NAME.</b>
    /// ⚠ <c>BTreeDefinitionAttribute.AssetId</c> is documented *"null for hand-authored"*, so a
    /// hand-written tree has no Guid — and the EDITOR already falls back this way
    /// (<c>AssetIdHasher.FromName</c>, rail <c>LoadFrom_FallsBackToFromName_WhenAssetIdAbsent</c>).
    /// ⭐ Both are <b>FNV-1a-32 on offset basis 2166136261</b>, so the two sides agree by
    /// construction. ⛔ They cannot simply delegate — <c>Hrot.Editor.AiShared</c> does not reference
    /// this assembly — so <c>CE-368</c>'s cross-assembly rail is what keeps them honest.
    /// </summary>
    public static Guid AssetIdFromName(string name)
        => new Guid(BehaviorHash.FromName(name), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// ⭐⭐⭐ Walks <paramref name="blob"/> for <c>NodeType.Subtree</c> nodes and computes each one's
    /// tree-state slot key. ⭐ PURE — it registers nothing, because
    /// <c>BehaviorDefinition.StatefulWorkingSlots</c> is <c>init</c> and so must be known BEFORE the
    /// definition is constructed. <see cref="Bind"/> is the second phase.
    /// </summary>
    /// <param name="hostName">The host behaviour's registry name; the identity fallback.</param>
    /// <param name="hostAssetId">The host's editor asset id when it has one; <c>null</c> for a
    /// hand-written tree, which derives from <paramref name="hostName"/>.</param>
    /// <param name="bindings">⭐ <c>CE-431</c> — per-SITE seed bindings, keyed by the site's stable id (the
    /// node's visual id), baked by the host's registrar. A site absent from it is UNBOUND and its child
    /// starts from its own defaults.</param>
    public static Plan PlanFor(BehaviorTreeBlob blob, string hostName, Guid? hostAssetId = null,
                               IReadOnlyDictionary<Guid, HostedSubtree.SiteBinding>? bindings = null)
    {
        if (blob is null) throw new ArgumentNullException(nameof(blob));

        var entries = new List<Entry>();
        var slots   = new List<StatefulSlotInfo>();
        if (blob.Nodes is null) return new Plan(entries, slots);

        Guid hostId = hostAssetId ?? AssetIdFromName(hostName);
        var seen = new HashSet<int>();

        for (int i = 0; i < blob.Nodes.Length; i++)
        {
            ref readonly var node = ref blob.Nodes[i];
            if (node.Type != NodeType.Subtree) continue;

            int pi = node.PayloadIndex;
            if (blob.SubtreeAssetIds is null || pi < 0 || pi >= blob.SubtreeAssetIds.Length) continue;

            string childName = blob.SubtreeAssetIds[pi];
            if (string.IsNullOrWhiteSpace(childName)) continue;

            // ⭐ The SITE. `.Subtree(name, visualId: …)` already accepts one, and BTreeBuilder.Compile
            //   populates DebugMetadata unconditionally on both paths.
            // ⚠⚠ THE ORDINAL FALLBACK IS A STATED LIMITATION, not a detail: ComputeSiteId's own doc
            //   says "from the author's stable node id — NOT an ordinal". Inserting a Subtree node
            //   above another in hand-written source shifts it, so that child's cursor resets ONCE.
            //   Bounded and acceptable (a recompile, one lost cursor, never a wrong child) — and the
            //   fix is free and the author owns it: pass `visualId:`. CE-367 warns.
            Guid siteId = SiteIdOf(blob, i, hostName, childName);

            int key = OccurrenceSlotKey.ComputeTreeStateKey(hostId, siteId, AssetIdFromName(childName));

            // ⚠ Two nodes hosting the same child get DIFFERENT keys because the site differs, so a
            //   collision means a duplicated visual id — malformed input, not a co-scoped share.
            if (!seen.Add(key)) continue;

            var binding = bindings != null && bindings.TryGetValue(siteId, out var b) ? b : default;
            entries.Add(new Entry(i, childName.Trim(), key, binding));
            slots.Add(TreeStateSlot(key, childName.Trim()));
        }

        return new Plan(entries, slots);
    }

    /// <summary>
    /// ⭐ Binds each planned site's child interpreter and publishes the node→key map for the
    /// interpreter to read at tick time.
    /// ⭐⭐ <b>ORDER-INDEPENDENT since <c>CE-377</c>:</b> <see cref="HostedChildren.Register"/> records
    /// the registry and the child's NAME and resolves LAZILY, so it does not matter whether the
    /// child's registrar has run yet. ⚠ An earlier version of this remark told callers to invoke
    /// <c>Bind</c> AFTER the definition is registered because resolution was eager — 📐 that was not
    /// enough: it ordered the HOST's own two steps but said nothing about the CHILD's registrar,
    /// which is the one that actually races.
    /// ⛔ A child that never resolves still fails closed, at the hosting site, as
    /// <see cref="HostedChildren.Require"/>'s named exception — the same policy
    /// <see cref="HsmHostedSubtrees"/> follows.
    /// </summary>
    public static void Bind(BehaviorRegistry registry, BehaviorTreeBlob blob, in Plan plan)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        if (blob     is null) throw new ArgumentNullException(nameof(blob));
        if (plan.Entries is null || plan.Entries.Count == 0) return;

        var map = new Dictionary<int, (int, HostedSubtree.SiteBinding)>(plan.Entries.Count);
        foreach (var e in plan.Entries)
        {
            map[e.NodeIndex] = (e.TreeStateSlotKey, e.Binding);
            HostedChildren.Register(registry, e.TreeStateSlotKey, e.ChildName);
        }

        // ⭐ Last writer wins, deliberately: hot reload re-runs registrars and the NEW blob's map is
        //   the one that must be reachable. Same rule HostedChildren.Register follows.
        lock (WriteLock) _byBlob[blob] = map;
    }

    /// <summary>⭐ The slot key for a hosting node, or <c>false</c> when this node hosts nothing.</summary>
    public static bool TryGetKey(BehaviorTreeBlob blob, int nodeIndex, out int treeStateSlotKey)
        => TryGetSite(blob, nodeIndex, out treeStateSlotKey, out _);

    /// <summary>⭐ <c>CE-431</c> — the slot key AND the site's seed binding for a hosting node.</summary>
    public static bool TryGetSite(BehaviorTreeBlob blob, int nodeIndex, out int treeStateSlotKey,
                                  out HostedSubtree.SiteBinding binding)
    {
        treeStateSlotKey = 0; binding = default;
        if (blob is null
            || !_byBlob.TryGetValue(blob, out var map)
            || !map.TryGetValue(nodeIndex, out var site)) return false;
        (treeStateSlotKey, binding) = site;
        return true;
    }

    /// <summary>
    /// ⭐ Appends the hosted tree-state slots to an asset's AUTHORED slots.
    /// ⚠ Hosted slots go LAST so an existing asset's slot ORDER is byte-identical — the same rule
    /// the HSM emitter follows, and what keeps every golden still.
    /// </summary>
    public static StatefulSlotInfo[] Combine(
        IReadOnlyList<StatefulSlotInfo>? authored, IReadOnlyList<StatefulSlotInfo> hosted)
    {
        int a = authored?.Count ?? 0;
        var all = new StatefulSlotInfo[a + (hosted?.Count ?? 0)];
        for (int i = 0; i < a; i++) all[i] = authored![i];
        for (int i = 0; i < (hosted?.Count ?? 0); i++) all[a + i] = hosted![i];
        return all;
    }

    /// <summary>⚠ Test seam — drops every binding. ⛔ Production never calls this.</summary>
    public static void ClearForTests() { lock (WriteLock) _byBlob.Clear(); }

    // ---- internals ----------------------------------------------------------

    private static Guid SiteIdOf(BehaviorTreeBlob blob, int nodeIndex, string hostName, string childName)
    {
        var meta = blob.DebugMetadata;
        if (meta is not null && nodeIndex < meta.Length)
        {
            string raw = meta[nodeIndex]?.VisualId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
                return parsed;
        }
        // ⚠ The ordinal fallback — see PlanFor's remarks. Keyed on the host AND child names too, so
        //   two different hosts cannot collide on a bare index.
        return AssetIdFromName($"{hostName}#{childName}#{nodeIndex}");
    }

    /// <summary>
    /// ⭐⭐ One hosted child's <c>BehaviorTreeState</c> slot.
    /// ⛔ <b>Role=State / Scope=Behavior with <c>WorkingStateType == typeof(BehaviorTreeState)</c></b>
    /// is what makes <see cref="HostedSubtree.IsTreeStateSlot"/>'s manifest test work — an authored
    /// WorkingState struct can never be that type, so an external reset clears a hosted CURSOR and
    /// never author state. ⚠ Identical to the HSM emitter's emission; re-spelling it is how the two
    /// would drift.
    /// </summary>
    private static StatefulSlotInfo TreeStateSlot(int key, string childName)
    {
        int size = HostedSubtree.TreeStatePayloadSize;
        return new StatefulSlotInfo(
            key,
            size,
            unchecked(TypeNameHash("Fbt.BehaviorTreeState") ^ (uint)size),
            typeof(Fbt.BehaviorTreeState),
            childName + " (hosted)",
            (byte)Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotRole.State,
            (byte)Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.Behavior);
    }

    /// <summary>FNV-1a-32 over the type name — the same shape the emitters bake.</summary>
    private static uint TypeNameHash(string typeName)
    {
        uint hash = 2166136261u;
        foreach (char c in typeName)
        {
            hash ^= (byte)c;
            hash *= 16777619u;
        }
        return hash;
    }
}
