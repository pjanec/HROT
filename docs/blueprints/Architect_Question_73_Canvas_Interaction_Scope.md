<!--STATUS
state: LIVE
updated: 2026-09-30
current-answer: ✅ RESOLVED 2026-09-30 — §8: option A, BUILT (CE-463). The user clarified that the external
  gizmo viewer is an external view on the node and selection spreads both ways "same like local gizmo
  renderer"; the "independent" remark was about a different case (ExCon remote-controlling the IG map),
  which must also sync — §8 measures it.
stale-below: nothing — this file is new.
known-rot: none.
known-conflict: it REVERSES PART of DESIGN_Gizmo_Anchor_Identity.md §6.3 ("0 is the honest value" for
  PickStreamId). §3 argues why that ruling's REASON does not cover this case; if the answer is A the
  amendment lands in §6.3 itself, not only here.
design-basis: DESIGN_Gizmo_Anchor_Identity.md §6.3 (the PickStreamId ruling) and §6.7 (the ingress
  filter's own argument) · UX_Feature_Selection.md §2.7.14 (the known limit) and §2.6 (remote map
  control is just another requester) · R-141 (an unused capability is the natural outcome of sharing) ·
  R-144 (the focus holder is served first, which is what the filter protects).
related-designs:
  - docs/DESIGN_Gizmo_Anchor_Identity.md — owns GizmoInteractionBatch's field meanings, the ingress
    resolve and the PickStreamId ruling this question reopens.
  - docs/UX/UX_Feature_Selection.md — owns UXI-11; §2.7.14 records the local-only empty-space clear
    that this question exists to close.
  - docs/blueprints/DESIGN_Editor_Entity_Selection_Source.md — owns the AI-editor entity cell; it does
    not own the map's wire protocol, but CE-306's facade unification is why the in-process half of this
    problem is already fixed.
-->

# Architect Question 73 — **what scopes a CANVAS (entity-less) map interaction to its target node?**

## 0. ⭐⭐ INVENTORY

```
grep -rn "PickStreamId"        --include=*.cs FDP/ Hrot/     → 11 sites, 0 readers  (§3 table)
grep -rn "GizmoInteractionBatch" (senders)                   → 2 production publishers + 1 adapter
search_graph name_pattern=".*GizmoInteraction.*"             → ingress 1 · egress 1 · batch 1 · bus events
```

| enumerated | |
|---|---|
| **publishers of `GizmoInteractionBatch`** | `GizmoInteractionEgressTranslator` *(Hrot nodes)* · `GizmoMap.Viewer/Program.cs:93` *(the standalone terminal)* · `DdsGizmoInteractionPublisher.cs:33` *(the reusable adapter)* |
| **consumers** | `GizmoInteractionIngressTranslator` — the only one |
| **readers of `PickStreamId`** | ⛔ **none**, measured *(and §6.3 measured the same in `2026-09-10`)* |
| **fields that could scope a canvas click** | `SourceNodeId` *(the SENDER)* · `PickStreamId` *(reserved, always 0)* · ⛔ no `MapId` |

⚠ `check_index_coverage` is unavailable through the CLI, so no coverage check backs these totals; they
rest on graph+grep agreement.

## 1. 🔴 THE DEFECT

📐 `GizmoInteractionIngressTranslator.cs:108-112`:

```csharp
if (!knownDead && NetworkIdResolver.ResolveNetworkId(repo, batch.PickAnchorId).IsNull)
    return;                                   // ← a CANVAS anchor (0) lands here and is dropped
```

⇒ **an external terminal's empty-space right-click never crosses the wire**, so its Clear is local-only
*(`UX_Feature_Selection.md` §2.7.14)*.

⭐⭐ **The drop is by OMISSION, not by design.** The gate's own comment states its purpose: *"DDS is
broadcast, so every node sees every interaction; and `DataDrivenGizmoSystem.Recipient` routes to the
FOCUS HOLDER FIRST (`R-144`) ⇒ forwarding a foreign drag would feed one operator's gesture into another
operator's active tool."* ⛔ That reasons about **entity** anchors. A canvas anchor is *no entity*, and
it falls through the same gate.

⛔⛔ **But the gate cannot simply be relaxed** — ⭐ that would be WORSE than today: the batch carries
nothing to scope a canvas click by, so **one operator's empty-space click would clear the selection on
every node.**

⭐ **Reachable, not theoretical** — 🔒 user, `2026-09-21`: *"remote terminal could be a map rendered
using gizmos running on external machine, taking data from the dds gizmo stream, or stride 3d windows
showing same scene just in 3d, clicking empty space."* 📐 `GizmoMap.Viewer` is exactly that and it does
publish the click.
✅ **The in-process Stride 3-D half is ALREADY FIXED** by `CE-306` — a 3-D empty-space clear goes through
`SetSelection2D(null)` → `ClearAll`, and `S-6` sends the empty set outward.

## 2. ⭐ WHAT THE TERMINAL ALREADY KNOWS

📐 `GizmoMap.Viewer/Program.cs:17,65,73` — the viewer takes **`targetNodeId`** *(a CLI arg, default 1)*
and already filters the primitive stream with `sample.Data.NodeId == targetNodeId`.
⇒ ⭐⭐ **the terminal knows exactly which node's map it is mirroring, by explicit configuration, at the
moment of the click.** ⛔ Nothing new has to be derived — which matters, because a canvas click hits no
primitive and so cannot be attributed from the hit.

## 3. ⚠ THE RULING THIS REOPENS — `DESIGN_Gizmo_Anchor_Identity.md` §6.3

> 🔒 §6.3, verbatim: *"Its **declared** meaning is a 'publisher stream discriminator for multi-SimHost
> clusters', which nothing sets ⇒ **`0` is the honest value**, not a placeholder to fill with whatever
> is to hand (`R-141`)."*

| what §6.3 was actually rejecting | measured |
|---|---|
| `PickStreamId = token.StreamId` — **a process-local ECS generation on the wire** | ✅ defect `D2` one publisher along; §6.3's whole subject |
| the field's **declared** purpose | ⭐ left intact and explicitly named |
| the guard rail `SC_GZ_WIRE_2` | ✅ asserts the record has **no ECS-handle-shaped FIELD** *(`AnchorIndex`, `PickGeneration`, …)* — ⛔ it does not constrain SETTING this one |

⇒ ⭐ **the ruling is against MISUSE, not against use.** ⚠ **It still has to be amended**, because it says
publishers should write `0`, and option A asks one to write something else.

## 4. ⭐⭐⭐ THE QUESTION

### A — **the terminal names the stream it acted on, in `PickStreamId`** ⭐ RECOMMENDED

| | |
|---|---|
| **terminal** | on a **canvas** pick (`AnchorId == 0`), set `PickStreamId = targetNodeId`. Entity picks keep `0` — they self-scope through the anchor. 2 sites |
| **ingress** | a canvas anchor stops falling through the entity gate; it requires `PickStreamId == this node's id`. 1 site |
| **design** | §6.3 gains: *0 stays honest for an entity pick; a canvas pick has a legitimate value, and this IS the declared purpose* |
| ⭐⭐ **backward-compatible BY CONSTRUCTION** | every un-migrated publisher sends `0`, and `0` keeps **today's** behaviour (dropped) ⇒ nothing changes until a terminal opts in. 📌 The same shape as `S-4b`'s `Left == 0` |
| ⭐ **no new wire field** | the vendored contract is untouched; `SC_GZ_WIRE_2` unaffected |
| ⛔ **the cost, stated** | it establishes a **cluster convention** — *a terminal must name the stream it is acting on* — and reverses half a sentence of §6.3 |

### B — add a `TargetNodeId` field to `GizmoInteractionBatch`

⭐ Unambiguous, and says what it means. ⛔ **A new field on a VENDORED contract** to carry what a
reserved field already declares — and `R-141`'s own logic says the unused capability is the thing to
use, not to duplicate. ⚠ Also a wire-format change for every publisher and subscriber.

### C — route the clear through `CMD_SET_SELECTION` instead

⭐ Uses `UXI-11`'s own scoped channel *(§2.6: "remote map control is just another requester")*, and
`S-6` already suppresses its echo. ⛔⛔ **Inverts a layering**: `GizmoMap.Viewer` is an FDP-level gizmo
client in `FDP/ExtDeps` and would gain a dependency on an **Hrot** command topic. ⚠ It also cannot
express a clear today — `ParseCommandAndSetSelection` requires `entityId` and returns without it.
*(I leant this before measuring the viewer; recorded because the reversal is the useful part.)*

### D — leave it. Document the limit and move on

⭐ Honest, and the in-process Stride case is already fixed. ⛔ Leaves a **silent divergence**: two
operators looking at one scene disagree about the selection after an empty-space click, with no
feedback that anything was dropped. ⚠ If this is chosen, the ingress should at least **log once** when
it drops a canvas anchor, so the next investigation starts at the filter instead of at the terminal.

## 5. ⭐ REUSE vs BUILD

| | |
|---|---|
| **reused** | `PickStreamId` *(declared, unused)* · `targetNodeId` *(already a viewer arg, already filtering)* · the ingress gate *(one added clause)* |
| **built** | ⛔ nothing new on the wire; ⭐ one convention, written down |

## 6. ⚠ WHAT I HAVE NOT MEASURED

| ⛔ | |
|---|---|
| whether any **other** terminal exists beyond `GizmoMap.Viewer` and the Stride window | the user named both; I enumerated publishers, not deployments |
| what a **multi-SimHost** cluster would want `PickStreamId` to mean | §6.3 says *"for multi-SimHost clusters"* and nothing sets it ⇒ ⭐ option A is the FIRST setter and therefore defines it. ⚠ That is the part most worth a second opinion |
| whether `DdsGizmoInteractionPublisher` has a target-node concept at all | it is the reusable adapter; the viewer has one, this may not |

## 7. ⭐⭐ `2026-09-30` — the user's position, and what the code does today

> 🔒 **User, `2026-09-30`:** *"I think external map is independent, selection changes there do not propagate
> to the other ui windows, nor the backwards."*

⚠ **The code does NOT do that today — in either direction, and both were built on purpose:**

| direction | measured | why it is that way |
|---|---|---|
| **external → node** | an external viewer's entity click reaches the node's selection. `GizmoInteractionIngressTranslator` publishes it onto the node's interaction bus (no source filter, `:58-62`), and the SAME bus feeds `SelectionInteractionSystem` on **CGF** (`CgfSubsystem.cs:1355,1369,1409`) and **SimHost** (`SimHostApp.cs:475`, `SimHostVisualization.cs:359`) | ⭐ `UXI-11` S-4 wired it deliberately: *"A click on a CGF-backed map selected nothing, silently"* (`CgfSubsystem.cs:1398-1403`); `UX_Feature_Selection.md` §2.6 *"remote map control is just another requester"* |
| **node → external** | the node's selection shows on the viewer: `SelectionHighlightGizmo` draws the rings **into the DDS primitive stream** the viewer renders | ⭐ its own header: *"flow through the DDS transport and are rendered by any connected consumer"* (`SelectionHighlightGizmo.cs`) |

⇒ ⭐ **the viewer is today a remote SCREEN of one node's map** (`targetNodeId`): it shows that node's selection
and its clicks edit it. Only the empty-space clear is missing (§1), which is this question.

| if the answer is… | what follows |
|---|---|
| ⭐ **shared (today's design)** — *lean* | §4 option A closes the one gap; nothing else changes |
| **independent** | a real reversal, not a fix: filter remote `Started` out of `SelectionInteractionSystem` on the ingress nodes, and either drop `SelectionHighlightGizmo` from the stream or scope it per viewer. ⚠ The viewer then has NO selection at all (it holds no state), so its right-click menu has no target set — the menu design would need its own answer |

⭐ **Lean: shared.** The viewer carries no state of its own, so "independent" means "has no selection", not
"has its own"; and S-4 made CGF's remote map usable precisely by sharing it.

## 8. ✅ RESOLVED `2026-09-30` — option A, built (`CE-463`)

> 🔒 **User:** *"The external gizmo viewer is just external view on the node, so in that case the selection
> should spread, it should work same like local gizmo renderer. I meant different situation, when the non gizmo
> independent map (simulated by the network map controller) is remote controlled from excon. But even there
> (the map running on the IG just remotely controlled) should sync the selection."*

⇒ §7's "independent" reading is **withdrawn**: it was about ExCon, not the viewer.

| | as built |
|---|---|
| **A — the viewer** | canvas picks carry `PickStreamId = targetNodeId`; the node accepts only canvas picks naming it ⇒ empty-space clear AND rubber band reach the node the viewer mirrors. 📄 `DESIGN_Gizmo_Anchor_Identity.md` §6.3a |
| **ExCon ↔ IG map** | ✅ already synced both ways: ExCon → IG by `CMD_SET_SELECTION` (routed to the one requester, `S-3`), IG → ExCon by the notification-driven `SelectionChangedEvent` (`S-6`), applied at `ExConLogic.cs:835`. 🔴 One leak closed: the shared-orbat seam `ExConOrbatAdapter.SelectEntity` set ExCon's selection LOCALLY only (`CE-259t`) — now `SendSetSelection`. ⚠ **Open:** `CMD_SET_SELECTION` cannot express a CLEAR (it requires `entityId`, `IgApplication.cs:3036`) ⇒ nothing on ExCon can clear the IG map's selection; recorded, not built |
