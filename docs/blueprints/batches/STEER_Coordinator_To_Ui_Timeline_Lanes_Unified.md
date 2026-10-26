<!--STATUS
state: LIVE — coordinator → ui: a STEER for the running Timeline work (CE-1302), rule 1c
updated: 2026-10-26
current-answer: the whole file
stale-below: nothing
related-designs:
  - docs/DESIGN_Director_Timeline_Editor.md — §9c is the design this steers to (R-4078, R-4079); §9b the review it answers
-->

# STEER — ui: one lane seam moved to the shared toolkit; every lane registered, none hard-coded

**For:** the ui lane, Timeline work (`CE-1302`, [handoff](HANDOFF_Coordinator_To_Ui_Director_Timeline.md)). **From:** the coordinator lane.
⭐ The user relays this to the running session (rule 1c ③). ⭐ It is NOT a correction of your work: TL-3 and the lane enum were
reasonable readings of what was written; the user has now ruled the question the review ([§9b](../../DESIGN_Director_Timeline_Editor.md))
put to them.

🔒 **User, `2026-10-26`, verbatim:** *"we need to unify and share, not duplicate and fix with adapters. we can move the editor code to
some shared place. what are built-in lanes, timeline should be extensible, not hardcoded."*

## 1. What changes — R-4078, R-4079, design §9c

| | instead of | build |
|---|---|---|
| **① one lane seam** | TL-3: a new `ITimelineRowProvider` + an adapter for the trace providers *(planned, not built)* | ⭐ **MOVE** `ITraceLaneProvider`, `TraceLaneDescriptor`, `TraceLevel` from `Hrot.Editor.AiShared` to `Fdp.Toolkits` (`ReplayBrowser/Timeline`); generalise: `AssetKind Kind` → `Applies(context)`, descriptors gain group / priority / glyph, providers may draw marks for a range. Any rename by Roslyn. ⛔ No second interface, ⛔ no adapter |
| **② lanes as registered data** | the `TimelineLane` / `TimelineLaneGroup` enums (29 production references) and `LifeExtractor` / `CombatExtractor` / `BehaviourExtractor` *(built, R1-a)* | ⭐ an **indexed lane = descriptor + `SearchPredicateDto` + a projection** (what to keep); the builder compiles every registered indexed lane once and evaluates all in its one pass; the index and the sidecar key events by lane **id**; the shipped lanes are definitions registered by the module that owns their data (as §15.2 registers the trace lanes) |
| **③ extend the predicate model (R-4077), not the index** | per-lane code | a **projection** on scanner results (the counterpart, the point, the value — the scanner returns frame + entity + a message today, `IEventScannerCompiler.cs:14-19`) · a **`RingEntryPredicateDto`** (one result per NEW entry of a recorded ring, keyed by its own counter — TE-22 ②, R-4076) |
| **④ the cache** | one schema for a fixed lane set | per lane: id + definition hash ⇒ a new or changed lane is ONE incremental pass for that lane |
| **⑤ user lanes** | — | a predicate built in the existing editor (R-4072), saved like a search preset, indexed on the next open |

⭐ **Keep the extractors' knowledge:** each one says exactly what its lanes mean (a refused shot is not a shot; one event per new ring
entry; the first frame a unit is seen is its baseline). Each becomes its lane definition's TEST, red-proven against the definition.

## 2. Unchanged

R1-b's `TimelineLayout` (density / glyph / cluster / priority — it now reads priority and group from the descriptor), the panel, the
cursor (TL-2), the sidecar's header check (TL-6), the background builder (TL-7), the Replay Browser hosting, R0 ⑧'s numbers, and every
other item of the handoff and of the review note ([§9b](../../DESIGN_Director_Timeline_Editor.md) ①–④).

## 3. Order and report

Fold ①–⑤ into your plan before R1-d (the swimlane's lanes and R1-e's Trace Timeline hosting both sit on them). Report as the handoff's
§6 asks, naming this steer; fold the as-built into §9c (obligation ⑤).

---
_coordinator lane, 2026-10-26_
