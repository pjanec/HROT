# Moved — `PresentationTkbTranslator` is shared (UXI-23 `S1`, 2026-08-28)

Moved from `Hrot.IG/Translators/` to **`Hrot.Core/MapDefinitions/Tkb/`**, namespace
`Hrot.IG.Translators` → **`Hrot.Map.Definitions.Tkb`** — beside `VisualData`, the component
it writes, and in the same assembly as the TKB definitions it reads.

It only ever needed `Fdp.Core`, `Fdp.Interfaces`, `VisualDefinitionDto` (`Fdp.Toolkits`) and
`VisualData` (`Hrot.Core`) — nothing IG-specific. Living in `Hrot.IG` made `VisualData` an
IG-private product: SimHost's six-translator list had no presentation translator, so its
TKB-built entities carried no `VisualData`, and the shared entity gizmos that project from
it found nothing to draw.

⚠ It early-returns when `VisualData` is unregistered, so a host that adds the translator but
forgets the component registration fails **silently**.

See `docs/UX/UX_Feature_Map_Parity.md` §3.0a and §3.9a.
