<!--STATUS
state: LIVE
build-state: PARTIAL
verified: 2026-08-28 (coordinator source scan)
current-answer: PARTIAL. Built on Editor+CGF (CgfEditorShellToolbar.RegisterCommonCore, ShellTimeControlToolbar, CE-016/037-045/069). MISSING: only 2 of 7 hosts (SimHost/ExCon/IG/Orchestrator/ReplayBrowser absent); the authority-derivation half (declared-written-components -> HasAuthority) not built.
-->
# Feature design — shell parity: every subsystem gets a menu and a toolbar

> **Design for [UXI-35](UX_Issues.md#uxi-35) (+ [UXI-36](UX_Issues.md#uxi-36)) · drafted 2026-08-14.**
> Direction from [ruling 58](UX_RESUME_INTERACTION.md). **Status: 🟡 PARTIAL — built on Editor+CGF (`CgfEditorShellToolbar.RegisterCommonCore`, `ShellTimeControlToolbar`, CE-016/037-045/069); only 2 of 7 hosts covered and the authority-derivation half is not built.**

## 0. Prior art — the mechanism is built; the registration is not

🔒 Index-first pass, then grep to confirm call sites ([rule 6e](UX_RESUME_INTERACTION.md)).

| Exists? | What | Adoption |
|:--:|---|---|
| ⭐ | **`ClusterSlave` is constructed by EVERY host** — IG (`IgNodeBootstrapper.cs:205`) · Orchestrator (`:115`) · CGF (`CgfSubsystem.cs:409`) · SimHost (`NodeBootstrapper.cs:194`) · Editor (`EditorSubsystem.cs:787`) · ExCon (`ExConSubsystem.cs:184`) · generic `HrotNodeBuilder.cs:174` | **the control plane is already universal** |
| ⭐ | **`TimeControllerFactory`** → `MasterSyncController` / `SlaveSyncController`. Orchestrator is master (`:146`); Editor also builds an **offline** `ClusterMaster` (`:1352`); everyone else slave | shared factory, 5 construction points |
| ⭐ | **`ClusterTimeTransportAdapter` + `ClusterTimeControlStatusBarSection`** | 🔴 **the same ~8-line block, copy-pasted twice** — CGF `:737` (perspective `"CGF"`) and SimHost `:265` (perspective `"SimHost"`). **Nobody else registers it** |
| ⚠ | `MainToolbarTimeControlSection` (shared, `Hrot.Presentation/Panels`) | 🔴 **Editor only** — so the *same function* lives on the **toolbar** in one host and the **status bar** in two others |
| ⭐ | **Scenario load is already cluster-wide** — `ClusterMaster` fans out `PrepareLive`/`FinalizeLive`; per-host handlers exist: `CgfScenarioLoadHandler` · `HrotScenarioLoadHandler` (SimHost) · `ReferenceLiveLoadHandler` (IG) · `ClusterScenarioPanel` (Orchestrator) | 🔴 **no host but the Editor has a way to *name* a scenario** (`AssetPickerModal` + `AssetPickActionRouter`) |
| ✅ | `MainToolbarManager` · `GlobalMenuRegistry` · `MenuCommandAdapter` · `ToolbarCommandAdapter` · `EditorCommandDescriptor` | 🔴 **Editor is the only writer of all four** ([UXI-35](UX_Issues.md#uxi-35)) |

**Registration census — every production surface, verified:**

| Host | Menu registry | Toolbar | Status bar | Own `BeginMainMenuBar` |
|---|:--:|:--:|:--:|:--:|
| **Editor** | ✅ 5 adapters + `ScenarioMenuCommands` | ✅ 12 entries | ✅ 2 | ⚠ 1 |
| **CGF** | ❌ | ❌ | ✅ 1 (time) | ❌ |
| **SimHost** | ❌ | ❌ | ✅ 1 (time) | ⚠ 1 |
| **ExCon** | ❌ | ❌ | ❌ | ⚠ 1 |
| **IG** | ❌ | ❌ | ❌ | ⚠ 1 |
| **ReplayBrowser** | ❌ | ❌ | ❌ | ⚠ 1 |
| **Orchestrator** | ❌ | ❌ | ❌ | ❌ |
| ClusterRunner | ❌ | ❌ | ✅ 2 | ❌ |

⇒ ⭐ **Seam-law instance 30 — the widest yet.** Four shared registries, one writer each. **Nothing in this design is a new mechanism.**

## 1. 🔒 The host tiers ([ruling 58](UX_RESUME_INTERACTION.md))

| Tier | Hosts | Shell |
|---|---|---|
| **Rich authoring** | **Editor** | everything — the reference implementation |
| **Rich, distributed** | **CGF** | *"almost like the Editor, just in network distributed mode"* ⇒ the same surfaces, resolved over the cluster instead of in-process |
| **Runtime participants** | **SimHost · ExCon · IG** | the common core (§3), **narrowed by ECS component ownership** |
| **Infrastructure** | **Orchestrator** | 🔒 **no requirements of its own** ([ruling 59](UX_RESUME_INTERACTION.md)) — it gets whatever the shared core gives it, and nothing is written for it. ⭐ **The falsifiability test for §2**: if it needs bespoke menu code, the derivation is wrong |
| **Separate beast** | **ReplayBrowser** | *"completely separate, its own specific stuff"* — its own windows exist already (`ReplaySearchWindow`, `ReplayTimelineWindow`, `ComponentDiffWindow`, `FdpEventBrowserWindow`, `FdpEntityInspectorWindow`). 🔒 **Common core does NOT apply**; it gets a menu/toolbar of its own vocabulary |

## 2. ⭐ The item set is **derived**, not authored per host

🔒 **The key move — and it needs no new machinery, only two things already ruled:**

| Input | From |
|---|---|
| *"can this host service this action at all?"* | [UXI-29](UX_Feature_Authority_Aware_Writes.md)'s **`HasAuthority<T>` gates** — a host may write a component only where it holds authority |
| *"then what does the operator see?"* | [Ruling 49](UX_RESUME_INTERACTION.md) — a blocker that **can never clear in this host is a fact about the host**, so the item is **absent**, not greyed |

> ⇒ 🔒 **One registration list for the whole product. Each host renders the subset whose written components it owns.**
> No per-host menu file, no `if (host == …)`, and adding a host adds **no** menu code.

| | |
|---|---|
| ⭐ **This is exactly [ruling 47/49](UX_RESUME_INTERACTION.md) applied one level up** | they decided *per selection*; this applies the same test *per host* |
| ⭐ **And it makes the ownership rule visible** | *"limited by their ECS component ownership"* stops being documentation and becomes **the thing that computes the menu** |
| ⚠ **It needs the authority map to be declarative** | an action must **declare** the components it writes. [UXI-03](UX_Feature_Entity_Action_Vocabulary.md)'s descriptor does not carry that today — **the one genuinely new field in this design** |

## 3. 🔒 The common core — every host except ReplayBrowser

| Capability | Mechanism (exists) | Missing |
|---|---|---|
| **Open an existing scenario** | per-host load handlers + `ClusterMaster` fan-out | 🔴 **a picker + a command** in each host. ⭐ `AssetPickerModal` + `AssetPickActionRouter` are the Editor's, and are **kind-generic** already. 🔒 **On every host but the Editor this is a CLUSTER-WIDE request to the master** ([ruling 59](UX_RESUME_INTERACTION.md)) — never a local load |
| **Sim time control** | `ClusterSlave` (all hosts) + `TimeControllerFactory` + `ClusterTimeTransportAdapter` | 🔴 registration in 4 hosts, and 🔒 **one surface, not two** (§4) |
| **Interactive runtime changes** | [UXI-32](UX_Feature_Entity_Commanding.md) commanding · [UXI-29](UX_Feature_Authority_Aware_Writes.md) authority-gated writes | the §2 derivation |
| **Window / perspective / help** | `WindowManager` shell menus | already global — no work |

🔒 **Time control gets ONE surface across all hosts.** Today it is the toolbar in the Editor and the status
bar in CGF/SimHost. ⚠ **Pick the toolbar** — it is the transport control, it is the shape
`MainToolbarTimeControlSection` already has, and [ruling 13](UX_RESUME_INTERACTION.md) reserves the status
bar for *activity progress*. The two CGF/SimHost status-bar registrations are then **replaced**, not added to.

## 4. Per-host matrix

| | Editor | CGF | SimHost | ExCon | IG | Orchestrator | ReplayBrowser |
|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Open scenario | ✅ **local (only single-node host)** | ✅ cluster | ✅ cluster | ✅ cluster | ✅ cluster | ✅ (free) | ⊘ own (open **recording**) |
| Time control | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ **master** (free) | ⊘ own (replay transport) |
| Entity commanding ([UXI-32](UX_Feature_Entity_Commanding.md)) | ✅ | ✅ | ⚠ by authority | ⚠ by authority | ⚠ by authority | ❌ | ⊘ |
| Authoring / asset edit | ✅ | ⚠ **bonus, not required** | ❌ | ❌ | ❌ | ❌ | ⊘ |
| Brain runtime diagnostics | ✅ | 🔴 **required — [UXI-37](UX_Issues.md#uxi-37)** | ❌ | ❌ | ❌ | ❌ | ⊘ |
| Tools ([UXI-07](UX_Feature_Tool_Model.md)) | ✅ | ✅ | ⚠ | ⚠ | ⚠ | ❌ | ⊘ |
| Cluster state / node health | — | — | — | — | — | ✅ | ⊘ |

✅ full · ⚠ derived from authority (§2) · ❌ absent (ruling 49) · ⊘ out of tier

## 5. What actually gets built

| # | Work | Kind |
|--:|---|---|
| 1 | An `ISubsystemShell` composition helper: registers the **common-core** commands into `GlobalMenu` + `MainToolbar` for a host, given its authority set | new, small |
| 2 | Move the time section to the toolbar in **all** hosts; delete the two copy-pasted status-bar blocks | de-duplication |
| 3 | Register the scenario picker per host, routing to the existing load handler (local for Editor, master fan-out elsewhere) | registration |
| 4 | `EntityActionDescriptor` declares **written components** ⇒ authority derivation (§2) | 🔴 **the one new field** |
| 5 | Accelerators — [UXI-36](UX_Issues.md#uxi-36); `EditorCommandDescriptor.DefaultKey` is the model | registration |
| 6 | ReplayBrowser: its own menu/toolbar from its existing windows | registration |

⚠ **Sequencing:** #4 depends on [UXI-03](UX_Feature_Entity_Action_Vocabulary.md) landing, and #1 depends on
[UXI-05](UX_Feature_Menu_Follows_Focus.md) (a shared registry is useless while four hosts draw their own bar).

## 6. ✅ Questions — all answered ([ruling 59](UX_RESUME_INTERACTION.md), 2026-08-14)

| | Answer |
|--:|---|
| **Q1 · Orchestrator** | 🔒 **Infrastructure, mostly invisible — no menu/toolbar requirements of its own.** *"It probably does not need either, or just a minimalistic one as a result of using the same shared infrastructure."* ⇒ **do not design for it**: it receives whatever the common core gives it for free, and nothing is added on its behalf. ⭐ **This is a stronger outcome than a tier** — it makes the shared-infrastructure claim falsifiable: if the Orchestrator needs bespoke menu code, the derivation in §2 is wrong |
| **Q2 · open = cluster-wide** | 🔒 **Yes — cluster-wide, from any node, for convenience.** *"To make the simulation startable and basically controllable (sim time, replay) from any node."* 🔒 **The Editor is the ONLY single-node host.** ⇒ every other host's *Open scenario* / transport command is a **request to the master**, never a local load — which is exactly what `ClusterMaster` fan-out already does. ⚠ **So "open" on a runtime host must be worded as what it is** — it restarts the exercise **cluster-wide** — and it is [ruling 53](UX_RESUME_INTERACTION.md)-confirmable **at the origin**, with the spec computed by the node that knows ([ruling 55/56](UX_RESUME_INTERACTION.md)) |
| **Q3 · CGF** | ⚠ **Split into two, with different priorities.** ① **Asset authoring on CGF = a *nice bonus* of shared infrastructure, NOT required.** ② 🔴 **Required instead: full runtime diagnostics of the brain stack** — load and show BTree/HSM/blueprint **asset graphs**, monitor **runtime activity**, runtime inspectors and **watch windows**, to debug a suspected misbehaviour **at exercise runtime**. ③ **Bonus:** breakpoints (*"could be destructive — breaking/stopping the running simulation"*) and possibly stepping. ⇒ ② and ③ are **not shell parity at all** — filed as **[UXI-37](UX_Issues.md#uxi-37)** |

## 7. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 35.1 | Every host except ReplayBrowser shows a **time control on the toolbar**, driven by its own `ClusterSlave` | I |
| 35.2 | 🔒 The time control appears **once** per host — no host shows both a toolbar and a status-bar transport | H |
| 35.3 | A host **without authority** over an action's written components **does not show the item at all** (ruling 49) | H |
| 35.4 | 🔒 The same registration list produces **different menus** in two hosts, with **no host-specific menu code** | H |
| 35.5 | Adding a new host registers **zero** menu items and still gets the common core | H |
| 35.6 | Opening a scenario from a runtime host reaches that host's **existing** load handler | I |
| 35.7 | ReplayBrowser's menu contains **none** of the common core and all of its own | H |
| 35.8 | 🔒 Registering a common-core command in a **headless** host raises no ImGui call and does not throw ([ruling 53](UX_RESUME_INTERACTION.md)) | H |
