<!--STATUS
state: LIVE — a Sonnet task brief (CE-4078), not started
updated: 2026-10-28
current-answer: the whole document
stale-below: nothing
related-designs:
  - ../Blueprint_Issues_Tracker.md CE-3000 — the library fixes these items consume (①②④); CE-4078 is this task's row
  - ../../DESIGN_Ownership_Groups_And_Grants.md ~§5.4 (lines ~360-372) — OWNS the handover: the giver unregisters after the grantee confirms, which needs autodispose OFF (②)
  - ../../DESIGN_Replay_Time_Axis_And_Review.md §12 W3 (rows 37, ~814, ~866) — OWNS "report prepared / seek-done only after the disposes are acknowledged" (④)
  - ../../designs/bugs-2/BUG2-DESIGN.md — OWNS sender identity on every participant (①)
  - ../../reference/NED_DDS_Protocol.md — the wire reference to keep current
-->
# TASK CE-4078 (Sonnet) — use the CycloneDDS.NET 1.2.20 fixes in HROT, and drop the redundant `[DdsId]`s

**Dispatched at** `f03270309` (coordinator, 2026-10-28). HROT has used **CycloneDDS.NET 1.2.20** since
`df04652e6`. The library is published, and its source is github.com/pjanec/CycloneDds.NET at `main` `416c68b`. Read the APIs there:
- `DdsQosAttribute.AutodisposeUnregisteredInstances` (`src/CycloneDDS.Schema/Attributes/TypeLevel/DdsQosAttribute.cs:38`, default `true`);
- `DdsWriter<T>.WaitForAcks(TimeSpan)` (`src/CycloneDDS.Runtime/DdsWriter.cs:265`);
- the sender-identity fix in `CHANGELOG.md`.

**Work on `coordinator`. One commit per item, each with its own tests. Push only when the reviewer (Opus) says so.** Follow CLAUDE.md:
- build the TEST project before `dotnet test --no-build`;
- run long builds and tests in the background;
- find each feature's EXISTING suite and add tests there;
- red-prove each test with a mutation that compiles;
- fold any as-built deviation back into the owning design.

## The four items

| # | what | where to start | design basis |
|---|---|---|---|
| **①** | **Delete the sender-identity workaround.** The library now resolves a sample's sender when it is delivered, so `EntityMasterIngressTranslator.RetryUnresolvedOwners` (`Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityMasterIngressTranslator.cs:303`, called at `:105`) and the state it keeps go. | its tests in `Hrot/Subsystems/Hrot.IG.Tests/EntityMasterTranslatorTests.cs` (~345, ~658) are replaced by one asserting that a ghost's primary owner is set **on the first poll** over two sender-tracked participants | CE-517 / R-177 (the replica records the EntityMaster's writer as primary owner); CE-3000 ① |
| **②** | **Descriptor writers stop auto-disposing on unregister.** Set `AutodisposeUnregisteredInstances = false` on every **descriptor** topic's `[DdsQos]`; messages keep the default. Then make the handover's giver **unregister** its instance once the grantee confirms, if it does not already (the design's F7). | find where `[DdsQos]` sits on descriptor types (`Hrot/Network/Hrot.Network.NED/*Descriptors*.cs`, `GenericDescriptors.cs`, `Fdp.Network.Cyclone/Topics`) and the handover code (search `UnregisterInstance`, `OwnershipUpdate`, the grant/confirm path) | `DESIGN_Ownership_Groups_And_Grants.md` (~360-372): *the giver's unregister must not dispose*; the BDC/NED spec's handover step 2 |
| **④** | **The replay / review swap waits for acknowledgements.** A node reports *prepared* / *seek-done* only after its retraction disposes are acknowledged (`WaitForAcks`, **bounded**). A timeout is **reported in the result, not fatal**. | the seam the design names: `IRecordReplayController.HoldEgress` and the handlers' release (prepare / seek result) | `DESIGN_Replay_Time_Axis_And_Review.md` §12 **W3** (rows 37 and ~866: *"⏸ not built — CE-3000 ④"*) |
| **⑤** | **Remove the redundant `[DdsId]`s** from the 7 HROT-owned files: every type there is appendable and its ids are 0, 1, 2… in declaration order, so they change nothing. `FDP/Network/Fdp.Network.Cyclone/Topics/{EntityMasterTopic,CommonTypes,EntityLifecycleStatusDescriptor,IdAllocTopics,OwnershipUpdate,EntityStateTopic}.cs` and `FDP/Toolkits/Fdp.Toolkits/Time/Messages/TimeMessages.cs`. ⛔ NOT SumoSharp (`FDP/ExtDeps/SumoSharp`, vendored, read-only, R-4046) and NOT the test type in `Fdp.Network.Cyclone.Tests/Translators`, unless it only mirrors these. | `Fdp.Network.Cyclone.Tests/Topics/TopicSchemaTests.cs` (~52, ~113) asserts the attributes: drop those id assertions and keep its other checks | no design asks for them (searched `docs/` and `.dev/`, 2026-10-28); appendable types match fields by position, so the ids do not reach the wire |

## Success conditions (report each with evidence)

1. **①:** `RetryUnresolvedOwners` and its retry state are gone, and grep shows no reference to them. A test over real DDS, with two sender-tracked participants, shows the ghost's primary owner resolved on the first poll. Red-proof: make the ingress ignore the sender → red.
2. **②:**
   - Every descriptor `[DdsQos]` says `AutodisposeUnregisteredInstances = false`; list them, with the count.
   - A test in the ownership-transfer suite over real DDS: after a handover, the giver unregisters, and a reader sees the instance still **alive**, written by the new owner, with no `NotAliveDisposed`.
   - A second test: when the last writer goes away, the reader sees `NotAliveNoWriters`, not `NotAliveDisposed`.
   - Red-proof: autodispose back on → red.
   - If the giver did not unregister before, say so, and name the design line it now satisfies.
3. **④:**
   - A test: a node's prepare / seek result arrives only after `WaitForAcks` returned true.
   - With a reader that never acknowledges, the result carries a timeout flag or text and the operation still completes.
   - Red-proof: skip the wait → the first test is red.
   - Update `DESIGN_Replay_Time_Axis_And_Review.md` row 37 and ~866 from "⏸ not built" to as-built.
4. **⑤:**
   - No `[DdsId` in the 7 files.
   - `TopicSchemaTests` passes.
   - The generated IDL for one of the types (e.g. `OwnershipUpdate`) still matches on the wire. Show a real-DDS round trip that already exists, or a new one.
5. **Gates:**
   - `dotnet build HROT.sln --no-restore`: 0 errors (once, at the end).
   - Suites, with every red shown pre-existing on the base commit:
     - `Hrot.Network.NED.Tests`
     - `Fdp.Network.Cyclone.Tests`
     - `Hrot.IG.Tests`
     - `Hrot.Orchestrator.Tests`
     - `Hrot.SimHost.Tests`
     - the ownership-transfer and replay suites you touched
   - `scripts/ned-wire-inventory.py --check`, `rulings-check.py` and `tracker-counts.py --check` pass.
6. **Wrap-up:**
   - CE-4078 is ticked done with a short note.
   - CE-3000's "still open" list says ①②④ are done.
   - `NED_DDS_Protocol.md` reflects ② (descriptor QoS) and ④ if it describes them.
   - The working tree is clean.
   - Commit messages end with the two attribution lines the session gives you, and name no model.

**Report:**
- a diff summary per item;
- each test, with what it asserts and its mutation;
- the gate table;
- the commit shas;
- anything you could not do and why. Show the search behind any "does not exist" or "cannot".
