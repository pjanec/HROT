# ADA-BATCH-05 Review (TKB catalog + world/coordinate info)

**Verdict:** ACCEPTED (first pass). **Reviewer:** dev lead (full-solution build + diff + real headless reproduce, run personally).

## Verified independently (lead)
- **Full-solution build** (`dotnet build IOS-IG-SimHost.sln`) → **0 errors, 0 warnings**. This was the key
  risk: `IGeographicTransform` gained a member (`Origin`), a breaking change for every implementer. Only one
  production implementer (`WGS84Transform`, updated); the rest are test mocks (11 files, all updated). The
  full build confirms no implementer was missed across Map.Common.Tests / IG.Tests / SimHost.Tests.
- `dotnet test … --filter "FullyQualifiedName~DebugApi"` → **51/51 passed** (32 prior + 19 new).
- **Real headless reproduce** (`-m editor --debug-api --headless`, curl) — the arbiter, and it confirms no
  harness-vs-production gap (the constructor defaults `_geoTransform`/`_tkbDb` to empty fresh instances when
  null; the real process must pass the populated ones — and it does):
  - `GET /world/info` → `origin {lat:52.52, lon:13.405, alt:0}` (**Berlin, not 0,0,0**), grid 1000×1000m
    (`extent maxX/maxY:1000`), `terrain:null, navmesh:null`. ✅
  - `GET /tkb/types` → **15 types**, first = `{tkbType:100, name:"M1 Abrams"}`. Genuinely populated. ✅
  - `POST /world/geo-to-local {52.52,13.405,0}` → `(0,0,0)` (origin maps to local zero). ✅
  - `POST /world/local-to-geo {100,0,100}` → ≈Berlin (lat 52.5199…, lon 13.4064…, alt 100.0008). Round-trip
    within tolerance. ✅
  - Clean `/shutdown` (200). ✅

## Diff review
- `IGeographicTransform.Origin` getter — additive only; `WGS84Transform` implements it by converting stored
  radians back to degrees. `ToCartesian`/`ToGeodetic` untouched (confirmed in diff). Sound.
- `EditorSubsystem` wires the **real** `HrotEnvironment.CreateGeoTransform()` (Berlin) + `CreateTkb()`
  (populated) + `PerceptionConstants` grid (200×200×5m) into the service — not the null defaults.
- `ListTkbTypes`/`GetTkbType` use the dynamic `TkbDatabase.GetAll()`/`TryGetByType` projection (no hardcoded
  array); descriptors via `EventSerializationHelper` (DTO path). 404 on unknown type.
- `GeoToLocal`/`LocalToGeo` use `WGS84Transform` + `SimTransformBridgeSystem.Heading↔Rotation`. Stateless,
  off-thread-safe (no `RunMain`) — correct.

## Heading-convention check (resolved — NOT a bug)
`geo-to-local headingDeg:90` returns the identity quaternion `(0,0,0,1)`, which initially looked like
heading 0. It is correct: `HeadingDegToRotation` uses `mathYaw = (90 − heading)`, and the underlying frame is
`yaw 0 = East`. So heading 90° (East) = mathYaw 0 = identity, and `RotationToHeadingDeg(identity)` = 90 — the
round-trip is internally consistent (North=0 → non-identity `w≈0.707`). The agent's test is legitimate.

## Minor / debt
- **ADA-05-D01** (P3, cosmetic): `disType` serializes as the CLR type name `"Fdp.Core.DISEntityType"` rather
  than a meaningful DIS value (`DISEntityType.ToString()` not overridden). The AI still gets `tkbType` + `name`
  + `categoryPath`; revisit if DIS-type filtering is needed. `categoryPath` was empty for M1 Abrams (likely the
  template simply doesn't set it).
- Smoke asserts `geo.origin.lat/lon` **presence** only, not non-zero — so it alone wouldn't catch a (0,0,0)
  origin. The lead's manual reproduce closed that gap (Berlin confirmed). Acceptable; noted for future smoke
  hardening.

## Lesson
The interface-member addition was the real hazard here (silent break of an out-of-filter test project), and
the full-solution build was the right gate for it — the DebugApi-filtered test run would not have caught a
broken mock elsewhere. Second clean batch in a row on the real-headless gate.
