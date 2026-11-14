<!--STATUS
state: LIVE — measured (CE-4456 Part 2)
updated: 2026-11-12
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — §7.4 the unmeasured assumptions these answer; F-9 / F-10 the cover recovery and callout de-duplication N-2 sizes
  - ../../DESIGN_Scale_Rig.md — the ≤ 4 GB per node budget
  - STATUS_Integration_Branch.md — the branch these ran on (exp/integration)
  - HANDOFF_Coordinator_To_Backend_Integration_Branch.md — CE-4456, this work
-->

# STATUS — memory per node, and same-tick cover / contact (CE-4456 Part 2)

**Branch:** `exp/integration`. The Release runner; the 4-core cloud VM; `HROT_QUERY_TIMINGS=0`.

## VERDICT

| question | answer |
|---|---|
| ⭐ a cluster NODE at 1 000 men | ✅ under 4 GB: cgf peak 1.77 GB plain / 2.10 GB with recording + replica + last-tick copies; simhost 1.83 / 1.96 |
| ⭐ 3 000 men | ✅ cgf peak **2.94 GB** (everything on), simhost 2.44 |
| ⭐ 5 000 men | ⚠ **at the budget**: cgf grows ~0.42 GB peak per 1 000 men with everything on ⇒ **~3.8 GB** at 5 000 — fits only with the two fixes below (or without recording) |
| ⛔ the EDITOR at 1 000 men | **4.24–4.29 GB — over**, threaded or not; the presentation world A adds only ~106 MB; ~1.2 GB is neither heap nor worlds (the software GL under Xvfb, terrain / navmesh / physics native — not attributed) |
| ⛔ `--mode all` at 1 000 men | **5.65 GB — over**: every node's heap in one process |
| what dominates | the GC heap, not the worlds (a world: ~97 KB per CGF man, ~25 KB per SimHost man; the replica and the last-tick copies each ≈ S) |

⭐ **Two fixes, both measured, neither touches the men:**
1. **Chunk-version arrays sized for `MAX_ENTITIES`** (`NativeChunkTable.cs:56,65`) — 84–217 MB per node, paid by every world including the EMPTY pooled
   snapshots ⇒ grow them with the committed chunks.
2. **The recording's ~0.5 GB of CGF heap** (three 128 MiB doubling buffers + the replay-browser timeline) ⇒ bound or spill them.
⚠ Also: the 15 pooled snapshot worlds of the editor keep 205 MB of native chunks committed while empty.

## The instruments (both opt-in, both on this branch)

| | what | output |
|---|---|---|
| `MemoryCensus` (`HROT_MEMORY_CENSUS=1`, `Fdp.Core`) | every `EntityRepository` tracked from its constructor with its CREATOR (two stack frames) ⇒ S, A, the replica R, the last-tick copies, the snapshot pools told apart; per world its entities, NATIVE committed bytes (component chunks + the entity index) and MANAGED slot bytes; the process (working set, GC heap, GC committed); owners that report a size (the undo `WorldSnapshotRing`) | `<out>.<pid>.json` every 5 s, the node's `--mode` in it |
| `ContactCoverProbe` (`HROT_CONTACT_PROBE=1`, `Fdp.Toolkits`, hooked at the kernel frame start) | per sim tick: cover commits (a `CoverClaim` turned live or its hide point moved > 0.5 m), same-point pairs among them (< 1.5 m, the claim radius), commits onto another unit's live claim, units whose `SensorContactList` gained an entity | `<out>.<pid>.tsv`, one line per tick |

⚠ The census does not attribute the GC heap's OBJECTS (a managed component's instances, caches, the recorder's buffers) — a `dotnet-gcdump` per node does (N-1c).

## N-1 — memory per node

### N-1a — the scale rig (orchestrator / cgf / simhost, one process each), 1 000 and 3 000 men

`scale-rig.py --terrain scale-1 --vehicles 50 --aircraft 10 --seconds 60`. **A** = every owner on (recording `--record`, the off-frame egress replica
`FDP_EGRESS_OFFFRAME=1`, the last-tick probe's two copies `HROT_LASTTICK_TYPES`=9 types); **B** = the plain node; **C** = A at 3 000 men.

| run | node | working set (peak) GB | GC heap GB | the worlds (native) |
|---|---|---|---|---|
| **B** 1 000, plain | cgf | 1.44 (1.77) | 0.66 | S 97 MB |
| | simhost | 1.81 (1.83) | 1.29 | S 27 MB · 10 snapshot-pool worlds 53 MB |
| **A** 1 000, all on | cgf | 2.08 (2.10) | 1.15 | S 97 MB · replica R 97 MB · last-tick masked + full 105 MB |
| | simhost | 1.96 (1.96) | 1.69 | S 25 MB · R 24 MB · last-tick 29 MB · 10 snapshot-pool worlds 46 MB |
| **C** 3 000, all on | cgf | 2.73 (**2.94**) | 2.68 | S 268 MB · R 268 MB · last-tick 288 MB |
| | simhost | 2.40 (2.44) | 1.82 | S 70 MB · R 69 MB · last-tick 82 MB · snapshot pool 134 MB |
| all | orchestrator | 0.67–0.69 | 0.03–0.13 | — |

⇒ a world costs **~97 KB native per CGF man** (S 97 MB / 1 000 men, 268 MB / 3 000) and ~25 KB per SimHost man; the replica R is the size of S;
the last-tick copies ≈ S (masked + full together). ⚠ The undo ring reported 0 bytes on the cluster nodes (no edits).

### N-1b — the editor (one process), 1 000 men, threaded vs not

`access-trace-session.py --stages load,play --play-seconds 90` (Xvfb, Release), `HROT_THREADED_KERNEL=3` vs `0`.

| editor | working set (peak) GB | GC heap GB | GC committed GB | the worlds (native) |
|---|---|---|---|---|
| threaded (S + A) | 4.19 (**4.29**) | 1.82 | 2.56 | S 106 MB · **A 106 MB** · 15 pooled snapshot worlds 205 MB |
| not threaded | 4.23 (**4.24**) | 1.72 | 2.60 | the world 105 MB · 15 pooled snapshot worlds 205 MB |

⇒ the presentation world A costs ~106 MB native (+ ~0.1 GB heap): **not** what puts the editor over 4 GB. ⛔ **The editor is over the
4 GB budget at 1 000 men either way**; ~1.2 GB of its working set is neither the GC heap nor the worlds (the software-GL drawing under Xvfb, the
terrain / navmesh / physics native buffers — not attributed here). ⚠ The 15 pooled snapshot worlds hold **205 MB of committed native chunks
while EMPTY** (0 entities): the pool keeps its chunks committed between uses.

### N-1c — what fills the GC heap (`dotnet-gcdump`, run A's set-up, 75 s into the live run)

The report lists each type by size bucket with the LARGEST object of the bucket ⇒ per type a range: count × the bucket floor … count × the largest.

| | cgf (heap 1.12 GB) | simhost (heap 1.33 GB) |
|---|---|---|
| `byte[]` | **311–449 MB** — three arrays of exactly 128 MiB (a buffer grown by doubling) | 75–182 MB |
| ⭐ `PaddedVersion[]` (a table's chunk versions) | **89–217 MB** | **84–167 MB** |
| `float[]` | 95–122 MB (one 127 MB array — the terrain grid) | 100–136 MB |
| `int[]` | 27–97 MB | 43–140 MB |
| replay-browser timeline (`PositionSample[]`, `TimelineEvent[]`, `SearchResultPayload[]`) | 11–55 MB | 9–47 MB |
| navmesh (`DtLink[]`) | — | 4–29 MB |

⇒ ⭐⭐ **two causes dominate, both fixable, neither is the men:**
1. **`PaddedVersion[]` is sized for `MAX_ENTITIES`, not for the world** (`NativeChunkTable.cs:56,65`: `_totalChunks = FdpConfig.GetRequiredChunks<T>()`,
   1 000 000 entities ⇒ 250 000 chunks × 64 B = 16 MB for a 16 KB component) — in EVERY world: S, the replica, both last-tick copies and each pooled
   snapshot, even an EMPTY one. ⇒ grow it with the committed chunks (the chunks themselves are already committed on demand).
2. **The recording**: run B (no `--record`) holds 0.66 GB of CGF heap, run A 1.15 GB — the ~0.5 GB difference is the three 128 MiB buffers + the
   replay-browser timeline types. ⚠ Their owner is not named by the report (no roots); the B/A difference is the attribution.


### N-1d — every node in ONE process (`--mode all`), 1 000 men

`cluster-node.sh start all`, the rig's scenario loaded live, 90 s: **working set 5.65 GB · GC heap 3.62 GB · GC committed 4.47 GB**; 4 worlds built
by the node builder (139 MB native together) + 15 pooled snapshot worlds (51 MB, empty). ⇒ ⛔ **over 4 GB** — the sum of the nodes' heaps in one
process (cgf 0.66 + simhost 1.29 + the rest, plain), no single owner.


## N-2 — same-tick cover and contact

`ContactCoverProbe` per sim tick (§ the instruments). The rig (run A, 1 000 men, the two sides closing — CGF holds the claims, SimHost the contact
lists) and the heaviest combat scenarios, each in a fresh headless editor running to its end (`--verify`; one world holds both).

| run | ticks (sim s) | cover commits | max per tick | same-point pairs (< 1.5 m, one tick) | onto a live claim | units with a new contact | max units per tick | a commit AND a new contact on one tick |
|---|---|---|---|---|---|---|---|---|
| rig, 1 000 men (80 s) | ~4 900 (80) | 4 | 1 | **0** | 0 | 970 over 166 ticks | **96** (0.6 s, the first sightings) · then ~25 every 10 s | — (two nodes) |
| `showcase-crossfire` | 55 424 (919) | 0 | 0 | 0 | 0 | 222 over 97 ticks | 16 | 0 |
| `uc-urban-battle` | 15 296 (200) | 1 785 — **1 742 from ONE unit** (below); the rest **43** | 3 | **0** | 1 | 132 over 54 ticks | 8 | 2 |
| `hill-attack` | 12 096 (200) | 2 | 2 | 0 | 0 | 0 (⚠ not explained — its units may hold no `SensorContactList`) | 0 | 0 |

⇒ ⭐ **sizing F-9 (cover recovery): two units committing to the same point on one tick never happened** (0 pairs in ~88 000 ticks); a commit onto
another unit's live claim happened ONCE. ⇒ the recovery is a rare-case fix-up (a tie-break when it happens), not a hot path.
⇒ ⭐ **sizing F-10 (callout de-duplication): new contacts arrive in BURSTS** — up to 96 units on one tick at the first sighting, 16 in a 900 s
fight, ~25 every 10 s in the rig (a sensor period) ⇒ the de-dup at command playback must expect tens of same-tick callouts, not one or two.
⛔ **Found on the way — CE-3444:** in `uc-urban-battle` entity 60 re-commits its cover claim on 1 742 ticks from 20 s to 181 s, cycling four hide
points 2–9 m apart — a unit thrashing between cover points (`HROT_CONTACT_PROBE_DETAIL=1` names it). Filed for the behaviors lane.

