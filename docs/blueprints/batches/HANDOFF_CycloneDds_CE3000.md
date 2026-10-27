<!--STATUS
state: LIVE — coordinator (HROT) → the CycloneDDS.NET lane: five library fixes, one release
updated: 2026-10-27
current-answer: the whole document
stale-below: nothing
related-designs:
  - ../Blueprint_Issues_Tracker.md CE-3000 — the HROT tracker row these five items come from (HROT keeps it; you report back to it)
  - ../../reference/NED_DDS_Protocol.md — how HROT uses the library on the wire (context only)
-->
# HANDOFF — CycloneDDS.NET: five fixes for HROT (tracker CE-3000)

**Dispatched at** the commit that adds this file (HROT `coordinator`). Self-contained: you need only the library repo.

## 0. Where and how

| | |
|---|---|
| repo | `pjanec/CycloneDds.NET` — read at HEAD `baafe2e` (2026-07-12) when this was written; re-check each `file:line` before editing |
| consumer | HROT uses NuGet package **`CycloneDDS.NET 0.3.2`** (tag `v0.3.2`). These fixes ship as the next release (Nerdbank versioning, `version.json` `0.3` — the release mechanics are `RELEASE-GUIDE.md`) |
| build / test | `.NET 10 SDK` (C# 13; binaries target `net8.0`). `dotnet build CycloneDDS.NET.Core.slnf`; tests in `tests/CycloneDDS.Runtime.Tests` (runtime), `tests/CycloneDDS.CodeGen.Tests`, `tests/DdsMonitor.Engine.Tests` |
| rules | every fix has a test that FAILS without it (prove it once: revert the fix, see red, restore); public API changes go in `CHANGELOG.md`; ⛔ no behaviour change for code that does not use the new options (an explicit topic name, default QoS — all keep working as today) |
| branch / release | work on a branch; ask the user before merging to `main`, tagging or publishing to NuGet |

## 1. The fixes, in the order to do them

### ① Sender identity can stay unresolved for a delivered sample  *(highest value)*

- **Where:** `src/CycloneDDS.Runtime/Tracking/SenderRegistry.cs`. The writer handle → participant GUID map is filled synchronously, from the reader's subscription-matched path (`RegisterRemoteWriter`, about line 92). The participant GUID → identity map is filled only by `MonitorIdentitiesAsync` (about line 54), which awaits `_identityReader.WaitDataAsync` and then calls `ProcessIncomingIdentities`. That await is a `TaskCompletionSource` with `RunContinuationsAsynchronously` (`DdsReader.cs:224`), so its continuation runs on the thread pool.
- **The failure:** under thread-pool starvation, an identity sample that has ARRIVED is not yet PROCESSED, so `TryGetIdentity` / `DdsLoan.GetSender` (`DdsLoan.cs:46`) returns null for a delivered data sample.
  - Measured by HROT: 1 in 4 full test runs. In a starved-pool probe it happened 1 in 3, and stayed unresolved for 10 s.
- **The fix:** on a miss in `TryResolveLazy`, drain the identity reader synchronously (a non-blocking `Take`, i.e. `ProcessIncomingIdentities`), then look again.
  - ⚠ That makes `ProcessIncomingIdentities` run on two threads, so guard it with a lock.
  - Better still: process identities from the identity reader's DDS data-available listener instead of a pool continuation.
- **Test:** two participants, both with sender tracking.
  - Block or starve the pool (e.g. `ThreadPool.SetMaxThreads` low plus blocking work items, or a test seam that suppresses the monitor).
  - Write the identity, then a data sample.
  - Assert that the reader's first `Take` resolves the sender. Red today.

### ② `[DdsQos]` cannot set the writer data lifecycle

- **Where:** `src/CycloneDDS.Schema/Attributes/TypeLevel/DdsQosAttribute.cs` exposes only `Reliability`, `Durability`, `HistoryKind`, `HistoryDepth`. `DdsWriter` applies only those (`DdsWriter.cs:100-112`). `Interop/DdsApi.cs` has no `dds_qset_writer_data_lifecycle` import.
- **Why it matters:** Cyclone's writer default is `autodispose_unregistered_instances = true`.
  - HROT hands a keyed instance from one writer to another: the old owner must UNREGISTER without disposing, and the new owner keeps writing.
  - With the default, the old owner's unregister DISPOSES the instance for every reader (measured).
  - If the old owner never unregisters, the instance keeps two writers, and the new owner's crash produces no not-alive sample.
- **The fix:**
  - Add `bool AutodisposeUnregisteredInstances { get; set; } = true` to `DdsQosAttribute`; the default `true` keeps today's behaviour.
  - Import `dds_qset_writer_data_lifecycle(IntPtr qos, bool autodispose)`.
  - Apply it in the writer's QoS path, both the attribute path and any per-writer QoS overload.
- **Test:** a writer with `false` registers and writes an instance, then unregisters it. A reader sees `NotAliveNoWriters` (not `NotAliveDisposed`) once no writer is left. With the default `true`, today's `NotAliveDisposed` is unchanged.

### ③ A noisy dispose

Disposing a participant prints `[SenderRegistry] Monitor task failed: ObjectDisposedException …` (`SenderRegistry.cs:72`): the identity reader is disposed under the running monitor.

- **The fix:** in `Dispose`, cancel `_cancellation`, wait for `_monitorTask` (with a short timeout), and only then dispose the identity reader.
  - Alternatively, treat `ObjectDisposedException` after cancellation as expected.
- **Test:** create and dispose a participant with sender tracking; nothing is written to the console (capture `Console.Out`).

### ④ `DdsWriter<T>.WaitForAcks` is missing

- **Where:** the native `libddsc` exports `dds_wait_for_acks(dds_entity_t writer, dds_duration_t timeout)`. `DdsApi.cs` does not import it, and `DdsWriter` exposes no public native handle, so a consumer cannot call it.
- **The fix:** `public bool WaitForAcks(TimeSpan timeout)` on `DdsWriter<T>`.
  - `true` = every matched reliable reader acknowledged everything written so far, disposes included.
  - `false` = timed out (`DDS_RETCODE_TIMEOUT`).
  - Any other return code → throw like the other calls.
- **HROT's use:** a node reports "ready" only after its dispose samples are acknowledged.
- **Test:** a reliable writer and a matched reader; write N samples and a dispose, then `WaitForAcks(5 s)` is `true`. With a reader that never takes (or a stopped reader process), a short timeout returns `false`.

### ⑤ The default topic name is documented but not applied

- **Documented:** `DdsTopicAttribute` (`src/CycloneDDS.Schema/Attributes/TypeLevel/DdsTopicAttribute.cs`) says a null name means *"derived from the type's fully-qualified name (dots replaced by underscores)"*.
- **Computed in two places, with two different spellings:**
  - the code generator, `tools/CycloneDDS.CodeGen/SchemaDiscovery.cs:311-330`: `Namespace + "." + Name`;
  - DdsMonitor, `tools/DdsMonitor/DdsMonitor.Engine/Metadata/TopicMetadata.cs:56-58`: `Type.FullName`.
  - They disagree for a NESTED type: the generator drops the outer type, DdsMonitor keeps `Outer+Inner`.
- **Not applied:** the runtime `GetTopicNameFromAttribute` in `DdsWriter.cs:156` and `DdsReader.cs:159` returns `attr.TopicName` unchanged, so a `[DdsTopic]` with no name opens its topic as `null` (`DdsWriter.cs:79-81`).
- **The fix:** ONE public rule in `CycloneDDS.Schema` (shared by the generator, the runtime and DdsMonitor), e.g. `public static class DdsTopicName { string Of(Type t); string Of<T>(); }`:
  - `attr.TopicName` when not null or whitespace;
  - else namespace + containing types + name, joined with `_`. For example `A.B.Outer.Inner` → `A_B_Outer_Inner`; no `+`.
  - The generator works on Roslyn symbols, not `Type`, so give it a string-based core (`Of(string ns, IEnumerable<string> typePath)`) that the `Type` overload also calls. ⇒ one spelling, by construction.
  - Use it in `DdsWriter`, `DdsReader`, `SchemaDiscovery` and `TopicMetadata`.
- **Tests:**
  - a name-less `[DdsTopic]` writer and reader on one participant exchange a sample over `Ns_Type`;
  - the generator, the runtime and DdsMonitor give the same name for a top-level and a nested type;
  - an explicit name still wins;
  - a whitespace name counts as none.

## 2. Release and report back

1. All tests green, each new test proved red without its fix.
2. `CHANGELOG.md`: the new public API (`DdsQos.AutodisposeUnregisteredInstances`, `DdsWriter.WaitForAcks`, `DdsTopicName`) and the behaviour fixes (①, ③, the ⑤ default).
3. With the user's go-ahead: merge, tag, publish (`RELEASE-GUIDE.md`).
4. **Report** to HROT, as a short note in HROT's `docs/blueprints/batches/` or to the user:
   - the released version;
   - per item: the test names and what each asserts, plus the red-proof;
   - any `file:line` above that had moved, and anything you could not do and why.

## 3. Not yours *(context, so you do not do it)*

After the package bump, HROT will:
- delete its sender-identity workaround (`EntityMasterIngressTranslator.RetryUnresolvedOwners`);
- set `AutodisposeUnregisteredInstances = false` on its descriptor writers;
- call `WaitForAcks` in its replay swap;
- remove every topic-name string from its 125 `[DdsTopic("…")]` attributes and ~144 translators. That is one pass, and ⚠ it must wait for the release, because 30+ HROT readers/writers take their name from the attribute today.
