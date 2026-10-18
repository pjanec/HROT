# NOTE — coordinator → behaviors (civilians): a converted SUMO pedestrian calms down in ~20 s

**From:** coordinator lane · **Date:** 2026-10-18 · **Tracker:** [CE-4028](../Blueprint_Issues_Tracker.md) · **Design:** [AQ93 §15.7](../Architect_Question_93_Sumo_Traffic_Node.md), [DESIGN_Civilians_And_Crowd_Control.md](../../DESIGN_Civilians_And_Crowd_Control.md) CV-12 / CIV-7

## What is built (coordinator, CE-4024)

A SUMO pedestrian frightened by a shot or blast leaves SUMO and is created on the brain node as one of your civilians
(1101–1104), keeping his network id, born with the creation attributes `Fear = 1` and `FrightFrom = <the shot's origin>`
(`CivilianState`, new attributes `AttributeIds` 30–33). Your `CivilianHsm` takes it from there.

## What we measured (five-process cluster, 2026-10-18)

| time after birth | `Fear` | `Mode` |
|---|---|---|
| ~1 s | 0.97 | Panic |
| ~12 s | 0.39 | Alarmed |
| ~60 s | 0 | Routine |

Nothing frightens him again (he heard nothing himself: `HeardAt` 0), so `CivilianFearSystem`'s fade (0.05/s, CE-2235) brings him
back to his routine.

## The question for you

AQ93 §8.2 and R-4032/R-4035 intend a panicked pedestrian to **run to an openable building and be deleted after a while**, never to
resume a routine. Is the fade the right behaviour for a converted pedestrian, or should a civilian born frightened (or with
`HasFrightFrom`) shelter rather than calm down? Your model, your call — the coordinator changed nothing in the civilians code.
