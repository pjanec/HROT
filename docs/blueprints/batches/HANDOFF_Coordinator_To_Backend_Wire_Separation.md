<!--STATUS
state: LIVE — coordinator → backend: after S0 — CE-3356 (a gateway create asks for id -1) · CE-4207 (split EqsResultEntry) · CE-4208 (no DDS struct or enum on the internal path — audit + fix) · CE-4209 (guard the by-name semantic types) · correct CE-4200's commit ids
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../RULINGS.md — R-134 (network strictly separated from the internal path; the translator is the SOLE boundary; duplicated structs / enums are the correct pattern) — THE basis of CE-4207 / CE-4208
  - ../../DESIGN_Entity_Creation_Unification.md — §3.9 (the creation path; who allocates an id) — CE-3356
  - ../../DESIGN_Semantic_Value_Types.md — §2.1 (S0's as-built) — CE-4209, and CE-4200's EQS exclusion CE-4207 removes
  - ../../reference/NED_DDS_Protocol.md — update for every wire type CE-4207 / CE-4208 moves or twins
  - ../Blueprint_Issues_Tracker.md — CE-3356, CE-4200, CE-4207, CE-4208, CE-4209 (the rows are the spec)
-->
# HANDOFF — coordinator → backend: **after S0 — wire separation and three fixes** *(overnight, autonomous)*

**Dispatched at** the commit that adds this line (`coordinator`) — ⚠ re-dispatched before start (rule 1a: `9d79fd3b7` is not on `backend`): item 0 widened and item 5 added after the user asked about the narrowing casts; ⛔ **scope frozen at THIS commit** — a later document that invalidates an item ⇒
STOP that item, report it, carry on (`R-106`). ⚠ This REPLACES the deleted `HANDOFF_Coordinator_To_Backend_After_S0.md` — that one is
withdrawn (user, `2026-11-02`: *"we are not yet ready for starting the Scenario macros implementation … please delete the handoff"*); ⛔ do
NOT build its items (S9′, CE-4154, CE-4157, CE-4155, node features S1, the semantic types, macros S3a) unless a later handoff names them.

🔒 **User, `2026-11-02`:** *"can you file a new handoff to the backend to take these tasks after it completes s0?"* · *"whenever there is the same
structure that serves as a network structure and internal structure, this is not desired. The engine should be agnostic to the network, so even
if we need to duplicate the structures, we are taking this cost for the sake of agnosticism. Same approach is taken for any enum values as well."*
· on CE-3356: *"0 is a default value that should mean invalid. So if that 0 fits the use case, that's fine with me and you can confirm this
solution in the handoff."*

## Start

1. ⭐ Finish S0 — its gate report (the Rule-8 table, incl. the filtered cluster suites and the ddsmonitor capture of `int32` ids) — and push it.
2. Merge `coordinator` (⚠ it carries the `2026-11-01` cleanup of `DESIGN_Scenario_Rules.md`; §9.8 was left for you — keep your §9.8.11 and the
   cleaned text around it). Push an empty `chore: started wire-separation batch at <sha>`. `dotnet restore` each test project once after the merge.

## The items, in this order

| # | item | the spec | done when |
|---|---|---|---|
| **0** | ⭐ **correct CE-4200's row**: it cites commits `a9d47cdf2`, `4369b3b64`, `21335195c`, `13870623f`, which do not exist on any branch (the history was rewritten); the real ones are `481e8a9a4` (7a+7b), `0c417651b` (7c), `332ae3333` + `9b7226250` + `07ba86e30` + `da1ffbcd9` (7d), `88a7201e4` (7) — check and fix · ⭐ and its claim *"the 69 `checked((int)…)` casts go"* is WRONG — the coordinator's count matched `unchecked((int)…)` too (hashing / bit patterns); measured properly, `coordinator` before S0 had ONE boundary narrowing (`NedCommandGateway.cs:154`, `checked((int)ack.EntityId)`), and S0 removed it — say so in the row | the row | every cited sha resolves (`git cat-file -e`); the cast count stated as measured |
| **1** | ⭐ **CE-3356** — a create through the NED gateway asks for network id **-1** | the CE-3356 row. ✅ **The solution is CONFIRMED by the user:** the gateway writes **`0`**. ⭐ It fits because `0` already means *"no id / invalid"* everywhere it matters: `EntityRef.IsNone` is `NetworkId == 0` (`Fdp.Toolkits/Replication/EntityRef.cs:37`), the CGF adapter's `preAllocated = 0` documents *"0 = allocate here"* (`Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs:43`), and the allocators start at 1000 so no real entity is ever `0`. ⇒ *"no id given"* and *"invalid"* are the same value, and the creator allocates. Fix `NedTranslationHelper.BuildCreateEntityDescriptors` (`Hrot.Network.NED/ExCon/NedTranslationHelper.cs:27`, today `EntityId = -1`); ⛔ no `-1` sentinel anywhere on that path | a rail: a gateway create gets an allocated id `> 0`; the two `*SpawnMovingVehicle*` gateway rails green (they were red at base for this reason) |
| **2** | ⭐ **CE-4207** — split `EqsResultEntry` (one struct is the wire payload AND the in-process result; its `EntityId` is a network id on the wire and a local 64-bit handle after `MapToLocal`) | the CE-4207 row + R-134 | the internal event carries an internal record with a local `Entity`; a wire twin with an `int` network id lives in the network assembly; the translators are the only converters; CE-4200's EQS exclusion removed (and its note in `DESIGN_Semantic_Value_Types.md` §2.1); the EQS distributed rails green |
| **3** | ⭐ **CE-4208** — AUDIT + FIX every DDS struct or ENUM on the engine's internal path, and every DDS type declared in an engine / toolkit assembly (11 files listed in the row) | the CE-4208 row + R-134 (the pattern: `AttributeValueKind` internal vs `AttributeValueType` wire; `NavigationIntent` vs its wire twin) | ⭐ the audit TABLE in the row (type · where declared · who uses it · move or split); each type MOVED into a network assembly (translator-only use) or SPLIT into an internal type + a wire twin (internal use too); enums the same (a numerically identical wire enum); ⭐ a RAIL fails on any `[DdsTopic]` / `[DdsStruct]` type — or a type a DDS struct uses as a field — reachable from an engine assembly's non-translator code; red-proved. ⚠ An item that would need a design call (a type whose split changes a public contract other lanes use) — STOP that type, list it in the report, carry on |
| **4** | **CE-4209** — guard the three semantic types matched by NAME on the netstandard2.0 side (`Stage2_Validate.cs:1339`, `StaticTypeRegistry.cs:63`, NodeEdit `PinDefaultValueEditorRegistry.cs:43`) | the CE-4209 row | a net8 rail: every such string equals `typeof(…).FullName` of a type in `SemanticValueTypes`; red-proved by a rename |

| **5** | the two id narrowings S0 left (measured on `backend`, `2026-11-02` — the only `checked((int)…)` casts besides a byte length in `JsonAttributeCompiler`) | ① `TimelineFrameScanner.cs:63` narrows `SearchResultPayload.Subject` (`ReplayBrowser/Search/ResultProjection.cs:33`, `long Subject, long Other`) into the `int` `TimelineEvent` — they are network ids (the other arm uses `ResultIds.Of`) ⇒ make the payload's `Subject` / `Other` `int` (or `EntityRef`), the cast goes · ② `MultiInstanceLayout.ReadEntityId` keeps a `checked` branch for *"a wider key field (a legacy topic)"* (`Replication/Utilities/MultiInstanceLayout.cs:46-52`) ⇒ after CE-4207, if no DDS key field is 64-bit any more, delete the branch and let a rail fail on a 64-bit entity key | no `checked((int)…)` on an entity id anywhere; the timeline lane rails and the multi-instance descriptor rails green |

⭐ **Design detail is yours** — where a row leaves a choice (the internal record's shape, which assembly a moved type lands in), decide it and
write it into the owning design (`DESIGN_Semantic_Value_Types.md` for CE-4207 / CE-4209; `NED_DDS_Protocol.md` for every wire type moved or
twinned) — not only the report.

⭐ **Sonnet** for CE-4208's mechanical moves and splits once you have measured the table (hand it file:line per type); ⭐ **your own model** for
the audit itself, the rail, and every review. ⚠ CE-4208 touches files other lanes own (perception, squad, time, Vis2D, animation, IG) — name
each in the commit messages and the report.

## Gates *(CLAUDE.md Rule 8)*

| gate | must report |
|---|---|
| ⭐ **T-1, the features' own suites FIRST** | item 1: the gateway spawn rails, `NedEntityCreationRequestEgressRails`, the entity-creation suites · item 2: the EQS distributed rails, `EqsResultIngressTranslator` / egress tests · item 3: each moved / split type's own suite (survey, danger area, time messages, selection, animation replication, IG capabilities) · item 4: the blueprint compiler suite |
| each touched test project, `--no-build` after ONE build of the test project | pass / fail / skip + delta vs baseline |
| ⭐ **the integration suites** (these are wire changes) | `ClusterRunner.Integration.Tests` `--filter`ed to spawn / EQS / replication / time-sync (the full batch hangs — CE-4158; state the base sha) · `Hrot.SimHost.Integration.Tests` · `Hrot.IG.Tests` · `Hrot.Animation.Network.Integration.Tests` |
| ⭐ **a wire capture** | `ddsmonitor` (pass `--AppSettings:TopicSources`): a twinned topic still carries the same samples |
| every red | pre-existing against the base sha, named |
| `tracker-counts.py --check` · `design-digest.py --check` · `rulings-check.py` · `mermaid-check.mjs` on touched designs | ✅ / ❌ |
| the working tree after every suite run | clean |

## Done when

Items 0–4 hold their done-when, the designs and `NED_DDS_Protocol.md` carry the as-built, and the report has the table above — or a stopped item /
type is named with its reason and the rest are done.

_coordinator lane, 2026-11-02_
