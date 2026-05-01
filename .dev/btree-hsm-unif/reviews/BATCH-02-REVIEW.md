# BATCH-02 Review

**Reviewer:** Dev Lead
**Status:** APPROVED

---

## Verification Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Fbt.Tests | 160 | 171 | +11 |
| Fhsm.Tests | 241 | 251 | +10 |
| Solution build | clean | clean | 0 errors |

All pass. Zero error CS lines in IOS-IG-SimHost.sln build.

---

## Spot Checks

**BHU-011**: `SharedAiAttributes.cs` exists in `Fbt.Kernel`. All three attributes + `ChannelKind` enum present, `AllowMultiple = true` on `WritesChannelAttribute`. Attribute constructor signatures match `(Type dtoType, string fieldName)` pattern — verified against file content.

**BHU-012**: `BTreeActionGenerator.cs` — `LayoutKind.Explicit == 2` bug fixed at both sites (line 298, 361). FNV-1a hash seed `2166136261` present (line 558). `WritesChannels` collection integrated. Compound-key `@offset` format emitted.

**BHU-013**: `HsmActionGenerator.cs` — `bridge->WorldHandle` (NOT `RepoHandle`) used in all three thunk emission sites (lines 580, 599, 619). `RequiredExitCleanups` dict emitted as `public static readonly IReadOnlyDictionary<string,string>` field. `ExitCleanup_` thunks registered.

**BHU-014**: `HsmGraphValidator.cs` — `ValidateChannelSafety` static method present, `Validate` overload accepting nullable dict calls it after base validation. Channel-safety tests pass (6 tests added).

**Critical invariant**: `bridge->WorldHandle` confirmed at 3 generator emission sites — no `RepoHandle` usage found.

**Critical invariant**: `LayoutKind.Explicit = 2` confirmed correct at 4 sites (2 in BTree generator, 2 in HSM generator).

---

## Issues / Deviations

None reported by agent. No deviations from spec.

Minor note: HSM thunk execution tests are reflection-only (not runtime invocation against a live `HsmKernelBridge`) because `Fhsm.Tests` does not reference `Fdp.Toolkit.*`. This is an acceptable constraint — integration coverage will come through BHU-017 (BATCH-03).

---

## Decision

**APPROVED — commit.**

Suggested commit message (from report):
`feat: BHU-011..014 shared AI attributes + channel safety generators`
