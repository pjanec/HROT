# Architect question #10 — robust pin↔link reconstruction for the editor round-trip (Blocker-1, part 2)

**Context.** Blueprints are stored **projection-only** (`"Pins": []`); the editor strips every node's pins
on save and the compiler (`Stage0_Rehydrate`) + the editor model (`BlueprintGraphModel.Rebuild`) rebuild
them on load. The user hit a corruption: opening a hill-attack blueprint in the editor and letting it
autosave produced a compile that fails (`BP1602 unknown pin id`, mis-typed links).

**Part 1 is fixed & shipped** (commit "Blocker-1 (part 1)"): the generator could not resolve *same-assembly*
curated-helper `FunctionCall` signatures (reflection can't see the assembly being compiled), so those pins
degraded to `System.Object` placeholders and every such blueprint had to persist explicit pins. A new
`IClrSignatureResolver` backed by the Roslyn semantic model now resolves them; a fully pin-less blueprint
(`SharedStateRallyDemo`) builds with real types, and 5 game-free unit tests + 58 hill-attack proofs guard it.

**Part 2 (this question) is architectural** and shared between the editor and the compiler, so we're putting
it to you before touching it.

---

## The defect — positional reconstruction is order-fragile

Both `Stage0_Rehydrate.AssignLinkGuids` and the editor's `BlueprintGraphModel.Rebuild` reconstruct pin GUIDs
**positionally**: the *i-th* pin (canonical order) is bound to the *i-th* distinct link GUID (first-occurrence
order in the links array). A `Link` carries only opaque GUIDs (`FromPinId`/`ToPinId`) — **no pin name, index,
or exec/data flag** — so nothing anchors a link to a specific pin except position.

This breaks whenever a node's **exec-In and data-In pins share the `In` bucket** and the saved links don't
happen to be ordered to match canonical pin order. Concrete failing case (integrated `IsWaveCompleted`,
`Branch` node):

```
canonical In-pins:   [ In(exec) , Condition(data:bool) ]
links (file order):    Compare ─▶ Condition          (first)
                       SetShared ─▶ In(exec)          (second)
positional bind:     In(exec)   ← Condition's GUID   ✗ swapped
                     Condition  ← exec's GUID         ✗ swapped
result:  Branch.Condition never receives the bool → emitted `var __t = default;` (CS8716);
         exec flow mis-wired.
```

The same algorithm runs in the editor, so an editor **save** of a non-canonically-ordered asset reproduces
it — this is (part of) the user's corruption. Part-1's fix makes `FunctionCall` *shapes* correct, but the
`Branch`/exec-vs-data ordering swap is independent and remains.

---

## Q-A — how do we make pin↔link reconstruction order-independent?

1. **Deterministic name-based pin GUIDs + one-time asset migration.** Assign *every* pin (connected or not)
   `Deterministic("pin:{nodeId}:{name}:{dir}")` in BOTH `AssignLinkGuids` and `Rebuild`, and match links to
   pins by that GUID (not by position). Requires a repo-wide migration rewriting every `.bp.json`'s
   `Link.FromPinId`/`ToPinId` to the name-based GUIDs (the unconnected-pin path already uses this exact
   formula, so the scheme is half-present). Fully robust and order-independent afterwards. *(our lean)*
2. **Enrich `Link` with pin identity.** Add `FromPinName`/`ToPinName` (or a stable pin index) to the `Link`
   record and bind by name. Also a format change + migration, but keeps opaque GUIDs as the wire id and is
   arguably more explicit than (1). Slightly larger asset/schema surface.
3. **Exec/data-aware bucketed positional assignment.** Split each direction into exec-pins and data-pins and
   assign each sub-bucket positionally. Problem: a `Link` has no exec/data flag, and classifying a link needs
   the *other* endpoint's pin (circular). Only works with a global 2-coloring of the link set (a
   matching/flow pass) — complex and can be ambiguous. No migration, but fragile.
4. **Keep positional; guarantee canonical link order.** Leave the algorithm; make the editor **reorder** each
   node's incident links into canonical pin order on save, and canonicalize the existing assets once.
   Smallest code change, but per-node ordering constraints on the *shared* global links array can conflict
   across a link's two endpoints (no guaranteed single ordering), so it is not provably safe.

- **Our lean:** **(1) deterministic name-based pin GUIDs + migration.** It removes the fragility class
  entirely (order-independent, no per-asset luck), reuses the formula the unconnected-pin path already uses,
  and keeps `Link` unchanged. The cost is a mechanical, scriptable one-time rewrite of every `.bp.json`'s
  link pin-GUIDs, applied identically in the editor `Rebuild` and compiler `Stage0` (they must stay in
  parity). We'd gate it behind the same "pins are unique within (node,direction)" invariant the deterministic
  fallback already assumes. Confirm (1), or point us at (2) if you'd rather links carry explicit pin identity.
- **Reuse vs build:** (1) = change 2 assign sites to name-based + a migration script over ~N assets, `Link`
  unchanged. (2) = `Link` schema + serializer change + migration + both assign sites. (3) = a new
  link-classification pass, no migration, but fragile. (4) = editor save-ordering + one-time canonicalize,
  smallest but not provably correct.

## Q-B — scope: make the hill-attack blueprints pin-less now, or after Q-A?

Today the hill-attack `.bp.json`s still carry explicit pins (so they compile green and the 58 proofs pass),
which means they are **not** yet editor-round-trip-safe. Options: (a) hold them as-is until Q-A's mechanism
lands, then strip pins repo-wide as part of the migration; or (b) strip them now via approach (4)'s per-asset
canonicalization as an interim, accepting it is not provably general.
- **Our lean:** **(a) hold until Q-A.** The part-1 fix already makes them *shape*-resolvable pin-less; the
  only thing standing between "explicit pins" and "safe pin-less" is the Q-A mechanism. Stripping pins before
  it lands just moves the corruption around. Keep explicit pins as the compiling state; land Q-A; strip
  repo-wide in one migration with a round-trip test (load → strip → rebuild → assert byte-identical pin/link
  binding).

---

## Recommendation summary
| | Lean | Why |
|---|---|---|
| Q-A robust reconstruction | **(1) deterministic name-based pin GUIDs + migration** | removes the fragility class; reuses existing formula; `Link` unchanged; parity across editor+compiler |
| Q-B scope | **(a) hold pins until Q-A lands** | part-1 already unblocks shapes; strip repo-wide once, guarded by a round-trip test |

*Status: Q-A/Q-B ANSWERED (option 1 + hold-then-strip). Implementation surfaced a scope blocker — see Q-C.*

---

## Q-C — the repo-wide strip is blocked by pin-less rehydration gaps in more node kinds (NEW)

**What we built & proved.** Implemented option 1: a canonical `DeterministicIds.PinId(nodeId,name,dir)` in
the compiler, byte-identical to the editor's `IdGenerator.Deterministic` (parity unit test passing);
`Stage0.AssignDeterministicPinIds` and the editor `Rebuild` both assign every pin its deterministic GUID.
Built the migration tooling (emit `oldGuid→newGuid` map via the old positional pass, apply losslessly).
The mechanism **works** — it fixes the Branch exec/data swap and the pin-less `SharedStateRallyDemo`.

**The blocker.** Making `Stage0` fully deterministic **breaks every existing pin-less asset** (~59 of 82):
their persisted links use the old positional/arbitrary pin GUIDs, which no longer match the deterministic
pins → `BP1602`. So the mechanism CANNOT land without migrating **all** assets in the same change. And the
migration requires every node in every asset to **rehydrate pin-less** — which several node kinds do NOT:
| Gap kind | Why it can't rehydrate pin-less today |
|---|---|
| `ChannelCommand` | data pins come from the catalog's `ParamsTypeFqn` struct fields; the editor's `NodePinSchema` projects them, but `Stage0` has NO enricher (only exec In/Out) |
| `PublishEvent` | catalog-driven event params; same gap |
| (others surfaced in demo/test fixtures) | additional dynamic-pin kinds with no `Stage0` enricher |

Each gap is the SAME class as the part-1 FunctionCall fix and needs its own generator-side enricher
(catalog/semantic-model driven, reflection-free). That is a sizeable multi-kind effort, well beyond the
Q-A framing. We reverted the (correct but non-landable) mechanism to keep the tree green on part-1.

**Q-C options:**
1. **Backward-compatible reconstruction (recommended).** Assign deterministic GUIDs first; per node, if no
   incident link references a deterministic pin GUID, fall back to the existing positional binding.
   Migrated/editor-saved assets get the robust deterministic path (fragility gone); legacy pin-less assets
   keep working unchanged. Lands the robustness for the assets that matter NOW (the integrated hill-attack
   blueprints, and anything re-saved through the editor) with zero repo-wide migration and no new enrichers.
   Trade-off: positional code stays as a legacy fallback (contra your "remove positional" ruling) until the
   enrichers land and every asset is migrated — i.e. it's the *transition*, deterministic is the end state.
2. **Full multi-kind enricher effort.** Build `Stage0` enrichers for ChannelCommand, PublishEvent, and every
   remaining dynamic-pin kind (each a part-1-style catalog/semantic-model resolver), then migrate all 82
   assets in one guarded pass. Achieves "all deterministic, no positional" but is a large, multi-slice track.
3. **Defer Q-A entirely.** Keep part-1 only; blueprints remain editor-fragile for the exec/data-swap case.

- **Our lean: (1)** — it delivers the anti-fragility you approved for the real target (the running behavior)
  immediately, is strictly safer than today for every asset, and lets (2)'s enrichers land incrementally
  (each migrated kind simply stops needing the fallback). If you hold firm on "no positional at all," we go
  to (2) as its own multi-slice track. Which do you want?

*Status: Q-C ANSWERED — option 1 (backward-compatible), then fix fully. IMPLEMENTED (general mechanism):*

- **Per-pin reconstruction** (a refinement of option 1) in both `Stage0_Rehydrate.AssignLinkGuids` and the
  editor `BlueprintGraphModel.Rebuild`, in strict parity via `DeterministicIds.PinId` (≡ the editor's
  `IdGenerator.Deterministic`). A link whose GUID is a pin's deterministic GUID binds that pin **by name**
  (order-independent — the exec/data swap is gone); remaining legacy links bind positionally to the still-
  unassigned pins. This handles migrated, legacy, AND **mixed** nodes — the naive "any incident link is
  deterministic ⇒ whole node deterministic" attempt broke assets where a link had been drawn to a pin that
  was saved unconnected (so it already carried its deterministic GUID) while sibling links stayed legacy.
- **PublishEvent enricher**: `Stage0` now rehydrates a pin-less PublishEvent node's data pins (`Target` +
  baked `EngineEventCatalogEntry.PayloadFields`) — a second catalog-driven gap (like ChannelCommand) that
  had forced explicit pins. Baked fields, reflection-free.
- Guards: 7 game-free reconstruction/parity/enricher tests; Blueprints.Tests green (mod. 2 pre-existing
  timing flakes); 60 hill-attack proofs green; behaviors build clean.

**Remaining tail (not yet landed):** migrating the 6 integrated `HillAssault2I_*` blueprints to pin-less
surfaced a *scheduler* codegen bug (`__t` producers for a Compare's operands dropped) that is independent of
the reconstruction fix — parked for a focused pass. Other node kinds (ChannelCommand, …) still need the same
PublishEvent-style enricher for a full repo-wide strip (mechanical, mirror the enricher per kind).
