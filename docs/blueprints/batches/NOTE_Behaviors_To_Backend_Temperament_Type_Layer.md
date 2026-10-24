<!--STATUS
state: LIVE
updated: 2026-10-24
current-answer: the whole note (short)
stale-below: nothing
related-designs:
  - docs/DESIGN_Temperament_And_Stress.md — OWNS Nerve / Resolve, the type layer and stress (R-2034); §7 is the as-built
  - docs/blueprints/Architect_Question_89_Suppress_And_Close.md — OWNS the component these widen (R-269 (backend))
-->

# NOTE behaviors → backend — AQ89's `CombatTemperament` widened (cross-lane, `CE-2269` / `CE-2270`)

*FYI, nothing to do unless something below collides with work in flight.*

| file (backend's) | what behaviors changed |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/CombatTemperament.cs` | ➕ `Nerve`, `Resolve` (Set bits 4 / 8) and a TYPE layer (`TypeRiskAcceptance` … `TypeResolve`, `TypeSet`), appended after `Set`; `TemperamentOf` reads authored › type › default. ⭐ `Willingness` / `Willing` are unchanged in formula — they now also see a type's value (an insurgent's 0.3) |
| `Hrot/Engine/Hrot.EcsNode/Serializers/CombatTemperamentTranslator.cs` | ➕ `nerve` / `resolve`; an inject REPLACES only the authored layer (the TKB's type fields survive); the type fields are never extracted |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Translators/BehaviorTkbTranslator.cs` | consumes the new `TemperamentDto` (`AI.Temperament`) and stamps it at creation |
| `Hrot/Engine/Hrot.Core/Tkb/UrbanCombatTkbCatalog.cs` | the Insurgent (2003) carries `TemperamentDto.Insurgent()` — obedience 0.3 ± 0.15 per man ⇒ ⚠ an insurgent ORDERED to assault is no longer willing to close (AQ89's 0.6) |
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | `Stress = 421` (behaviors' block) |
