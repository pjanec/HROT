<!--STATUS
state: LIVE
build-state: BUILT (7 of 8 items complete; item ④'s cluster half blocked CROSS-LANE and reported)
updated: 2026-08-24
current-answer: §1 what shipped per item · §2 obligation ③ (the five deviations, folded into Q54) ·
  §3 the §Gates table · §4 the mutation table · §5 the ids · §6 the two measured gaps.
design-basis: 📄 docs/blueprints/Architect_Question_54_Cluster_Mcp_Contract.md (RESOLVED, and its new
  § AS-BUILT) · docs/DESIGN_Headless_Testability.md §6/§6e/§Conformance · DESIGN_Perspective_Unification.md
  §1b/§1d · docs/blueprints/batches/HANDOFF_Conformance_Harness.md (dispatched 045773154).
known-rot: none. ⚠ EPHEMERAL — the durable record is Q54 § AS-BUILT and §6e.
-->
# REPORT — **the cross-host conformance harness** *(steps 6+7, on the Q54 contract)*

> 🔒 **User, `2026-08-24`:** *"(a) `--mode all` answers MCP and its panels/gizmos match the editor's; (b) the
> part-C editor goldens still pass; (c) BOTH driven by the SAME deterministic cluster-wide step."*

⭐⭐ **(a) yes, with one measured gap** *(gizmos are not published cluster-side — `MX-014`)*; ⭐ **(b) yes —
80/80 including `PanelGoldenRails`**; ⚠ **(c) the same seam, and it is ack-gated — but the cluster half of
the gate is blocked by a lane boundary** *(`HN-028`)*.

⛔⛔ **This report is EPHEMERAL.** ⭐⭐⭐ The durable record is
**[`Architect_Question_54`](../Architect_Question_54_Cluster_Mcp_Contract.md) § AS-BUILT** and
**[`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md) §6e + the conformance AS-BUILT** —
both written before this batch closed *(obligation ⑤)*.

## 1. ⭐⭐ PER ITEM

| # | item | state |
|---|---|---|
| ✅ **①** | `ISubsystemDebugProvider` + `PerspectiveScopedDispatcher` | **BUILT.** Providers from **CGF · SimHost · IG · ExCon**; the dispatcher resolves the LIVE perspective and exposes `Resolve(name)` as `Q54-2`'s override |
| ✅ **②** | lift the API to the ClusterRunner host | **BUILT.** The four §6a wiring points in `Program.cs`, gated on `HROT_DEBUG_API_PORT` **and** on the editor subsystem being absent. 📐 `--mode all` answers `/status`, `/capabilities`, `/perspectives`, `/perspective`, `/panels`, `/panels/{id}`, `/sim/*` |
| ✅ **③** | `GET /capabilities` — the FULL manifest | **BUILT.** **64 endpoints enumerated from the live route table**, `unclassifiedRoutes: []`, matrix MEASURED from wired deps. ⭐ The known-absent baseline lives in the HARNESS *(live dump here, reviewed golden there)* |
| ⚠ **④** | ack-gated cluster-wide `Step()` | **BUILT for the editor; the CLUSTER half is BLOCKED CROSS-LANE** — `HN-028`. ⭐ Reported with `hasMaster:false` in the manifest **and a rail that asserts it** |
| ✅ **⑤** | the three-way conformance suite | **BUILT** — and it needed a **FOURTH** verdict *(§2 ④)* |
| ✅ **⑥** | prove conformance + the manifest FAIL on demand | **BOTH PROVEN** — §4 |
| ✅ **⑦** | re-prove the part-C editor goldens | **80 / 80**, `PanelGoldenRails` included, under the ack-gated step |
| ✅ **⑧** | a lockstep rail | **BUILT.** After a **paused** cluster step, CGF and SimHost report *identical* sim time |

## 2. ⭐⭐⭐ OBLIGATION ③ — **the design's UML was checked; FIVE deviations, all folded into Q54**

| # | short form |
|---|---|
| **①** | ⛔⛔ §6c's *"gate INSIDE `Step()`"* is **impossible** — the ACK drain and `Step()` are both main-thread ⇒ deadlock. The gate moved to the HTTP handler; the return contract is unchanged |
| **②** | 🔴🔴 the gate's cluster half is **cross-lane blocked** *(`MasterSyncController` is private in `OrchestratorSubsystem`)* ⇒ `hasMaster:false`, asserted |
| **③** | ⛔ a **value-captured provider LIES** — `time.drive:false` for SimHost/CGF, because their adapter is built in `RegisterWindows`, after the composition root ⇒ lazy accessors, matrix measured at read time |
| **④** | ⭐⭐⭐ the three-way verdict needed a **FOURTH: *DIFFERENT BY DESIGN*** — declared, with a reason per entry, plus a control that reddens if one starts agreeing |
| **⑤** | ⛔ `/status` and `/sim/state` had to **degrade** — a supported step answered `NOT_SUPPORTED_HERE(preview.control)` because the *response* read `_preview` |

⭐ **Also corrected in the design:** §6a's class diagram shows the superseded single-`ClusterReadDriveService`
shape; §6e now says so and points at `Q54`'s UML.

## 3. ⭐⭐⭐ §GATES

| # | gate | verbatim command | `--no-build`? | result · delta vs `045773154` |
|---|---|---|---|---|
| 1 · 8 | ⭐⭐⭐ **the integration gate** | `bash scripts/run-system-tests.sh` | builds | ⭐ **80 / 80 pass, 0 fail, 0 skip** *(baseline `76/76` ⇒ **+4**, all new conformance/lockstep/manifest cases)* |
| 1 | build | `dotnet build IOS-IG-SimHost.sln --no-restore` | must build | ⭐ **succeeded, 0 errors** *(rebuilt for every mutation and every restore)* |
| 1 | ⭐⭐ **`--mode all` boots and answers** | `HROT_DEBUG_API_PORT=… xvfb-run dotnet Hrot.ClusterRunner.dll --mode all` + `curl` | n/a | ⭐ five subsystems over DDS; slaves `#1/#100/#400` enrol; the API answers. ⛔ **No DDS-allocator crash on this machine** — the handoff's worry did not materialise |
| 2 | out-of-solution / stale bin | — | — | ⭐ all gated projects are in the solution; every `--no-build` run followed a full build of the same tree |
| 3 | golden movement | `git status` | — | ⭐ **ZERO goldens moved** *(0 created, 0 modified, 0 deleted)* — this batch adds rails, not baselines. ⭐ The one committed "baseline" is the conformance **known-absent set**, in code, with a reason per entry |
| 4 | every RED pre-existing, by name | — | — | ⭐ **no reds on the clean tree.** ⚠ `HN-023`'s determinism flake did not recur in 3 full runs *(1-in-4 previously)* — ⛔ still open, not evidence it is gone |
| 5 | working tree clean after every suite | `git status --short` | — | ⭐ clean; both mutation probes reverted and verified *(`grep -c "MUTATION PROBE"` ⇒ 0)* |
| 6 | quarantine counts | — | — | ⭐ **0 skips before, 0 after.** ⛔ No new filter |
| 7 | doc gates + ids | `tracker-counts.py --check` · `rulings-check.py` · `design-digest.py --check` · `mermaid-check.mjs` | — | ⭐ **OK (open 99 / done 333)** · **24/24 verified** *(2 known WARNs)* · **85 designs OK** · **2/2 mermaid blocks parse** in `Q54` |

## 4. ⭐⭐⭐ THE MUTATION TABLE — **item ⑥**

| # | mutation *(reverted)* | what reddened | expected? |
|---|---|---|---|
| **①** | ⭐⭐ **a matrix cell made HAND-AUTHORED** — `[TimeDrive] = true` instead of `Drive is not null` | ⭐ the manifest rail, with *"the matrix claims **'IG'** can drive time, but POST /sim/step answered **501**"* | ✅ yes — 📌 **exactly `Q54`'s one named risk** |
| **②** | ⭐⭐ **a host-specific panel divergence** — ExCon's `config` VM flipped one bool | ⭐ **exactly ONE** DIFFERENT: **`config (editor_config vs excon_config): $.grid: golden=false actual=true`**; the two declared divergences stayed in their own bucket | ✅ yes |

⭐ Both were **inverse edits**, the full solution was rebuilt before drawing a conclusion *(the stale-binary
trap)*, and both were restored and re-verified.

## 5. ⭐ RULE 5 — **the ids allocated**

| id | |
|---|---|
| ✅ **`HN-025`** | the dispatcher + the lifted API + the manifest *(items ①②③)* |
| ✅ **`HN-026`** | the conformance suite + the fourth verdict + lockstep *(items ⑤⑧)* |
| ✅ **`HN-027`** | both mutation proofs + the goldens re-proof *(items ⑥⑦)* |
| 🔴 **`HN-028`** | **open** — the ack-gate's cluster half is cross-lane blocked *(TIME lane: expose `MasterSyncController`)* |
| 🔴 **`HN-029`** | **open** — `POST /scenario/load` unsupported in `--mode all` ⇒ conformance cannot equalise the worlds |
| ⚠ **`MX-014`** | **open** — no cluster host publishes a gizmo frame |

⭐ The handoff's series *(`HN-025` / `MX-014`)* was accurate this time; `HN-122` was not needed.

## 6. ⛔⛔ THE TWO MEASURED GAPS — **and why they are stated, not smoothed**

| ⛔ | ⭐ |
|---|---|
| **the cluster step is not confirmed cluster-wide** *(`HN-028`)* | The gate exists and works; its truth lives one accessor away, in a file this lane may not touch. ⭐ `hasMaster:false` is in the manifest **and asserted by a rail**, so the day the TIME lane exposes the master, the rail reddens and the wiring is a two-line change |
| **the two hosts cannot be given the same world** *(`HN-029`)* | ⇒ only world-INDEPENDENT structure is comparable, which is why `entity-inspector` is DECLARED rather than diagnosed. ⭐⭐ **This is the highest-value next step for the harness**: a cluster-side scenario load turns two SAME kinds into a real content comparison |

⚠ **And what conformance does NOT yet cover, stated plainly:** gizmo frames *(`MX-014`)*, the authoring
perspectives' populated state *(`MX-013`, still open from part C)*, and any world-content diff between the
modes *(`HN-029`)*.
