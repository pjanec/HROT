# Changelog

## [Unreleased]

### ⚠️ BREAKING — Flight Recorder format v5 → v6 (3D Cognitive Spatial Awareness promotion)

**Recorded sessions do not survive this change. Tell everyone first.**

The 3D Cognitive Spatial Awareness promotion makes `SimTransform.Position.Z` the authoritative
physical altitude and carries real Z through EQS, perception/`TargetMemory`, pathing cost, the
navigation destination/trajectory carriers, and the position-carrying translators. This shifts
several blittable struct layouts and serialization formats engine-wide:

- `SimTransform.Position.Z` is now authoritative altitude (previously force-zeroed / a visual offset).
- `EqsResult` grew a `PositionZ` field (24 → 32 bytes); `EqsResultArray` 384 → 512 bytes.
- `EqsResultEntry` (DDS `hrot-eqs-msgs`) carries `PositionZ`.
- `TargetMemory` gained a `PositionsZ` parallel array.
- `CoverPoint` grew `PositionZ` (24 → 28 bytes).
- `NavigationIntent`/`NavState`/`MoveToParams`/`PlanRouteParams` destinations and
  `TrajectoryWaypoint.Position` widened `Vector2` → `Vector3`.
- `GroundClampingState` was slimmed and renamed to `TerrainClampBaseline`.

**Flight Recorder:** `FdpConfig.FORMAT_VERSION` bumped 5 → 6. Pre-v6 `.fdp` recordings are rejected
**fast** with a clear version-mismatch error by `PlaybackController`/`PlaybackSystem` (no silent
mis-deserialization). There is no migration path for old recordings — re-record after upgrading.

See `.dev/promote-to-3d/` for the full task set (P3D-001…P3D-405).
