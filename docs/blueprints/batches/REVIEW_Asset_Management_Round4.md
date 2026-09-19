<!--STATUS
state: LIVE
doc-type: REVIEW (round 4) of the ASSET MANAGEMENT programme, by the backend lane (`H - back`) on branch
  `backend`. ⚠ EPHEMERAL — findings only. ⛔ No design content here: every finding names the section of
  the OWNING design it lands on, and the fix belongs in that design, not in this file.
updated: 2026-09-19
build-state: n/a — a review, not a design.
current-answer: §1 what the programme is · §2 what measured SOUND · §3 the findings, severity-ordered
  (F1–F11) · §4 what I did NOT cover.
stale-below: nothing — new document.
known-rot: nothing. ⚠ Every §3 row carries its own file:line; re-measure before acting (§M discipline).
known-conflict: none. ⚠ Rounds 1–3 are already folded into DESIGN_Asset_Management.md `## ⛔ HISTORY`;
  this is round 4 and none of it is in that table yet.
related-designs:
  - docs/DESIGN_Asset_Management.md — THE owning design. F1/F2/F3/F4/F6 land on §7.3a/§7.3b/§7.6/§2.
  - docs/blueprints/PLAN_Asset_Management_Build.md — F5/F8/F9 land on its task list and §3 register.
  - docs/blueprints/Architect_Question_72_Asset_Lifecycle_And_Distribution.md — the decision record;
    F4 and F11 land on Q72-M's amendment ①.
  - docs/blueprints/RESUME_Assets_And_Occurrences.md — the coordinator resumption that dispatched this.
  - docs/DESIGN_Artifact_Staging.md — BUILT. Its `(length, mtime)` skip is the predicate F6 is about.
  - docs/DESIGN_Cluster_Load_Phase.md — owns RoleLoadRequirements; F10 is about its 300 s parked bound.
-->

# REVIEW — **asset management, round 4** *(backend lane)*

> ⭐ **Tooling note, stated because `CLAUDE.md` requires it.** The codebase-memory MCP tools were **not
> connected** this session (`ENOENT` on `${CODEBASE_MEMORY_MCP_BIN}`). I ran `scripts/cloud-bootstrap.sh`,
> indexed the repo fresh (`home-user-HROT`, branch `backend`) and drove the graph **through the CLI**.
> ⭐ **Every enumeration below is graph + grep**, and where they agree I say so. ⛔ The three previous
> rounds were **grep-only** (`DESIGN_Asset_Management` §1 says so itself: *"grep-corroborated, not
> graph-enumerated"*) — **F3 is a direct consequence of that**, and the graph is what found it.

---

## 1. WHAT THE PROGRAMME IS — in one table

| | |
|---|---|
| **the invariant** | 🔒 *"loadable assets live on NAS as the master source, nodes keep just copies they really need, automatic sync when out of date, scenario always loads with consistent data everywhere needed. Error during sync should fail the scenario loading."* |
| **the model** | **three forms** (NAS at-rest · transport · node at-rest), **two directions** (NAS→node sync, node→NAS publish), **one freshness predicate** — the BUILT `(length, mtime)` |
| **the key ruling** | ⭐ **NAS form == node form, always** (`Q72-H`) ⇒ transport shape is a pure wire optimisation, and the node tree **mirrors** NAS, subfolders and all (`Q72-K`) |
| **the transport** | a **partition** — big / pre-compressed travel standalone, the rest as one archive (`Q72-H1`) |
| **the filter** | `hrot.asset.needs.*` **derived** from `RoleLoadRequirements` via a 3-row `LoadPart → AssetKind` adapter, **plus** `Brain ⇒ every kind with a `BaseFolder`` (`Q72-M`) |
| **the safety rule** | `hrot.asset.authors.*` **subtracted** from the needs set, in three clauses, bounded away from `LoadPart`-derived kinds (§7.3b) |
| **the shape** | 3 increments / 15 tasks — **A** manifest+recursive walk · **B** needs-filtered sync · **C** publish+probe+refresh |

---

## 2. WHAT MEASURED SOUND — ⛔ do not re-litigate these

⭐ Stated first because three rounds of review have already paid for them, and a fourth round that only
lists faults invites re-opening settled things.

| claim | ✅ measured |
|---|---|
| `LoadPart` ∩ `AssetKind` = ∅ | `LoadPhaseContracts.cs:32-49` = {KnowledgeBase, Terrain, ScenarioEntities}; `AssetKind.cs:3-11` = 6 authoring kinds. **Disjoint.** `Q72-M`'s whole premise holds |
| the freshness predicate is exact, and is ONE predicate | `StorageGatewayModule.cs:519-526` — `src.Length == dst.Length && src.LastWriteTimeUtc == dst.LastWriteTimeUtc`. ⭐ Exact equality, as §2 assumes |
| `PrefetchScenarioAsync` is FLAT | `StorageGatewayModule.cs:272` `Directory.GetFiles(sourceDir)` — no `SearchOption` |
| **no recursive walk in the orchestrator** | `grep -rn AllDirectories Hrot/Subsystems/Hrot.Orchestrator/` ⇒ **zero hits.** ⭐ `A2` is genuinely new code *there* — ⚠ but see **F7** |
| `AssetRoots.AssetsRelative` throws for 3 of 6 kinds | `AssetRoots.cs:197-206` — resolves Blueprint/BTree/Hsm, throws for Blackboard/Utility/Scenario. ⭐ The **predicate, not a list** correction was right |
| `UniversalParts = { KnowledgeBase }`, not role-derived | `RoleLoadRequirements.cs:34` ⇒ the `Map2D` correction in §6 was right; *"Map2D gets nothing"* would indeed have thrown |
| SimHost carries **no** Brain | `SimHostApp.cs:182-183` = `MuscleGround\|Perception\|NavigationSolver`. ⭐ Round 3's *"a guard is not a declaration"* correction stands |
| the editor **is** Brain | `EditorCapabilities.cs:60-61` = `Brain\|MuscleGround\|Perception\|NavigationSolver` — ⚠ see **F11** on the citation |
| exactly ONE capability feature token exists | `CapabilityTokens.cs:14-20` — `fdp.reliable-init`, and nothing else |
| the 300 s parked bound is real | `ClusterMaster.cs:138` `ParkedTransitionExpirySeconds = 300.0` |
| **all five T-1 suites named in the PLAN exist** | `StorageGatewayTests`, `ClusterMasterPrefetchTests`, `TheHostsAgreeOnTheScenarioRootTests`, `EveryEcsHostComposesTheLoadPhaseChainRails`, `LoadPhaseChainTests` — ⭐ `R-142`'s row is accurate, not aspirational |

---

## 3. THE FINDINGS

⭐ **Severity key:** 🔴 blocks a batch as written · ⚠ must be stated before dispatch · ⭐ precision.

### 🔴 F1 — **the AI-asset destination is not addressable by the process the design puts in charge of writing it** → design §7.3b, §7.6

📐 **The design rejects the one path the orchestrator can compose.** §7.3b, *Rejected:* *"land synced AI
assets in the per-node staging root instead — the CGF reader is `AssetRoots`, so they would be invisible
there."* ⇒ the destination must be `AssetRoots.AssetsFor(kind)`.

📐 **And that path is resolved in the TARGET node's process, from that process's own state:**

| arm | `AssetRoots.cs` | can the orchestrator compute it? |
|---|---|---|
| ① `ConfiguredRoot` | `:110-111`, a **process-global static** (`:40`) | ⚠ **only if it knows that node's config** — see below |
| ② source walk-up | `:114-115` — walks up from the **target process's CWD + `AppContext.BaseDirectory`** | ⛔ **no** |
| ③ output directory | `:118` — `AppContext.BaseDirectory` of the target process | ⛔ **no** |

📐 **Arm ① has exactly one production setter:** `Hrot.ClusterRunner/Program.cs:246`
`AssetRoots.Configure(config.AssetRoot)`, from `HrotRunnerConfiguration.AssetRoot`
(`:90`) — ⛔ **whose default is `string.Empty`**, which falls through to arms ②/③.

⇒ ⭐⭐ **The honest statement: the destination is computable by the orchestrator ONLY under an unstated
deployment assumption** — *every syncable node sets `AssetRoot` explicitly, and the orchestrator is told
the same value.* ⛔ Under the default configuration it is **not computable at all, even on one machine.**

⚠⚠ **And the escape hatch is closed by scope.** §7.6 names the alternative — *"the node builds and returns
its own manifest"* — and rules it out as *"a new `NodeOpType`, which this design's scope explicitly
excludes"*; `PLAN` §4 repeats the ban. ⇒ 🔴 **`B4` has no legal way to name its own destination.**

⭐ **What §7.6 currently says is too weak for increment B.** It records the constraint as *"true on one
machine or a shared filesystem, false for a genuinely distributed deployment"* — ⛔ that is the
**staging-root** case. For AI kinds it is *"false unless `AssetRoot` is configured on every node **and**
known centrally."*

| ⭐ the three ways out — **a design call, not an implementer call** | |
|---|---|
| **(a)** make `AssetRoot` a **required** config on any node advertising `hrot.asset.needs.<AI kind>`, orchestrator-visible; rail the fallthrough as a **refusal**, not a silent wrong path | ⭐ smallest; keeps the `NodeOpType` ban |
| **(b)** the node reports its own resolved root in its capability/descriptor payload | ⚠ additive to an existing durable message — ⛔ but it is the honest fix and it is *not* a new `NodeOpType` |
| **(c)** accept the staging root and give the CGF reader a second root + merge rule | ⛔ §7.3b already rejected this, with reasons |

---

### 🔴 F2 — **`BaseFolder` is NOT the folder the contributor scans** → design §7.3a amendment ②, `C2`

⭐⭐ **This is a live defect in shipped code, and the design leans on the broken property.**

📐 There are **two different base resolutions in one class**, and they disagree:

| member | base chain | `AssetRoots.cs` |
|---|---|---|
| `ResolveAssetsRoot(kind, segments)` | `ConfiguredRoot` → **source walk-up** → output dir | `:93-94`, `:110-119` |
| `AssetsFor(kind)` | `ConfiguredRoot` → output dir — ⛔ **no walk-up** | `:260`, `:279-280` |

📐 **And the contributors are built from one and report the other:**

| | scans | reports as `BaseFolder` |
|---|---|---|
| `BlueprintAssetContributor` | `_rootDirectory`, **injected** (`:23-26`) — editor passes `ResolveAssetsRoot(...)` (`EditorSubsystem.cs:1217`) | `AssetRoots.AssetsFor(Kind)` (`:32`) |
| `BTreeJsonAssetContributor` | `rootDirectory` handed in on refresh — `_btreeJsonRootDir = ResolveAssetsRoot(...)` (`EditorSubsystem.cs:1218`, `:1250`) | `AssetsFor(Kind)` (`:56`) |
| `HsmJsonAssetContributor` | same shape (`EditorSubsystem.cs:1219`, `:1252`; `HsmJsonAssetContributor.cs:60-69`) | `AssetsFor(Kind)` (`:39`) |

⇒ ⛔⛔ **On any host with a source tree and no `ConfiguredRoot` — the dev-box shape — `BaseFolder` names
`<binDir>/Assets/<Kind>` while the contributor is enumerating `<sourceTree>/Assets/<Kind>`.**

⚠ **And it is silent:** `ReportBase` (`:169-176`) warns only when **neither** config nor a source tree
answered. The divergent case is exactly *"a source tree answered"* ⇒ **no warning.**

📌 **This is the `SILENT-DEFAULT PATTERN` shape from `CLAUDE.md`** — *"a production caller that HAS a
dependency must PASS it"*: `EditorSubsystem` computes the root and hands it over, and `BaseFolder`
ignores it.

| ⇒ consequences for this design | |
|---|---|
| **`C2`'s probe walks `BaseFolder`** | ⛔ it would stat the **wrong directory**, find it empty or stale, and warn BEHIND/AHEAD about nothing |
| **§7.3a's predicate** *("syncable iff the contributor has a non-null `BaseFolder`")* | ⚠ the property is real, but it **does not mean what the design reads it to mean** — it is not *the authoring root* |
| **`B4`'s destination** | 🔴 if it mirrors into `BaseFolder`, assets land where the editor is **not** reading |

⭐ **Fix belongs upstream of this programme:** `BaseFolder` must return the root the contributor was
constructed with. ⛔ Do not paper over it inside `C2`.

---

### 🔴 F3 — **the predicate is per-CONTRIBUTOR; the rule is per-KIND; and two kinds have two contributors that disagree** → design §7.3a, `B1`

⭐⭐ **This is the finding the graph produced and three grep-only rounds missed.**

```
search_graph(name_pattern=".*Contributor.*", label="Class")   →  24 nodes
grep -rn ":.*IAssetCatalogContributor"                        →  10 hits (4 test doubles)
```
⭐ **Both agree on the production set — SIX implementations over FOUR kinds:**

| contributor | Kind | `BaseFolder` |
|---|---|---|
| `BlueprintAssetContributor` | Blueprint | ✅ `AssetsFor(Blueprint)` |
| `BTreeAssetContributor` | BTree | ⛔ **null** *(no override — assembly-backed, `LoadFrom(asm)`)* |
| `BTreeJsonAssetContributor` | BTree | ✅ `AssetsFor(BTree)` |
| `HsmAssetContributor` | Hsm | ⛔ **null** *(no override)* |
| `HsmJsonAssetContributor` | Hsm | ✅ `AssetsFor(Hsm)` |
| `ScenarioCatalogContributor` | Scenario | ⛔ `null` (`:82`) — ✅ confirms the design's scenario claim |

📐 **`BaseFolder` is a DEFAULT INTERFACE MEMBER returning `null`** (`IAssetCatalogContributor.cs:20`) —
*"every existing implementor that does not override this property stays backward-compatible."*
📐 **And both members of each pair are registered together** — `EditorSubsystem.cs:1190-1191` (the
assembly pair) and `:1232-1233` (the JSON pair); CGF holds the same JSON pair (`CgfSubsystem.cs:323-324`).

⇒ ⛔⛔ **For `Hsm` and `BTree` the question *"does this kind's contributor have a `BaseFolder`?"* has TWO
answers at once.** The design says *"its catalog contributor"* — **singular** — and `B1`'s success
condition (*"a rootless kind yields no token rather than throwing"*) inherits the same assumption.

| ⭐ what the design must state, and does not | |
|---|---|
| ⭐⭐⭐ **the AGGREGATION rule** | *any* contributor with a non-null `BaseFolder` ⇒ syncable? *all*? the *first*? ⛔ **First-wins is registration-order dependent — `R-132`: two producers for one slot bound by registration order is a race, not a precedence rule** |
| ⚠ **and which FOLDER wins** when two non-null roots disagree | ⛔ today they do not, only because both call `AssetsFor(Kind)` — 📌 **which F2 shows is the wrong folder anyway** |
| ⚠ **the "zero maintenance" claim reads the default backwards** | §7.3a: *"a kind gaining a root becomes syncable with no edit here."* ⛔ The default is `null`, so a new **file-backed** contributor that forgets to override is silently **non**-syncable. ⭐ The property conflates *"not file-backed"* with *"did not override"* |

---

### 🔴 F4 — **clause ③'s safety claim does not hold in the omission direction** → design §7.3b, `Q72-M` amendment ①

📐 **The design's claim, verbatim (§7.3b):** *"③ is correct **whatever the configuration says**: the worst
case becomes 'an authoring host is missing updates' — staleness, and the probe reports it — instead of
'an authoring host lost its unpublished assets'."*

⛔ **Clause ③ is keyed on the SAME token as clause ①** — *"for a kind it **DOES author**, the sync degrades
to ADD-ONLY."* ⇒ the two clauses fire together or not at all.

| configuration | ① subtract | ③ add-only | outcome |
|---|---|---|---|
| token **present**, correctly | ✅ | ✅ | ⭐ safe — stale at worst |
| token **present**, over-broad | ✅ | ✅ | ⭐ staleness, **reported** — the case the design analysed |
| ⛔⛔ **token ABSENT on a host that DOES author** | ⛔ no | ⛔ no | 🔴 **full overwriting mirror onto the author's folder — the exact data loss ③ claims to prevent** |

⇒ ⭐⭐ **The design analysed one direction of misconfiguration and generalised to "whatever the
configuration says."** ⚠ The omission direction is the **likelier** one: the token is new, the default
will be *absent*, and `CgfSubsystem.DefaultRole = NodeRole.Brain` (`:92`) means **every CGF host is in
scope on day one**.

| ⭐ the fix, and it is small | |
|---|---|
| ⭐⭐⭐ **make ADD-ONLY unconditional for every kind arriving by the `Brain`/`BaseFolder` rule** — ⛔ not conditional on the authorship token | ⭐ then the claim *"correct whatever the configuration says"* becomes **true**, because no configuration can turn overwriting back on |
| ⭐ clause ① then does what it is actually for | it stops the **delete** half and keeps the needs set honest |
| ⭐ and it costs nothing | 📐 the only kinds reachable by that rule are Blueprint/BTree/Hsm — ⛔ all three land in a folder a human edits. **There is no case where overwriting one of them is desirable**, which is why the token was never the right guard |

---

### ⚠ F5 — **an add-only SKIP will be read as a FAILURE, and that fails the load** → `PLAN` `B4`/`B6`, `Q72-F`

📐 **`Q72-F` ruled:** *"'fails the load' must be defined as ANY failure count > 0."*
📐 **And it is already implemented literally** — `AssetPrefetchProcessManager.cs:103-104`:
`bool isSuccess = !task.IsFaulted && !task.IsCanceled && task.Result.FailureCount == 0;`
📐 `GatewayResult.IsFullSuccess => FailureCount == 0` (`StorageGatewayModule.cs:71`).

⛔⛔ **Clause ③ makes the sync deliberately NOT transfer a changed file** on an authoring host. On such a
host the diff reports *changed* entries on **every load, forever** — by design.

⇒ 🔴 **If those are counted into `FailureCount`, every scenario load on every CGF host fails**, with a
message naming files that were *intentionally* left alone. ⭐ The design never says which bucket they go
in, and the built path's default reading is the fatal one.

⭐ **State it explicitly in `B4`:** *an add-only skip is a SUCCESS with a reported reason; only an
I/O error counts as a failure.* ⚠ And rail it — the rail is *"an authoring host with local edits loads
its scenario successfully"*.

---

### ⚠ F6 — **restore-at-unpack introduces a silent-corruption window the direct-copy arm does not have** → design §2, §7.1

📐 §2 requires: extraction is followed by `File.SetLastWriteTimeUtc(dest, entry.LastWriteUtc)` — ⭐ the
mtime is restored **from the manifest**, not from the source file.

| arm | what stamps the node's mtime | if NAS changes between manifest-build and transfer |
|---|---|---|
| **standalone** | `File.Copy` carries the **actual** source mtime | ⭐ node gets new bytes **and** new mtime ⇒ next sync compares unequal ⇒ **self-heals** |
| ⛔ **archived** | the **manifest's** `LastWriteUtc` — a value now stale | 🔴 node gets **new bytes stamped with the OLD mtime**. If length is unchanged, `IsAlreadyCurrent` (exact equality, `:524-525`) says **current — forever** |

⇒ ⛔⛔ **§2's load-bearing sentence — *"the two transport shapes are indistinguishable to the skip"* — is
false in precisely the way that matters: they are indistinguishable when nothing races, and they differ
in FAILURE MODE when something does.** ⚠ The same-length case is not exotic: a re-saved asset of
identical size is the common shape.

⭐ **Cheap fixes, either is enough:** stamp from the **source file re-read at pack time** (not the
manifest snapshot), **or** re-stat the differing set after packing and abort the entry if it moved.
🔒 This belongs in `B4` beside the restore, and it wants its own rail — ⛔ it is not an implementation
detail, for the same reason the restore itself was promoted to a named success condition.

---

### ⚠ F7 — **"recursion exists NOWHERE today" is true of the orchestrator and false of the repo — and the prior art has hard-won properties `A2` will lose** → design §1 ①, `A2`

📐 **Measured (graph + grep agree), recursive walkers that already exist:**

| | |
|---|---|
| `Fdp.Toolkits/Tkb/Vfs/RawDirectoryTkbProvider.cs` | ⭐⭐ **the raw-TKB tree reader** — the very asset shape `Q72-H` legalises |
| `Hrot.Blueprints.Editor/BlueprintPeerSource.cs:56-66` | ⭐⭐⭐ `RecurseSubdirectories = true` **+ `IgnoreInaccessible = true` + `MatchCasing.CaseInsensitive`** |
| `Hrot.AiEditor.Persistence/{BTree,Hsm}JsonServices.cs` | the authoring-side recursive scans |
| `Hrot.Presentation/.../CuratedScenarios.cs` | — |

⭐⭐ **`BlueprintPeerSource`'s two extra options are scar tissue, and its comments say so:** `IgnoreInaccessible`
exists because a denied subdirectory *"would abort quick-reload's sibling-signature scan"*, and
`MatchCasing.CaseInsensitive` because *"`PlatformDefault` would silently miss them on Linux."*

⇒ ⛔ **A fresh `SearchOption.AllDirectories` walker in `A2` re-acquires both bugs.** ⭐ **The seam law
answer: mirror `BlueprintPeerSource`'s `EnumerationOptions`, and say in the report that you did.**
⚠ §1 ①'s sentence should be narrowed to *"no recursive enumeration **in the orchestrator**"* — ⛔ as
written it tells `A2` there is no prior art, which is the opposite of true.

⭐ **A positive that falls out of the same measurement:** `BlueprintPeerSource` **is** recursive ⇒ the
Blueprint reader already supports `Q72-K`'s subfolders. **The read side of the subfolder ruling is
already built for Blueprint** — only the distribution side is missing.

---

### ⚠ F8 — **clause ②'s "ADVERTISED BY CONFIGURATION" has no configuration surface** → `PLAN` `B2`, §4

📐 **The facility exists; the configuration path does not:**

| | |
|---|---|
| `ClusterSlave(… , IReadOnlyList<string>? capabilities = null)` | `ClusterSlave.cs:95` — tokens come from the **composition root**, as a literal |
| every production call site passes a literal | `CgfSubsystem.cs:1048-1049` → `new[] { CapabilityTokens.ReliableInit }`; others pass **nothing** (`IgNodeBootstrapper.cs:358`, `OrchestratorSubsystem.cs:136`, `NodeBootstrapper.cs:231`, `HrotNodeBuilder.cs:232`) |
| `NodeConfiguration` has **no** token/capability member | `NodeConfiguration.cs:42-95` — DDS, paths, rate, origin, temp root. ⛔ nothing |
| tokens are published **once at join** | `_capabilitiesPublished` (`ClusterSlave.cs:40`) — ⭐ fine for a config value, ⛔ **not runtime-changeable** |

⇒ ⚠ **`PLAN` §4's *"⛔ A new capability facility — `AQ-70`'s exists"* is true and reads as *"nothing to
build here."*** ⛔ **`B2` must build config → composition root → `ClusterSlave.capabilities` plumbing**,
and that work is named nowhere. ⭐ Add it to `B2`'s task text, and decide there whether the home is
`NodeConfiguration` (SimHost-shaped) or `HrotRunnerConfiguration` (which already carries `AssetRoot` —
📌 **and F1 wants that same file anyway, so the two fixes share a home**).

---

### ⚠ F9 — **the NAS-side layout for AI kinds and terrain is undefined, and it is not in the W register** → `PLAN` §3

📐 `OrchestrationConstants` defines NAS roots for **`scenarios`, `exercises`, `episodes`, `tkb`**
(`:21-23`, `:106`, `:113`) and `shared` (`:43`). ⛔ **Nothing for Blueprint/BTree/Hsm, nothing for terrain.**
📐 The design says so itself (§1 ③: *"nothing enumerates a NAS asset tree"*) — ⛔ **but then never decides
the layout**, and `W1`–`W4` cover threshold, caching, scenarios and deletion. **Not this.**

⇒ ⭐ **`B1`, `B4` and `C1` all need a NAS path per kind before they can be written.** Add a `W7`:
*the NAS root convention per `AssetKind`* — ⚠ and note it interacts with `Q72-K`: the NAS root is the
base the **relative path** is taken from, so getting it wrong relocates every entry in the manifest.

---

### ⚠ F10 — **a GLOBAL asset mirror is placed on the critical path of a PER-SCENARIO transition with a 300 s bound** → design §4, `B4a`

📐 `B4a` puts the sync inside `AssetPrefetchProcessManager`, and `ClusterMaster` parks the transition
until `PrefetchDistributionCompletedEvent` — with `ParkedTransitionExpirySeconds = 300.0`
(`ClusterMaster.cs:138`).

⛔ **But the AI-kind needs set is NOT scenario-scoped.** It derives from `NodeRole.Brain`, so it is
*"every syncable AI asset on the NAS"* — 🔒 and the user's own words are *"behavior assets … can be
numerous"* and the TKB is *"hundreds of small files."*

⇒ ⚠ **The FIRST sync on a fresh node mirrors the entire asset tree inside a parked transition**, and the
failure mode at the bound is 🔴 **a hang, not an error** — which §4's own caption names as the thing
`B4a` exists to prevent, for a different cause.

⭐ **Not necessarily wrong — but it must be a stated decision with a rail**, one of:
**(a)** the bound is raised/made proportional for the first sync · **(b)** the first sync is a separate
pre-transition step · **(c)** measured and accepted with a documented ceiling on tree size.
⛔ *"`W1`'s threshold is tuning"* does not cover this: the threshold moves traffic **between arms**, it
does not bound the **total**.

---

### ⭐ F11 — **two citation precision defects, both the programme's own named traps**

**(a) — the editor's Brain declaration.** The design cites `EditorSubsystem.cs:1451` for *"the editor
composes Brain too"*. 📐 That line is `LoadPhaseChain.FromRoles(NodeRole.Brain, …)` — **a literal argument
to a load-chain factory**, not a role declaration. ⭐ The **declaration** is `EditorCapabilities.cs:60-61`.
⚠ The claim is **TRUE**; ⛔ the citation is trap ①'s shape (*"to prove a role is composed, find where it
is ASSIGNED"*) — and trap ① was written by this same programme, one round ago.

**(b) — and this one is load-bearing.** §7.3a's claim table says *"behaviour assets go to every Brain
host, as files … ✅ the reader is built — `CgfSubsystem.cs:2078`, `:2506`, `:2479`."*
📐 **Measured: `:2078` and `:2506` are BOTH `BlueprintPeerSource(AssetRoots.AssetsFor(AssetKind.Blueprint))`.**
⛔ **They prove the reader for `Blueprint` — and for nothing else.**

📐 **And the BTree/Hsm picture is materially different.** Every consumer of `AssetsFor(BTree|Hsm)` is an
**authoring** surface (`BTreeNewAssetService.cs:42`, `HsmNewAssetService.cs:42`, the two JSON
contributors, `AssetBrowserPanel.cs:976`, `AssetSaveAsRequests.cs:33`). The `.btree.json` / `.hsm.json`
files are consumed by **`IIncrementalGenerator`s** — `BTreeJsonGenerator.cs:20`, `HsmJsonGenerator.cs:18`
⇒ ⭐⭐⭐ **they are BUILD-TIME inputs.** At runtime a BTree/HSM behaviour is compiled C# in an assembly,
which is exactly why `BTreeAssetContributor` / `HsmAssetContributor` are assembly-backed with a null
`BaseFolder` (**F3**).

⇒ ⛔⛔ **The sharp consequence, and it lands on clause ②.** §7.3b clause ② exists to create a host that
does **not** author and therefore **receives** — *"a CGF deployed as a runtime brain advertises none and
receives."* 📌 **That is precisely the host on which synced `.btree.json` / `.hsm.json` files are read by
nothing**: it does not author them, and it does not compile them.

⇒ ⭐ **§4.1a's *load-nothing-where-nothing-reads-it* test does NOT pass for BTree and Hsm** — the design
records it as passing on the strength of two Blueprint-only call sites. **Either** the Brain rule narrows
to `Blueprint` **or** the design states what a runtime brain does with behaviour JSON (recompile? and with
which toolchain?). ⛔ It cannot be left as *"the reader is built."*

---

## 4. ⚠ WHAT I DID NOT COVER — stated so nobody reads silence as clearance

| | |
|---|---|
| ⛔ **increment `C` end to end** | I measured `C2`'s probe premise (**F2**) and `C5`'s trigger, ⛔ but not `PullToNasAsync`'s per-file error swallowing, which `Q72-F` §3 flags and I did not re-measure |
| ⛔ **`B5`'s seam extraction** | `ITkbStorageStrategy`'s members are TKB-entity-shaped (`Q72-D` says so); I did **not** check whether `ZipTkbProvider`/`RawDirectoryTkbProvider` survive the narrowing unchanged, which is `B5`'s stated success condition |
| ⛔ **`B3`'s partition** | no measurement — `W1` is genuinely open and it is a tuning value |
| ⛔ **terrain** | 🔒 *"largely unimplemented"*; I took the design at its word and did not look |
| ⚠ **Stride** | the graph found a **fourth** role-declaring host — `StrideCapabilities.DefaultRole = MuscleGround\|Perception` (`:64`) — which the design's §6 diagram does not draw. ⭐ It is covered by the MUSCLE box **by role**, so I am **not** filing it as a finding; ⛔ but §6 draws hosts, and a reader will look for it |
| ⛔ **I ran no build and no tests** | this is a design review; ⚠ **F2 is a code defect I did not attempt to fix or rail** |
