<!--STATUS
state: LIVE
updated: 2026-09-16
current-answer: the whole file — it is a procedure, not a design; every section is current.
  §5a (added 2026-09-16) is the DDS wire-capture recipe (ddsmonitor sniff-to-JSON).
stale-below: nothing
known-rot: nothing known
known-conflict: none. tools/ai-debug-mcp/SKILL.md documents the SAME surface through the MCP
  server; this document is the direct-HTTP path to it and does not contradict it.
-->

# RUNBOOK — debugging a running HROT cluster over plain HTTP

> 🔒 **User, `2026-09-04`, on why this exists:** *"i would like you to start writing a runbook … giving
> intro to how to debug clusters using direct HTTP requests (not via mcp - too often not up and not
> allowing for multi-node cluster setup)"*

⭐⭐⭐ **Every node runs its own debug-API HTTP listener.** The `ai-debug` MCP server is a *client* of that
same surface. Talking to it directly costs nothing, works when the MCP server is down, and — the reason
that decides it — **is the only way to hold several nodes at once**, because each node needs its own port
and the MCP server addresses one.

📄 **The route reference is [`tools/ai-debug-mcp/SKILL.md`](../tools/ai-debug-mcp/SKILL.md) §4** — read it
for what a route *means*. ⛔ Do not derive capabilities from engine `.cs`. This document is the **transport
and the traps**, not a second route list.

---

## 1. ⭐ Launch

### 1.1 One process, all subsystems

```bash
cat > /tmp/launch-all.sh <<'SH'
#!/bin/bash
cd /home/user/HROT
exec env HROT_DEBUG_API_PORT=8111 xvfb-run -a --server-args="-screen 0 1600x1000x24" \
  dotnet Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll --mode all
SH
chmod +x /tmp/launch-all.sh
setsid nohup bash /tmp/launch-all.sh > /tmp/cluster.log 2>&1 < /dev/null &
disown
```

### 1.2 ⭐⭐ Several processes — one node each, **one port each**

This is the shape the MCP server cannot give you.

```bash
D=/home/user/HROT/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll
for spec in "orchestrator:8100" "cgf:8101" "simhost:8102" "ig:8103"; do
  m=${spec%%:*}; P=${spec##*:}
  HROT_DEBUG_API_PORT=$P setsid nohup xvfb-run -a --server-args="-screen 0 1280x800x24" \
      dotnet "$D" --mode "$m" --no-wait > /tmp/n-$m.log 2>&1 < /dev/null &
  sleep 5
done
```

⭐ `--no-wait` skips the waiting-room handshake; without it a node blocks for peers that may never come.

### 1.3 ⛔⛔ Four launch traps, all measured

| trap | symptom | fix |
|---|---|---|
| ⛔⛔⛔ **the process dies with your shell** | the log stops mid-boot, `ps` shows nothing, and it looks like a crash | **`setsid nohup … &` + `disown`.** A plain `&` leaves it in the tool call's process group, which is killed on return. 📌 Cost me two "crashes" that were not crashes |
| ⛔⛔ **`pkill -f` kills your own shell** | the command exits ~144 with no output and nothing ran — including the `cat` that was going to write your script | the pattern matches your own command line. Use the bracket trick: **`pkill -f 'ClusterRunner[.]dll'`** |
| ⛔ **port already in use** | `HttpListenerException (98): Address already in use`, then `Aborted` | a leftover node still holds it. Kill first *(bracket trick)*, or just move to a fresh port — cheaper and avoids racing teardown |
| ⚠ **Xvfb leaks** | second launch hangs on display allocation | `pkill -f '[X]vfb'` after killing the nodes |

### 1.4 Confirming it is up

```bash
grep -iE "Debug API listening|Unhandled|Aborted" /tmp/cluster.log | tail -3
```

A healthy line names the port, the mode and the providers:

```
[Runner] Debug API listening on 8111 — mode=all,
  providers=[SimHost, IG, ExCon, CGF], perspectives=[ExCon, IG, Scenario, SimHost].
```

---

## 2. 🔴🔴🔴 THE TWO TRAPS THAT WASTE THE MOST TIME

### 2.1 ⛔⛔⛔ Use `localhost`, **never** `127.0.0.1`

The listener binds `http://localhost:{port}/` *(`DebugApiHost.cs:100`)*. `HttpListener` matches on the
**hostname**, so a request to `127.0.0.1` returns

```html
<h1>Not Found (Not Found)</h1>
```

⚠ **on every route**, including ones that certainly exist. 📌 That looked exactly like "the API is not
wired up" and cost a full diagnostic detour.

```bash
curl -s --noproxy '*' http://localhost:8111/status      # ✅
curl -s --noproxy '*' http://127.0.0.1:8111/status      # ⛔ 404 on everything
```

⭐ Always pass **`--noproxy '*'`** — this environment has an HTTPS proxy that will otherwise intercept.

### 2.2 ⛔⛔ The cluster boots **PAUSED**

`startPaused: true` is deliberate *(`CE-101`)*. The world ticks, the clock does not.

```bash
curl -s --noproxy '*' -X POST http://localhost:8111/sim/play \
     -H 'Content-Type: application/json' -d '{}'
```

⚠ **`-d '{}'` is required** — a bodyless POST returns `411 Length Required`.
⭐⭐ **Verify by `simTime`, not by the response**: read `/status` twice and check `simTime` advanced.
⛔ `PumpFrames`-style spinning against a stopped clock produces `DeltaTime = 0` and every integrating
system silently does nothing — the disease `CE-148` documents.

---

## 3. ⭐ The core loop

```bash
B=http://localhost:8111
curl -s --noproxy '*' $B/status
curl -s --noproxy '*' $B/scenarios
curl -s --noproxy '*' -X POST $B/scenario/load/live \
     -H 'Content-Type: application/json' -d '{"name":"hill-attack","waitForReady":true}'
```

A good load answers:

```json
{"ok":true,"data":{"loaded":"hill-attack","awaited":true,"target":"OperatingLive",
                   "entityCount":8,"sawWorldChange":true,"hadWorldAnchor":true}}
```

⭐ `sawWorldChange` + `hadWorldAnchor` are the honest bits: `ok:true` alone only means the intent was
accepted.

---

## 4. ⭐⭐⭐ PERSPECTIVES — **`--mode all` reads ONE node at a time**

The single most misleading thing about a one-process cluster: **the nodes disagree, and every entity read
answers for the active perspective only.**

```bash
curl -s --noproxy '*' -X POST $B/perspective \
     -H 'Content-Type: application/json' -d '{"name":"SimHost"}'
```

| ⚠ | |
|---|---|
| ⛔ the body key is **`name`**, not `perspective` | wrong key ⇒ `400`, and the 400 names the legal set |
| ⛔ **`?perspective=` in a query string is IGNORED** | it is not implemented on any route; you get a hint saying so |
| ⭐⭐ **always read `current` back** | an unknown name is a no-op |
| ⭐ **ExCon has no ECS world** | entity routes there answer `NOT_SUPPORTED_HERE`, correctly |

📌 **Measured, entity 1000, one process, same instant:**

| perspective | node | `HasAuthority` | `PrimaryOwnerId` | behaviour hash |
|---|---|---|---|---|
| Scenario (CGF) | 400 | ✅ true | **400** | `1234950103` |
| SimHost | 1 | false | **-1** | `0` |

⇒ ⭐⭐⭐ **Read the entity on BOTH nodes before concluding anything about "the cluster".** A defect can live
entirely in the gap.

---

## 5. ⭐⭐⭐ `/diagnostics/architecture` — **the highest-value route**

```bash
curl -s --noproxy '*' $B/diagnostics/architecture
```

Per **subsystem** *(not per node — a `--mode all` node runs several kernels side by side)*: every module,
ECS system and DDS translator, and for each translator **`sentSamples` / `receivedSamples`**.

```json
{"systemName":"CycloneNetworkIngressSystem","direction":"Ingress",
 "topic":"SensorConfig","descriptorOrdinal":60,"receivedSamples":6,"sentSamples":0}
```

⭐⭐⭐ **This answers "is the wire carrying X?" without touching the engine.** 📌 The lesson that produced
this section: I proposed *adding* sample counters to two translators — they had been published here all
along.

⭐ **How to read it — a worked triage:**

| topic | sent → recv | reading |
|---|---|---|
| `SensorConfig` | CGF **6** → SimHost **6** | ✅ that direction of the wire is healthy |
| `SST_OwnershipUpdate` | SimHost **16** → CGF **16** | ✅ ownership handover crossing |
| `EntityMaster` | CGF **8** → both **8** | ✅ spawn path fine |
| 🔴 `SensorTrackState` | **0 → 0** | the only silent topic — **and silent on the SEND side** |

⇒ ⭐⭐ **`sent == 0` means the producer never published; it does NOT mean DDS is broken.** With other
topics flowing in both directions, DDS is *proven* healthy and the fault is upstream of the egress.
⛔ **`sent > 0` with `recv == 0` is the opposite finding** — that one is the wire or the topic.

⚠ **`sent == 0` can also be CORRECT.** Confirm the producer had anything to say before calling it a
defect: here `SensorContactList.Count` was `0` on every observer, so publishing nothing was right.

---

## 5a. ⭐⭐ DDS WIRE CAPTURE — sniff the bus to JSON with `ddsmonitor`
⭐ **This is NOT HTTP** — it is a separate CycloneDDS instrument. §5's translator counters answer *"did it
leave?"*; `ddsmonitor` answers *"what EXACTLY crossed the wire, from which node, in what order"* — the sample
payloads, the sender identity, and the `InstanceState` *(alive / disposed / no-writers)*. Reach for it when the
HTTP diagnostics say a sample should have flowed but the receiver disagrees, or to see lifecycle/dispose traffic
directly *(e.g. the reliable-init barrier: `EntityMaster` `WaitForAcks`, `EntityLifecycleStatusDescriptor`,
`OwnershipUpdate`, `NodeCapabilities`, and the dispose sample of an aborted create)*.

**Install:** it is a .NET global tool provisioned by `scripts/cloud-bootstrap.sh` *(pkg `cyclonedds.net.ddsmonitor`)*;
`ddsmonitor` is on PATH via the `/usr/local/bin` wrapper. Full arg docs: **github.com/pjanec/CycloneDds.NET →
`tools/DdsMonitor/README.md`**.

**Record the bus to JSON** *(headless; runs until `Ctrl+C`; no browser)*:
```bash
ddsmonitor --DdsSettings:HeadlessMode=Record \
           --DdsSettings:HeadlessFilePath=/tmp/cap.json \
           --DdsSettings:DomainId=0 \                     # ⛔ MUST match the cluster's DDS domain
           --AppSettings:TopicSources:0=/home/user/HROT/Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0
# ...let the scenario run, then Ctrl+C. /tmp/cap.json is a JSON array of samples.
```
🔴🔴 **`--AppSettings:TopicSources:0=<dir-or-dll>` IS MANDATORY** *(measured `2026-09-16`)* — ddsmonitor is a
GENERIC sniffer: it discovers every topic over SEDP but can only DECODE/record a topic whose `[DdsTopic]` type it
has loaded. Point it at the cluster's **build-output dir** *(the message DLLs: `Hrot.Network.NED`, `Fdp.Network.Cyclone`,
`Fdp.Core`, …)*. ⛔ **Without it the capture is an empty `[`** — it only knows its own self-test types. Multiple dirs/DLLs:
`--AppSettings:TopicSources:0=… --AppSettings:TopicSources:1=…`. *(A persisted list also lives at `$APPDATA/DdsMonitor/assembly-sources.json`; the CLI list overrides it.)*
Filter to just the barrier topics *(glob on the fully-qualified type name)*:
```bash
ddsmonitor --DdsSettings:HeadlessMode=Record --DdsSettings:HeadlessFilePath=/tmp/cap.json \
  --AppSettings:IncludeTopics:0="*EntityMaster*" \
  --AppSettings:IncludeTopics:1="*EntityLifecycleStatus*" \
  --AppSettings:IncludeTopics:2="*OwnershipUpdate*"
# drop noise instead: --AppSettings:ExcludeTopics:0="*Heartbeat*"
# value filter (Dynamic-LINQ): --DdsSettings:FilterExpression="Payload.State ne 1"
```
⭐⭐ **DEFAULT TO A FILTER — capture-all is for rich diagnosis only.** 📌 Measured `2026-09-16`: an unfiltered
capture was **93 MB / 2167 samples, of which 1801 (83 %) were `DebugPrimitivesBatch`** — pure gizmo-draw noise that
buries the lifecycle traffic you actually came for. ⇒ start with `--AppSettings:IncludeTopics:*` scoped to the topics
in question *(the barrier set above)*, or at minimum `ExcludeTopics *DebugPrimitives* / *Heartbeat* / *TimeSync*`.
⛔ Only drop the filter when you genuinely need the full bus *(chasing an unknown flow, ordering across all topics)*.
Multi-domain / partitioned capture: `--DdsSettings:Participants:0:DomainId=0`
`--DdsSettings:Participants:1:DomainId=5 --DdsSettings:Participants:1:PartitionName="sim"`.
Replay a capture back onto the bus: `--DdsSettings:HeadlessMode=Replay --DdsSettings:HeadlessFilePath=/tmp/cap.json [--DdsSettings:ReplayRate=2.0]`.

**Each JSON entry:** `Ordinal` · `TopicTypeName` · `Timestamp` · `DomainId` · `PartitionName` ·
`Sender` *(PID / process / machine IP)* · `InstanceState` · `Payload`.

| ⚠ traps | |
|---|---|
| 🔴 **empty `[` capture** | **almost always missing `--AppSettings:TopicSources`** *(the type assemblies — see above)*; less often a `DomainId`/partition mismatch. Fix TopicSources first |
| ⛔⛔ **run ONE instance, to a fresh file** | ddsmonitor starts a Kestrel web host on **`:5000`**; a leftover instance holds `:5000` and the next run **crashes at startup** *(`address already in use` → 0-byte capture)*. Kill stale instances *(`pkill -f 'Headless[M]ode=Record'` — bracket-trick so it can't match your own shell)*, or give each a distinct port `ASPNETCORE_URLS=http://127.0.0.1:5088`. ⚠ Two instances writing the SAME `HeadlessFilePath` interleave → corrupt/unclosed JSON |
| ⚠ **the array closes on clean `Ctrl+C`/SIGINT** | an abrupt kill (or concurrent writers) leaves the JSON unclosed; grep `"TopicTypeName"` still works, or append `]` to parse |
| ⛔ **it needs `DOTNET_ROOT`** | the `/usr/local/bin/ddsmonitor` wrapper sets it; if you run the raw `~/.dotnet/tools/ddsmonitor`, export `DOTNET_ROOT=$HOME/.dotnet` first |
| ⚠ **run it BESIDE the cluster** | start the capture, then run the scenario *(§1/§3)*; a late-started capture misses the create/spawn burst unless the topic is durable |

> ✅✅ **VERIFIED WORKING `2026-09-16`** *(corrects an earlier wrong note that claimed cross-process discovery was
> blocked here — it is NOT)*. Against a live **multi-process** cluster *(§1.2: separate orchestrator/cgf/simhost/ig
> processes)* on domain 0, a separate ddsmonitor process **captured 2167 fully-decoded samples** across 10 HROT
> topics — `GizmoMap.Network.DebugPrimitivesBatch` (1801), `TimeSyncRequest`/`Response` (117 each), `NodeHeartbeat`
> (115), `AssetInventoryTopic`, `ClusterStateTopic`, `EntityAttributeSchema`, `IdStatus`, … — with decoded payloads
> and distinct sender PIDs *(cross-process proven)*. ⭐ The two things that made it work: **`--AppSettings:TopicSources`**
> pointed at the build-output DLLs *(else empty)*, and **one instance / clean port** *(a stale `:5000` crashes the run)*.
> ⚠ In `--mode all` (single process) there is little cross-process wire traffic to see — use the **multi-process**
> launch (§1.2) for a meaningful capture.

---

## 6. ⭐ Reading entities

```bash
curl -s --noproxy '*' $B/entities            # [{networkId, ...}]
curl -s --noproxy '*' $B/entities/1000       # { EntityId, NetworkId, Components:{...} }
```

| ⚠ | |
|---|---|
| ⛔ **`Components` and every name inside it are PascalCase** | indexing lowercase silently yields nothing and reads like an empty entity |
| ⭐⭐ **measure motion as Δposition over ΔsimTime** | never over wall-clock. Identical positions across a *real* simTime advance is evidence; across a stalled clock it proves nothing |
| ⭐⭐ **intent vs status distinguishes three bugs** | intent empty ⇒ nothing issued it · intent set + status absent ⇒ the consumer never ran · both set + zero velocity ⇒ the consumer ran and produced nothing |
| ⭐ **check authority before filing "the write did nothing"** | a write to an entity this node does not own is legitimately dropped |

⭐ **The components worth reading first for an AI/combat question:** `NetworkAuthority`,
`PendingAuthorityGrants`, `BehaviorState.ActiveBehaviorHash`, `MissionPlanQueue.PhaseCount`,
`SensorContactList.Count`, `ActiveSensorTracks.Count`, `LocomotionChannel.Status`,
`WeaponChannel.Status`, `Health.Current`.

---

## 7. ⭐ Logs

```bash
grep -icE "Strict Mode Violation|Unhandled|Exception" /tmp/cluster.log
grep -iE "DeferredTakeover|GhostPromotion|OwnershipUpdate" /tmp/cluster.log | tail
```

⚠ **The module host's fault handling CHANGED — verify which mode you are in** *(CE-188/CE-189; user
ruling `2026-09-04`)*. `FdpConfig.FailFastOnModuleException` now ships **ON by default**
(`FdpConfig.cs:136`), so a module `Tick` exception is **RE-THROWN** (`ModuleHostKernel.ReportModuleFault`
→ `ExceptionDispatchInfo…Throw()`), not swallowed — a fault surfaces loudly on the first frame instead of
hiding. The old swallow-every-frame behaviour is now **opt-in**: only when `FDP_FAIL_FAST=0` (or
`false`/`off`) is set at process start.

⛔ **When swallowing IS enabled (`FDP_FAIL_FAST=0`)**, the log line is now
`[ModuleHost] {Sync|Async} module '<name>' FAULT (frame N, T total for this module). FDP_FAIL_FAST=0 keeps
the node alive instead.` followed by the exception — and it **de-duplicates**: the first occurrence of a
fault signature prints, then repeats print only at powers of ten (10th, 100th, 1000th…). So a control
plane throwing every frame under `FDP_FAIL_FAST=0` shows up as a handful of lines, not a flood, while the
API still answers `ok:true`. ⚠ The old `[ModuleHost] Sync Module '<name>' exception:` string no longer
exists — grep for `FAULT` and `FDP_FAIL_FAST`.

📌 **The find that justifies grepping first** *(measured under the pre-`2026-09-04` swallow-by-default
regime)*: an unregistered `OwnershipUpdate` event threw inside `DeferredTakeoverSystem` on every tick.
Nothing in `/status` or the entity dumps said so; **the log line was the only evidence**, and the fix took
0/8 entities moving to 4/8. Under today's fail-fast default the same bug would crash the node on frame 1 —
still grep the log, but expect a throw, not a silent survivor, unless `FDP_FAIL_FAST=0` is set.

⭐ Over HTTP, `/logs` takes `level` — **always pass `level:"Info"` on a cluster**, or time-sync `Trace`
chatter fills the whole window *(measured: 176 of 200 entries)*.

---

## 8. ⚠⚠ `hill-attack` is **NON-DETERMINISTIC** — assert the chain, not the numbers

> 🔒 **User, `2026-09-04`:** *"the scenario is not deterministic, entities do not go the same ways but they
> always go towards enemy until they 'see' it and then fire until enemy is destroyed."*

⇒ ⛔⛔ **Never assert an exact position, path or acquisition range.** Two runs differ legitimately.
⭐⭐⭐ **Assert the INVARIANT CHAIN instead** — each link is a component read, and the first broken link is
your defect:

| # | link | observable |
|---|---|---|
| ① | the friendlies **advance toward the enemy** | Δposition over ΔsimTime, distance to nearest hostile falling |
| ② | they **acquire** | `SensorContactList.Count > 0`, then `ActiveSensorTracks.Count > 0` on the Brain |
| ③ | they **fire** | `WeaponChannel.Status` reaches `Running` |
| ④ | the enemy **dies** | `Health.Current` falls to 0 |

⭐ **Stopping without acquiring is out of spec** — the entities are supposed to keep closing until they
see. ⛔ Do not explain a halt away as "out of range".

#### ⭐⭐⭐ LINK ⑤ — **the run must END** *(added `2026-09-13`; ⑤ CORRECTED `2026-09-21`)*

| # | link | observable |
|---|---|---|
| ⑤ | the targets are **DEAD** — `Health.Current: 0` — and they **STAY IN THE WORLD** | ⭐ `GET /entities/<id>` ▸ `Components.Health.Current == 0`. ⛔⛔ **The entity COUNT does NOT fall** — `--mode all` stays at **8** |
| ⑥ | every attacker is **home on the baseline** and stationary | `LocomotionChannel.Status: Success`, `NavigationIntent.FinalDestination` ≈ its own position, `WeaponState.Ammo` STOPS changing |

> 🔴🔴 **⑤ USED TO SAY *"they disappear from `GET /entities`"* AND THAT IS NOW WRONG.** It was written
> with `CE-267`, which destroyed an entity at 0 HP — and **`CE-272` REVERTED `CE-267`** on an explicit
> ruling. 🔒 **User, `2026-09-13`:** *"dead entity should not vanish, it should stay dead in the world,
> every entity (no magic dead body vanishing)."*
> ⇒ ⭐⭐ **a steady entity count is the CORRECT observation, not a broken run.** `CE-272`'s own live
> verification records it: *"`--mode all` — Muscle Health 3000→25→0, attacker withdrew … and STAYED
> stopped, **count steady at 8**"*.
> ⚠ 📌 **Measured `2026-09-21`:** waiting for the count to fall reads a healthy run as a hung one — both
> `hill-attack` and `hill-attack-close` sat at 8 entities with both hostiles at `Health 0` and zero
> exceptions, which is exactly right. ⭐ **Assert `Health.Current == 0`, never the count.**

⛔⛔ **DO NOT measure "on the baseline" as distance to ONE POINT.** 📌 That mistake was made on
`2026-09-13` and read as a half-failure: `BrainBlackboard.BaselineX/Y` *(`527.5, 474.5`)* is the **centre of
a line**, and four tanks park abreast along it ~50 m apart, so the outer two are legitimately **~75 m** from
that point. ⭐ **Compare each tank to its OWN `NavigationIntent.FinalDestination` instead** — it matches its
position within a metre when the tank is home.

⭐⭐ **The cheapest completion check is AMMO.** It drains while engaging and freezes when the run ends.
📐 Measured after the fix: `42 → 41` *(one round each, matching the blueprint's `MaxRounds: 1`)* and
unchanged over 30 s. ⛔ Before the fix it ran `42 → 19` and never stopped.

📐 **Reference run, distributed CGF+SimHost, `2026-09-13` post-fix:** both hostiles destroyed by `t≈37`,
all four attackers stationary on the baseline by `t≈59`. ⚠ **Orientation only, NOT an assertion** — §8's
whole point is that this scenario is non-deterministic.

### 8.1 ⛔⛔⛔ READ THE RUNTIME VALUE OFF THE ENTITY — **never off a source file**

> 🔒 **User, `2026-09-04`:** *"you have the way of dumping any entity so pls use it instead of
> theoretizing."*

📌 **The case that cost three corrections in one exchange.** Asked whether the tanks were within sensor
range, I grepped `BdcTkbCatalog.cs:50` — `SensorRange = 8000` — and concluded they were *"fifty times
inside range"*. 📐 **The live entity says `VisionRange: 100`**, because
`scenarios/hill-attack/scenario.json` overrides it on six entities *(deliberately small, to exercise the
sensor)*. The friendlies were **37–46 m OUTSIDE** vision, i.e. the exact opposite of the conclusion.

⇒ ⭐⭐⭐ **A catalog default is not the value in play.** A scenario-stored component override silently
beats it, and nothing in the code says so.

| ⛔ do not read from | ⭐ read from |
|---|---|
| a TKB catalog `.cs` default | `GET /entities/<id>` ▸ `Components.PerceptionReceptor.VisionRange` |
| a preset table | `Components.VehicleParams` |
| `TkbType`, to infer the faction | ⭐⭐ **`Components.EntityInfo.ForceId`** — `"Friend"` / `"Hostile"` |

⛔⛔ **`TkbType` IS NOT THE FACTION.** 📌 In `hill-attack` the hostiles `1006`/`1007` are **`Tkb 100`,
M1 Abrams — the same type as the friendlies.** Splitting sides by type gets them backwards.

⭐ **Two reads that decide "is this entity idle or un-commanded?"** — they look identical from position
alone:

| observed | reading |
|---|---|
| `NavigationIntent {Mode: None, IntentId: 0, FinalDestination: [0,0,0]}` | ⭐ **nothing ever commanded it** — not stuck, never told to go |
| `BehaviorState.ActiveBehaviorHash: 0` + `MissionPlanQueue.PhaseCount: 0` | ⭐⭐ **no behaviour is running at all** — no amount of navigation debugging will help |

⚠ **And never compute a distance against `FinalDestination` without checking it is non-zero** — a
`[0,0,0]` destination yields a large, meaningless number that reads like "still has far to go".

---

## 9. ⭐⭐ `--mode editor` is the ORACLE for everything except the translators

The editor composes **both** halves in **one world** — the producer `CognitiveSpatialModule`
*(`EditorSubsystem.cs:1344`)* and the consumer `CgfLogicPack` → `ActiveSensorTracksUpdateSystem` +
`CgfThreatEvaluationSystem` *(`EditorSubsystem.cs:1362`)*. Events cross the world bus directly, so
**there is no network hop at all.**

```bash
HROT_DEBUG_API_PORT=8131 … --mode editor
```

⇒ ⭐⭐⭐ **Anything the editor does correctly is proven for the shared code and NARROWS a cluster defect to
the translators.** 📌 Measured `2026-09-04`: in the editor the tanks advanced, acquired
*(`SensorContactList.Count = 1`)*, fired *(`WeaponChannel.Status = Running`)* and **killed** *(a hostile
at `Health 0`)* — while `--mode all`, same scenario and assets, produced none of it. That single
comparison bracketed the defect from both sides in one run.

⚠ **What it cannot tell you:** anything about `Cyclone*` translators, DDS topics, descriptor ordinals or
authority handover — those exist only when there is a wire.

### 9.1 ⭐⭐⭐ THE LOCKSTEP DIFF — **step BOTH hosts and diff ONE entity per step**

📌 **`2026-09-04`, `CE-172`.** *"The tanks behave differently on the cluster"* is unanswerable as stated.
⭐ It became a two-line answer by running both hosts under **deterministic stepping** and reading the same
entity after each step:

```bash
for P in 8111 8131; do curl -s --noproxy '*' -X POST http://localhost:$P/sim/step \
    -H 'Content-Type: application/json' -d '{"count":30}' > /dev/null; done
curl -s --noproxy '*' http://localhost:8111/entities/1000   # cluster — set perspective first, §4
curl -s --noproxy '*' http://localhost:8131/entities/1000   # editor
```

| ⭐ trap | |
|---|---|
| ⛔⛔ **`/entities/{id}/state` is a SUMMARY** — position, speed, and a three-field `behavior` block | ⭐ the **components** live at **`/entities/{id}`**, under a `Components` key. Reading the wrong one looks like *"the field is missing"* |
| ⛔⛔ **the editor ignores `/sim/step` until it has been PLAYED once** | ⭐ `POST /sim/play` → `POST /sim/pause` → then step. The cluster steps from a cold load |
| ⛔ **a `/scenario/load/live` that answers `sawWorldChange: false` did NOT reset the clock** | ⚠ you are stepping a world that already ran; restart the process if you need `t=0` |
| ⭐⭐ **the sharpest single field is `BrainBTreeState.State.RunningNodeIndex`** | 📌 it is what split `CE-172`: cluster `3 → 7 → dead`, editor `3 → 9 → held`. Map the index with a pre-order walk of the asset's `Nodes` in `*.btree.json` |

### 9.2 ⛔⛔⛔ READ A BLACKBOARD **BEFORE** THE BEHAVIOUR IS CLEARED — **or the instrument lies**

📌 **`CE-172` cost a wrong lean to this.** `BrainBlackboard.BehaviorParameters` came back `{}` on the
cluster and fully populated on the editor, which reads exactly like *"the params never resolved."*
⛔ **False.** The dump decodes that blob against the **active behaviour's** params DTO; once
`ActiveBehaviorHash` is `0` there is no DTO, so it renders empty **whatever the bytes hold.** Stepping the
same node from `t=0` showed the cluster's params byte-identical to the editor's from the first frame.
⇒ ⭐⭐ **an empty `BehaviorParameters` next to `ActiveBehaviorHash: 0` is a rendering artefact, not a
measurement.**

### 9.3 ⭐⭐ THE TRANSLATOR COUNTERS ANSWER *"DID IT EVEN LEAVE?"* IN ONE CALL

`/diagnostics/architecture` carries `sentSamples` / `receivedSamples` **per translator per subsystem**:

```bash
curl -s --noproxy '*' http://localhost:8111/diagnostics/architecture | python3 -c "
import sys,json
d=json.load(sys.stdin)['data']
for s in d['subsystems']:
    for t in s.get('translators') or []:
        if 'AreaQuery' in json.dumps(t): print(s['subsystem'], json.dumps(t))"
```

⭐⭐⭐ **Four zeros across a registered four-translator round trip means the message never reached the
wire — the break is UPSTREAM, in the egress translator's own filter, not in DDS.** 📌 That is precisely
what `CE-172` was, and it is the cheapest possible discriminator between *"the wire is broken"* and
*"nothing was ever sent."* ⚠ Registration is **not** traffic: all four were correctly registered the whole
time.

### 9.4 ⭐⭐ GREP THE NODE'S OWN LOG BEFORE THEORISING

📌 `CE-172`'s root cause was printed at `ERROR` level, in `/tmp/cluster.log`, from the first run:
`"EQS area query timed out after 5.0s. RequestId=0."` — and the editor's log had the matching `Submitted`
line with **no** timeout. ⇒ ⭐ **diff the two logs for the feature's own words before reasoning about
mechanism.** The `Behavior` logger prints `Entity:[…] Behavior:[…] Node:["…"]`, so a single grep names the
failing BTree node.

---

## 10. ⭐ A worked session, end to end

1. Launch `--mode all`, confirm the listening line. → §1
2. `/status` on **`localhost`**. → §2.1
3. Load the scenario, check `sawWorldChange`. → §3
4. `POST /sim/play` with `-d '{}'`, confirm `simTime` advances. → §2.2
5. Sample entity positions twice over a real `simTime` delta. → §6
6. **Nothing moved?** → grep the log for module `FAULT` lines / a re-thrown exception **before** reading any more state (§7 — fail-fast is ON by default now). → §7
7. Read the entity from **every** perspective; compare authority and behaviour. → §4
8. `/diagnostics/architecture`; find the silent topic and which side is silent. → §5
9. Cross-check the same chain in `--mode editor` to split shared-code from wire. → §9
10. Report the first broken link in §8's chain — not the symptom at the end.

---

## 11. ⛔ Cleanup — always

```bash
pkill -9 -f 'ClusterRunner[.]dll'; sleep 2; pkill -9 -f '[X]vfb'
```

⚠ Leftover nodes hold ports **and** DDS domains; the next run then fails in ways that look like product
defects.
