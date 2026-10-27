<!--STATUS
state: LIVE — coordinator (HROT) → the CycloneDDS.NET lane: 0.4 (`claude/dds-security`) tried in HROT; one library fix still needed
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - HANDOFF_CycloneDds_CE3000.md — the five fixes; all five are in 0.4 (its CHANGELOG)
  - ../Blueprint_Issues_Tracker.md CE-3000 — the HROT row
-->
# NOTE — HROT tried CycloneDDS.NET 0.4: one library fix still needed (enum `bit_bound`), two behaviour changes to document

**Dispatched at** the commit that adds this file (HROT `coordinator`). Package built locally from `claude/dds-security` `d93cc44`:
`CycloneDDS.NET 0.4.4-gd93cc449de` (Linux natives only), consumed from HROT's gitignored `nugets/` feed.

## Result

All five CE-3000 items are in 0.4. ⭐ HROT builds and runs against it after the two HROT-side fixes below plus one TRIAL-ONLY patch.
13 DDS-heavy suites: every red also fails on 0.3.2 (measured on the same tree), so nothing here is a 0.4 regression.
`Hrot.IG.Tests` (where the sender-identity race showed 1 run in 4) is 455/0.

## ⛔ Needed from the library — the generator emits enum IDL that Cyclone 11's `idlc` rejects

| HROT type | C# | what the generator emits | idlc 11 says |
|---|---|---|---|
| `Fdp.Toolkit.Navigation.NavLayerMask` | `enum : uint { …, All = 0xFFFFFFFF }` | `@value(4294967295) All` | `@value(4294967295) cannot be applied to 'enumerator' element` |
| `Fdp.Toolkit.Behavior.Components.BehaviorOrigin` | `enum : byte { …, Self = 255 }` | `@bit_bound(8) enum { …, @value(255) Self }` | `Enumerator overflow` (bit_bound 8 = signed 8-bit) |

- ⭐ **Fix ① — the size from the VALUES, not the C# underlying type:** pick the smallest `@bit_bound` (8, 16, 32) whose SIGNED range holds every value.
  A `byte` enum holding 255 gets `@bit_bound(16)`. A value no signed 32-bit enumerator can hold (`0xFFFFFFFF`) gets a clear generation-time
  error naming the C# type and member, never IDL that `idlc` rejects later.
  - ⚠ HROT cannot renumber `BehaviorOrigin.Self`: recordings store the value. So this is a library fix.
  - HROT fixed `NavLayerMask` itself: `All` is now the union of the defined layers.
- ⭐ **Fix ② — emit IDL only for types REACHABLE from a `[DdsTopic]` / `[DdsStruct]`.** The generator wrote 180 `.idl` files for `Fdp.Toolkits`, many for
  enums that are on no wire (both enums above are ECS-only). With ② alone neither would have failed.
- Rail: a generator test with a `byte` enum holding 255 and a `uint` enum holding `0xFFFFFFFF`. The first must produce IDL that the bundled `idlc` accepts, and
  the second must produce the clear error, or no IDL if it is unreachable.

## Behaviour changes to put in the CHANGELOG (HROT hit both)

1. **Writing an enum value the enum does not declare now fails:** `DdsWriter.Write` → `DdsException … BadParameter (-3)`. 0.3.2 wrote it unchecked.
   - Measured on HROT's `NodeOpStatus`: the operation enum starts at 1, and a default-initialised message carried 0.
   - HROT added a zero member to both affected enums. Consumers will hit this with any default-initialised struct whose enum lacks 0.
2. **`idlc` is stricter about enumerator values**, as in the table above.

## HROT side (FYI)

- **A build-only trial needs the .NET 8 runtime beside the .NET 10 SDK.** The code generator targets net8.0, and a side-by-side .NET 10 install fails with "You must
  install .NET … 8.0.0". Worth a line in `RELEASE-GUIDE.md`.
- **HROT's full `Hrot.ClusterRunner.Integration.Tests` run is broken on BOTH versions:** it aborts on 0.3.2 and hangs on 0.4. The test it hangs in passes alone
  on both, so this is not attributed to 0.4.

⏭ **Ask:** fix ① (and ②), then publish 0.4. HROT then switches with one line, `CycloneDdsNetVersion` in `Directory.Build.props`, and starts the CE-3000
follow-ups.

_coordinator lane (HROT), 2026-10-27_
