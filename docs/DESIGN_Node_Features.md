<!--STATUS
state: LIVE
build-state: S1 READY-TO-BUILD (2026-11-01, §4 F-13…F-18 — leans await the user) · the model AGREED by the user 2026-10-31 ("Good, this sounds like what is needed", "Yes, please write it down") · S2, S3 DESIGN
updated: 2026-11-01 (S1 made buildable: the gate, the two delivery paths, the recording, the first two features — coordinator)
current-answer: §2 the three tiers · §3 the diagrams · §4 decisions F-1…F-18 · §5 slices (S1 buildable) · ⭐ §6 the S1 technical spec (catalogue, C# shapes, where it lives, speed, wire, recording + going live)
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - docs/DESIGN_Scenario_Rules.md — §00.6 S1 NEEDS this design's S1: the feature `scenario.rules` gates the scenario-rules pack (R-4108); SR-6 (a scenario with rules refuses to start when no node has it on); SR-101's run variant + params share F-15's recording seat
  - docs/DESIGN_Cluster_Load_Phase.md — the load PREPARE payload (`EditLoadHandlerPayload`) F-14 stamps AtLoad features into, as CE-3354 ③ stamps MapInLoad
  - docs/DESIGN_Player_Join.md — §7c (R-4115): gameplay packs are composed by the shared bootstrap and their features default ON; a pack runs when any of its features is on
  - docs/PLAN_Training_And_Gameplay_Engine.md — every item there is delivered as features (P-7 scenario presets of these switches)
  - docs/DESIGN_Node_Roles_And_Policies.md — §3.2–§3.4 OWN composition by role (R-4000, R-4005): a role sets DEFAULTS through composition; a switch never replaces composition
  - docs/DESIGN_Player_Join.md — the deploy server is the feature gameplay.deploy-server (D-3 there); its §7a audit items G1…G6 become hud.* features here (S2)
  - docs/blueprints/Architect_Question_108_Run_Mode_Picker.md — D15 OWNS the cluster run settings (SetRunSettings, "Ask to keep recordings") the cluster override rides
  - docs/DESIGN_Cluster_Load_Phase.md — R-1046 / CE-3354 ③ OWN "the 3-D map in the load" (NodeRunProfile.MapInLoad + the master's MapInLoadNodes): the pattern this generalises and its first member (S1)
  - docs/blueprints/Architect_Question_70_Host_Capabilities_And_Reliable_Init_Degradation.md — AQ-70 §Q70-B/C OWNS the capability tokens on the durable NodeCapabilities topic a feature's availability is advertised on
-->

# DESIGN — **node features** *(one switch structure: composed where the code is, on or off per node and per cluster)*

> 🔒 **User, `2026-10-31`:** *"the node should have some configuration structure for features to be turned on or off, and the role which is
> assigned to the node by composition usually, but also via the minus M switch, should simply turn the features on or off. And that could happen
> cross-cluster, the same way as we are switching the 3D map loading, waiting, on or off. That way it wouldn't matter much what is strictly military
> and what is strictly gameplay … we can create a mixture of these that suits the needs of the training or the gameplay … It could be more granular."*
> · *"is this feature setting on or off related to the capabilities that are announced by different nodes? So the feature can be turned on only if
> the capability is saying that the node has it."*

⭐ **R-4108 (`2026-10-31`):** this structure is the rule for EVERY behaviour of the engine, not only gameplay — *"Nothing is hard-coded, everything is a feature that can be switched on or off and announced as a capability."*

**The idea in one line:** a **feature** is a named behaviour declared by the capability whose code implements it; a node ADVERTISES it as
available when that code is composed, and whether it is ON is decided by three layers — the composition's default, the node's own override,
the cluster's override — so any mixture of training and gameplay behaviour can be assembled at run time.

## 1. INVENTORY *(`2026-10-31`)*

Queries run: `scripts/cbm.sh search_graph` Class `.*(Feature|Toggle|Flag|Settings|Config).*` (the hits: per-tool settings — `AudioSettings`,
gizmo `*Settings`, `UnitDebugSettings`, `MapUserConfig`; `HrotNodeConfig`) · grep `MapInLoad` · grep `CapabilityKeys` / `CapabilityTokens` /
`NodeCapabilitiesEvent` · the run-settings path (`SetRunSettingsIntent`, `ClusterView`).

| what exists | where | used here as |
|---|---|---|
| composition units, role-sized: 8 keys (`cap:brain`, `cap:muscle-ground`, `cap:perception`, `cap:navigation-solver`, `cap:image-generator`, `cap:animation-muscle`, `cap:traffic`, `cap:traffic-mirror`) | `CapabilityKeys`, `INodeCapability` (`Hrot.Common/Infrastructure/NodeCapability.cs:25-46, :142`) | tier ① — the OWNER of a feature |
| advertised tokens, published ONCE at join on a durable topic; roles derived from `fdp.role.*`; feature tokens like `fdp.reliable-init` | `NodeCapabilitiesEvent` (`ClusterCqrsEvents.cs:109-117`), `CapabilityTokens`, `NodeRoster.Supports` | tier ② — a feature's AVAILABILITY (`hrot.feature.<id>`) |
| ONE switch in two layers: the node default from composition and a cluster override naming nodes | `NodeRunProfile.MapInLoad` (R-1046; `NodeRunControls.cs:20-23`) · `ClusterView.MapInLoadNodes` (CE-3354 ③), sent by `SetRunSettingsIntent` | tier ③'s pattern, generalised; its first member |
| the cluster run settings: master-owned, broadcast, read through `ClusterView` | `SetRunSettingsIntent` → master → `ClusterStateTransitionedEvent` → `ClusterView` (`AskToKeepRecordings`, `MapInLoadNodes`) | the cluster override's wire |
| the heartbeat carries strings | `NodeHeartbeat` `[DdsManaged] string Step, Reason, NodeName` (`OrchestrationMessages.cs:435-440`) | each node's EFFECTIVE feature state |
| the node's base config is infrastructure only | `HrotNodeConfig` (domain, node id, staging, name) | where the node override is parsed into |
| per-tool settings (gizmo, audio, debug) | `*Settings` classes | ⛔ NOT features: local UI preferences of one window, never cluster-switched |

| claim | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| composition is role-sized; a feature is finer | ✅ 8 keys | ✅ Node Roles §3.2 |
| availability may ride the capability tokens | ✅ tokens are namespaced, extensible (`fdp.reliable-init`) | ✅ AQ-70 §Q70-B ("feature tokens") |
| the on/off state may NOT ride them (published once) | ✅ `NodeCapabilitiesEvent` *"Published ONCE at join"* | — |
| the cluster override has a home | ✅ `MapInLoadNodes` on the run settings | ✅ AQ108 D15, CE-3354 ③ |
| an AtLoad feature's state is recorded with the run | ⛔ **measured `2026-11-01`: NO** — `RecordingMetadata` (`Fdp.Core/FlightRecorder/Metadata/RecordingMetadata.cs:8-117`) holds scenario, terrain, exercise / parent ids, checkpoints, reviews and a free `CustomTags` map, but no run settings ⇒ F-15 adds them | — |
| ⭐ an AtLoad switch already reaches a node FROZEN per load | ✅ `ClusterMaster.FanOutLoadPrepare` stamps `EditLoadHandlerPayload.MapInLoad = true` per node whose name is in `MapInLoadNodes` (`ClusterMaster.cs:2392-2400`) | ✅ CE-3354 ③ ⇒ F-14 generalises it |
| ⭐ a Live switch already reaches every node | ✅ `MapInLoadNodes` crosses as a comma string on the cluster facts and is folded into `ClusterView` (`ClusterFactsIngress.cs:180-181`); the master keeps it on `ClusterMaster.RunSettings.cs:43-47, :97` | ✅ AQ108 D15 |
| ⭐ the scheduler already gates systems by a cached ATTRIBUTE | ✅ `SystemScheduler` reads each system's `[TickMode]` once (`TickModes.OfSystem`, `Scheduling/SystemScheduler.cs:24-95`) and skips a `WhenTimeAdvances` system on a paused frame | ✅ CE-3293 / R-3036 ⇒ F-13 puts the feature gate beside it |
| the capability interface today | `INodeCapability { Key; Needs; PopulateSystems(…); ProvideModules(); Register(…) }` — no features (`NodeCapability.cs:142-178`) | — |
| the command line | `--map-in-load` is a `CommandLineParser` option with a sentinel for "no names" (`HrotRunnerConfiguration.cs:28-40`) | ⇒ `--feature` copies it |

## 2. The three tiers

| tier | what it is | granularity | lifetime |
|---|---|---|---|
| **① composition capability** | the CODE a node loaded (systems, modules) | coarse — role- or pack-sized | fixed at boot |
| **② availability token** `hrot.feature.<id>` | "this node HAS feature X" — advertised because its owner is composed | one per feature | static (published once at join) |
| **③ feature switch** | "feature X is ON here now" | one per feature | dynamic |

## 3. Diagrams

```mermaid
classDiagram
    direction LR
    class INodeCapability { <<existing, grows>> Key · Needs · +Features: FeatureDeclaration[] (default none) }
    class FeatureDeclaration { <<new>> Id · Description · DefaultOn(composition) · Change: Live or AtLoad }
    class FeatureRegistry { <<new, per node>> the declarations of the composed capabilities · Available(id) }
    class NodeFeatures { <<new, per node, the ONE reader>> On(id) · effective = cluster ▸ node ▸ default · Changed event }
    class NodeFeatureOverrides { <<new>> parsed from --feature id=on|off and the node config }
    class ClusterView { <<existing, grows>> FeatureOverrides: (feature, nodes or all, on) — replaces MapInLoadNodes }
    class SetRunSettingsIntent { <<existing, grows>> + FeatureOverrides }
    class NodeCapabilitiesEvent { <<existing>> + hrot.feature.id tokens for every Available feature }
    class NodeHeartbeat { <<existing, grows>> + FeaturesOn (the effective ON set, for the operator grid) }
    class FeatureGridPanel { <<new, operator UI>> nodes × features · not available / on / off · toggles send SetRunSettings }
    class IFeatureGate { <<new, in Fdp.ModuleHost>> On(id) — what the scheduler asks }
    class NodeFeatureAttribute { <<new>> [NodeFeature(id)] on a system or its module }
    class SystemScheduler { <<existing, grows>> caches TickMode AND NodeFeature per system type; skips a system whose feature is off }
    class EditLoadHandlerPayload { <<existing, grows>> MapInLoad → FeaturesAtLoad: the node's effective AtLoad ON set }
    class RecordingMetadata { <<existing, grows>> + FeaturesAtLoad (and SR-101's variant + params) }
    NodeFeatures ..|> IFeatureGate
    SystemScheduler ..> IFeatureGate : asks per frame
    SystemScheduler ..> NodeFeatureAttribute : reads once per type
    NodeFeatures ..> EditLoadHandlerPayload : AtLoad set, frozen per load
    RecordingMetadata ..> EditLoadHandlerPayload : records what the load received
    INodeCapability *-- FeatureDeclaration : declares
    FeatureRegistry ..> INodeCapability : collects at boot
    NodeFeatures ..> FeatureRegistry
    NodeFeatures ..> NodeFeatureOverrides
    NodeFeatures ..> ClusterView
    NodeCapabilitiesEvent ..> FeatureRegistry : advertises
    NodeHeartbeat ..> NodeFeatures : reports
    FeatureGridPanel ..> SetRunSettingsIntent : toggles
    FeatureGridPanel ..> ClusterView : reads availability + state
```

*What the picture shows that prose hid:* the ONLY reader is `NodeFeatures.On(id)` — and most systems never call even that: the SCHEDULER
asks it for every system marked `[NodeFeature]`, beside the `[TickMode]` gate it already applies, so a feature's code cannot forget the
check. The AtLoad set arrives FROZEN in the load payload and is what the recording keeps.

```mermaid
sequenceDiagram
    participant O as Operator (FeatureGridPanel)
    participant M as Master (run settings)
    participant N as Node "Player-2" (NodeFeatures)
    participant S as a system (e.g. the kill cam)
    Note over N: boot — FeatureRegistry from the composed capabilities · defaults from the composition (player mode) · the node's feature flags
    N->>M: NodeCapabilitiesEvent (once): hrot.feature.hud.kill-cam, …
    O->>M: SetRunSettings{ FeatureOverrides: hud.kill-cam, [Player-2], off }
    M->>M: Player-2 advertises hrot.feature.hud.kill-cam? yes ⇒ accepted (no ⇒ refused, "not available")
    M-->>N: ClusterStateTransitioned{ FeatureOverrides } (broadcast, read via ClusterView)
    N->>N: effective(hud.kill-cam) = cluster off ▸ node ▸ default ⇒ OFF · Live ⇒ now, AtLoad ⇒ at the next load
    S->>N: On("hud.kill-cam")? ⇒ false — nothing drawn, nothing recorded
    N-->>O: heartbeat FeaturesOn (the grid shows OFF)
```

```mermaid
graph TD
    subgraph node["every ECS node — the shared bootstrap"]
        CAP["composed capabilities<br/>(declare features)"] --> REG["FeatureRegistry (boot)"]
        REG --> NF["NodeFeatures<br/>(recomputed when ClusterView changes)"]
        CLI["--feature / node config"] --> NF
        CV["ClusterView<br/>(folds the master's broadcast each frame)"] --> NF
        NF --> SYS["any system: On(id)"]
        NF --> HB["heartbeat: FeaturesOn"]
        REG --> NC["NodeCapabilities: hrot.feature.* (once)"]
    end
    subgraph master["the master (OrchestratorCore)"]
        RS["run settings: FeatureOverrides<br/>(validated against the advertised tokens)"]
    end
    RS -- "broadcast" --> CV
    style NF fill:#efe
```

*Caption:* `NodeFeatures` is built by the SHARED bootstrap on every ECS node (the `ClusterView` it reads already exists there), so no
host can lack it; ⚠ the edge that could go silently dead is a feature whose owner forgets to DECLARE it — `On(id)` of an undeclared id is a
**thrown** error at registration in every build (F-18), and a rail lists every `On("…")` literal against the declarations.

```mermaid
sequenceDiagram
    participant M as Master (run settings)
    participant N as Brain node (NodeFeatures)
    participant K as SystemScheduler (Brain node)
    participant R as recorder (Brain node)
    Note over M: FeatureOverrides holds (scenario.rules, all, on) — default anyway
    M->>N: LoadingLive PREPARE · payload.FeaturesAtLoad = the effective AtLoad ON set for THIS node
    N->>N: freeze the AtLoad set for this load (a Live override never changes it mid-run)
    N->>R: RecordingMetadata.FeaturesAtLoad = the frozen set
    loop every frame
        K->>N: On("scenario.rules")? (for each [NodeFeature] system, cached type → id)
        N-->>K: true ⇒ the system runs · false ⇒ skipped, like a paused WhenTimeAdvances system
    end
```

*What the picture shows that prose hid:* an AtLoad feature is decided ONCE, by the master, per node, in the payload the load already carries —
the same path `MapInLoad` takes today — so a run cannot change outcome-affecting features half-way, and the recording knows exactly which were on.

## 4. Decisions

| # | decision | ⭐ lean |
|---|---|---|
| **F-1** | what a feature is | a named behaviour (`area.name`, e.g. `hud.kill-cam`, `map3d.in-load`, `gameplay.deploy-server`) that an operator would want on or off INDEPENDENTLY. Two switches that always move together are one feature |
| **F-2** | who declares it | exactly ONE composition capability — the one whose code implements it (`INodeCapability.Features`, default none) |
| **F-3** | availability | declared ⇔ its owner is composed ⇒ the node advertises `hrot.feature.<id>` on `NodeCapabilities` (static, once at join) |
| **F-4** | the only reader | `NodeFeatures.On(id)` — no system reads a run setting, a flag or a host kind for a feature |
| **F-5** | precedence | **cluster override ▸ node override ▸ composition default** |
| **F-6** | the composition default | declared by the feature (`DefaultOn(composition)`): e.g. `hud.kill-cam` ON for `-m player`, OFF elsewhere; `map3d.in-load` ON for the Player (today's R-1046) |
| **F-7** | the node override | `--feature <id>=on|off` (repeatable) and the node config file |
| **F-8** | the cluster override | the run settings carry `FeatureOverrides: [(feature, nodes by name or all, on/off)]`; the master REFUSES an override for a node that does not advertise the feature. `MapInLoadNodes` becomes `(map3d.in-load, nodes, on)` |
| **F-9** | when a switch takes effect | each feature declares **Live** (now — a HUD element) or **AtLoad** (at the next load — anything that changes simulation outcomes, e.g. `gameplay.team-kill`) |
| **F-10** | replays | the effective switches of AtLoad features are part of the run's recorded settings, so a replay reproduces the run (S1 measures where run settings are recorded) |
| **F-11** | what the operator sees | each node reports its effective ON set on its heartbeat; a `FeatureGridPanel` shows nodes × features (not available / on / off) and toggles through the run settings |
| **F-12** | composition is untouched | a switch NEVER loads code; a feature that needs its own code or wire is still a pack (R-4102), and the switch decides whether it is active |

| **F-13** | ⭐ the GATE *(lean, `2026-11-01`)* | `[NodeFeature("id")]` on a system (or on its module — inherited by its systems), read ONCE per type by `SystemScheduler` beside `[TickMode]`; each frame the scheduler asks `IFeatureGate.On(id)` (an interface in `Fdp.ModuleHost`, implemented by `NodeFeatures`) and SKIPS the system when off ⇒ a pack is idle while its features are off **by construction** (R-4115). `NodeFeatures.On(id)` stays for finer checks INSIDE a system (one HUD element of several) |
| **F-14** | ⭐ how an AtLoad switch arrives *(lean)* | the master stamps each node's effective **AtLoad** ON set into its load PREPARE payload (`EditLoadHandlerPayload.FeaturesAtLoad`, replacing `MapInLoad`) — exactly `FanOutLoadPrepare`'s path today; the node FREEZES it for the load. A **Live** feature reads `ClusterView` each time it changes. ⇒ an override of an AtLoad feature during a run applies at the NEXT load, and the grid says so |
| **F-15** | ⭐ the recording *(lean)* | `RecordingMetadata.FeaturesAtLoad` (per node — each node records its own) = the frozen set; a REPLAY needs nothing (the outcome is already recorded); a BRANCH TO LIVE from a checkpoint re-applies it, so the continued run has the same rules. ⭐ The same metadata seat carries SR-101's run variant + params (`DESIGN_Scenario_Rules.md`) — one place for "how this run was set up". ⛔ not `CustomTags` (a typed field, read by code) |
| **F-16** | ⭐ the wire shapes *(lean)* | `SetRunSettingsIntent.FeatureOverrides` + the cluster facts: a typed managed list `{Feature, Nodes (names, empty = all), On}` — ⛔ not another comma string; `NodeHeartbeat.FeaturesOn` (the effective ON set). `MapInLoadNodes` becomes the override `(map3d.in-load, nodes, on)` and is deleted (no compatibility, CE-4153 S10) |
| **F-17** | ⭐ the first two features *(lean)* | **`map3d.in-load`** (migrated, declared by the Player's capability, AtLoad, default ON for `-m player` — R-1046 unchanged) and **`scenario.rules`** (declared by the Brain capability, AtLoad, default ON where composed — R-4115; it gates the scenario-rules systems, `DESIGN_Scenario_Rules.md` §00.6 S1). ⭐ SR-6 follows: the master refuses to START a scenario that carries rule entities when no node has `scenario.rules` on — *"no node runs scenario rules"* |
| **F-18** | an undeclared id *(lean)* | `On("x")` / `[NodeFeature("x")]` with no declaration THROWS at registration in every build (registration is boot, never mid-run — so throwing costs nothing in production); a rail lists every literal against the declarations. ⇒ supersedes the caption's "false in production" |

**Rejected — one line each:**
- *Each system calling `NodeFeatures.On` at its top* — N copies of one check, and the one that forgets is the silent defect; the scheduler already gates by attribute.
- *Live-reading AtLoad features from `ClusterView`* — a run could change outcome-affecting rules half-way; the load payload freezes them.
- *Run settings in `CustomTags`* — an untyped map nobody can rely on.
- *One bespoke setting per feature, the MapInLoad way, repeated* — N copies of one plumbing; MapInLoad becomes the first member instead.
- *Compile-time switches, or packs only* — no run-time mixture.
- *One "game mode" flag* — too coarse for "a mixture that suits the needs of the training or the gameplay".
- *The on/off state on the capabilities topic* — it is published once at join.
- *Features as a second kind of capability* — a capability is code, a feature is a behaviour inside it; merging them loses "available but off".

## 5. Slices

| slice | builds | done when |
|---|---|---|
| **S1** ⭐ READY-TO-BUILD | `FeatureDeclaration` on `INodeCapability`, `FeatureRegistry`, `NodeFeatures` (`IFeatureGate`), `[NodeFeature]` + the `SystemScheduler` gate (F-13), `--feature`, `FeatureOverrides` on the run settings + the cluster facts + `ClusterView` (F-16), `FeaturesAtLoad` in the load payload (F-14) and in `RecordingMetadata` (F-15), the availability tokens, `FeaturesOn` on the heartbeat; **`map3d.in-load` migrated** (`MapInLoad` / `MapInLoadNodes` deleted) and **`scenario.rules` declared** (F-17) | a Player builds its 3-D map in the load exactly as today (the R-1046 / CE-3354 ③ rails unchanged, re-pointed); an override switches it on a named node at the next load; an override for a node without the feature is refused; a `[NodeFeature]` system is skipped while off (a rail beside the TickMode rails); a branch to live re-applies the recorded set; rails: every literal is declared, every `[NodeFeature]` id is declared |
| **S2** | the HUD features: `hud.hit-marker`, `hud.damage-arc`, `hud.kill-cam`, `hud.squad-takeover-after-death`, `hud.spread-ring`, `hud.near-miss` (CE-4148; Player Join §7a G1–G6) + the `FeatureGridPanel` | with each OFF nothing of it is drawn or recorded on any host; defaults: ON for `-m player`, OFF elsewhere (G5/G6 lean ON everywhere) |
| **S3** | `gameplay.deploy-server` (Player Join D-3): "a server exists" = a node with the feature available AND on | a Player observes when no node has it on (R-4103) |

## 6. S1 technical spec *(`2026-11-01`, answers the user's questions: *"What is the list of features? What do the network topic structs look like … the reason for comma separated list in one place and a list of strings in the other … What is the scenario.rules feature about? How does the node keep / manage the list … where, what module? How fast is the NodeFeatures.On(id) check? How do we remember the features in the recording … when we load the recording and go to live, how do we know what features to enable?"*)*

### 6.1 The feature catalogue *(every id the designs name — grep of `docs/` for dotted ids, `2026-11-01`; S1 builds the first two, the rest arrive with their owners)*

| id | declared by (capability) | change | default | owning design / slice |
|---|---|---|---|---|
| `map3d.in-load` | Player (`cap:image-generator`) | AtLoad | ON for `-m player` | R-1046 · **S1** (migrated) |
| `scenario.rules` | Brain (`cap:brain`) | AtLoad | ON where composed | `DESIGN_Scenario_Rules.md` §00.6 S1 · **S1** |
| `scenario.rules.pause-on-end` | Brain | Live | ON | Scenario Rules SR-9 / SR-116 · its S2 |
| `hud.objectives` · `hud.own-evaluation` · `hud.messages` | Player | Live | ON (demo profile, SR-83) | Scenario Rules SR-106 / SR-112 · its S2 / S4 |
| `hud.hit-marker` · `hud.damage-arc` · `hud.kill-cam` · `hud.squad-takeover-after-death` · `hud.spread-ring` · `hud.near-miss` | Player | Live | ON for `-m player` | `DESIGN_Player_Join.md` §7a G1–G6 · S2 |
| `hud.kill-feed` · `hud.score-popups` · `hud.coaching` | Player | Live | game profile ON, training OFF | Scenario Rules SR-112 · its S6 |
| `gameplay.deploy-server` | the deploy pack | AtLoad | ON where composed | Player Join D-3 · S3 |
| `gameplay.team-kill` | Combat (`cap:muscle-ground`) | AtLoad | ON | PLAN_Training_And_Gameplay_Engine |
| `ui.side-knowledge` · `ui.side-knowledge.last-known` · `map3d.knowledge-indicators` | Player | Live | per profile | Entity Control / Map 3D designs |
| `eval.performance` | Brain | AtLoad | ON | Scenario Rules S4 |

⭐ The catalogue is CODE, not a file: each capability's `Features` list is the truth; a rail prints the table, so this one can be regenerated.

### 6.2 The C# shapes

```csharp
public enum FeatureChange : byte { Live, AtLoad }
public sealed record FeatureDeclaration(string Id, string Description, FeatureChange Change, Func<NodeRole, bool> DefaultOn);

public interface INodeCapability {                      // existing — grows ONE default member
    IReadOnlyList<FeatureDeclaration> Features => Array.Empty<FeatureDeclaration>();
    // Key, Needs, PopulateSystems, ProvideModules, Register — unchanged
}

public readonly struct FeatureHandle { internal readonly int Index; }   // a dense index, resolved ONCE

public interface IFeatureGate { bool On(FeatureHandle f); FeatureHandle Resolve(string id); }   // Fdp.ModuleHost — the scheduler's view

public sealed class NodeFeatures : IFeatureGate {       // Hrot.Common/Infrastructure — ONE per node
    public bool On(FeatureHandle f) => _on[f.Index];    // the hot path: one array read
    public bool On(string id) => _on[_index[id]];        // convenience: one dictionary lookup
    public FeatureHandle Resolve(string id);             // throws on an undeclared id (F-18)
    public event Action? Changed;                        // after a recompute
    private bool[] _on;                                  // effective, recomputed on ClusterView change / at load
    private readonly Dictionary<string, int> _index;     // id → index, built at boot from the FeatureRegistry
    private bool[] _frozenAtLoad;                        // the AtLoad part, set from the load payload or the recording
}

[AttributeUsage(AttributeTargets.Class)] public sealed class NodeFeatureAttribute(string id) : Attribute { public string Id => id; }
```

### 6.3 Where it lives, who builds it

```mermaid
graph TD
    subgraph boot["every host — SharedApplicationBootstrapper (Hrot.Common/Infrastructure) — Editor, CGF/Brain, SimHost, IG/Player, Traffic, Stride"]
        WALK["NodeCapabilityWalk (existing, :207)"] --> REG["FeatureRegistry — the declarations of the composed capabilities, dense indices"]
        REG --> NF["NodeFeatures — placed on HrotNodeContext"]
        REG --> TOK["NodeCapabilitiesTopic.CapabilitiesJson += hrot.feature.<id>"]
        CLI["--feature id=on|off · node config"] --> NF
    end
    CV["ClusterView (existing, folds ClusterStateTopic)"] -- "Changed ⇒ recompute _on" --> NF
    LOAD["load PREPARE payload FeaturesAtLoad"] -- "freeze" --> NF
    REC["RecordingMetadata.FeaturesAtLoad (on a branch)"] -- "freeze" --> NF
    NF --> GATE["IFeatureGate → SystemScheduler (Fdp.ModuleHost)"]
    NF --> HB["NodeHeartbeat.FeaturesOn"]
    NF --> SYS["a system's own FeatureHandle checks"]
```

*Caption:* the registry is built where the capability walk already is, in the ONE base bootstrapper every host derives from (`TrafficNodeBootstrapper`,
`IgNodeBootstrapper`, `StrideNodeBootstrapper`, `CgfNodeBootstrapper`, `SimHostNodeBootstrapper`, `EditorNodeBootstrapper` — all
`: SharedApplicationBootstrapper`), so no host can lack it; the scheduler sees only the FDP-level `IFeatureGate`.

### 6.4 Speed

| call | cost | used by |
|---|---|---|
| `On(FeatureHandle)` | one `bool[]` read (~1 ns) | the scheduler (the handle cached per system TYPE at registration, beside `TickMode`) and systems (handle resolved in the constructor) |
| `On(string)` | one `Dictionary<string,int>` lookup (~20 ns) | UI and rare checks — ⛔ not in a per-entity loop |
| recompute `_on` | O(features), only when `ClusterView` changes or a load freezes | — |

### 6.5 The wire *(measured shapes: `OrchestrationMessages.cs:150-200` `ClusterStateTopic`, `:426-445` `NodeHeartbeat`, `:463-472` `NodeCapabilitiesTopic`; `Payloads/OrchestrationPayloadDtos.cs:98-108` `SetRunSettingsPayloadDto`)*

```csharp
public partial struct FeatureOverrideWire {                // NEW, a DDS struct
    [DdsManaged] public string Feature;
    [DdsManaged] public List<string> Nodes;                // empty = all nodes
    public bool On;
}
// ClusterStateTopic — APPENDED LAST (R-42); MapInLoadNodes DELETED (no compatibility):
    [DdsManaged] public List<FeatureOverrideWire> FeatureOverrides;
// NodeHeartbeat — appended:
    [DdsManaged] public List<string> FeaturesOn;          // the effective ON set
// NodeCapabilitiesTopic — UNCHANGED: CapabilitiesJson simply gains "hrot.feature.<id>" tokens
// SetRunSettingsPayloadDto (JSON, the cluster-op channel) — appended; MapInLoadNodes deleted:
    [property: JsonPropertyName("FeatureOverrides")] FeatureOverrideDto[]? FeatureOverrides = null   // null = no change
// EditLoadHandlerPayload (JSON, the load PREPARE) — MapInLoad DELETED:
    string[]? FeaturesAtLoad                               // this node's effective AtLoad ON set
```

⭐ **Why a comma string in one place and a list in another — measured, no deep reason:** the orchestration topics carry lists in THREE
conventions today — a comma string (`ClusterStateTopic.MapInLoadNodes`, CE-3354 ③: *"comma-joined (names hold no comma)"*), a JSON
`string[]` inside a string (`NodeCapabilitiesTopic.CapabilitiesJson`, `SubsystemsJson`, the asset inventory), and a typed DDS sequence
(`EntityMission.Plan.Tasks`, `[DdsManaged] List<MissionTask>`, `MissionDescriptors.cs:62-64`). The JSON payload DTOs (`SetRunSettingsPayloadDto`)
use `string[]` because they are JSON. ⭐ Lean: NEW topic fields use the typed sequence (it already crosses the wire for missions);
`MapInLoadNodes` goes with this slice. ⚠ Unifying the existing JSON-in-a-string lists is a separate, cheap finding — filed with this slice's row.

### 6.6 The recording, and going live from it

| step | what happens | where |
|---|---|---|
| a live load | the master stamps each node's effective AtLoad ON set into its load PREPARE (`FeaturesAtLoad`) — the `FanOutLoadPrepare` path `MapInLoad` takes today | `ClusterMaster.cs:2392-2400` |
| the node | freezes it (`_frozenAtLoad`) and writes it into its own `RecordingMetadata.FeaturesAtLoad` | `RecordingModule` (`Fdp.Toolkits/Replay/RecordingModule.cs`), where `ParentExerciseId` is written today (`:89`) |
| replay | nothing needed — the outcome of those features is already in the recorded state | — |
| ⭐ go live from the recording (`NodeOpType.PrepareLive`, `LiveBranchAggregator`) | each node's `StartBranch` already copies its parent's metadata (terrain, geo origin, checkpoints, parent id — `RecordingModule.cs:57-73`); it copies `FeaturesAtLoad` too, and the node FREEZES that set — ⭐ **the recording wins** for AtLoad features (the continued run plays by the same rules); Live features follow the cluster's current settings | `RecordingModule.StartBranch` |
| a node that kept no recording | takes the master's current overrides, as on a fresh load | — |
| a recorded feature this build does not declare | the branch is REFUSED loudly (*"recording needs feature X, not available on node Y"*) — ⛔ never a silent different run | the PrepareLive answer |
