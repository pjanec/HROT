<!--STATUS
state: LIVE
updated: 2026-10-09
current-answer: the whole file (a usage guide, not a design)
stale-below: nothing
known-rot: none yet — measured from the gizmo sources 2026-10-09 (37 [GizmoProjector] classes); a gizmo added later is not listed until this is updated
related-designs:
  - DESIGN_Terrain_Combat_Tuning.md — OWNS the map debug layers (§5), the recorded debug traces (§5a), family scope and pins (§5b), the navmesh layer (§5c)
  - DESIGN_Uniform_Gizmo_Membership.md — OWNS which hosts draw which gizmo (one projector, every map host)
  - DESIGN_Ai_Action_Status_Gizmo.md — OWNS the Actions family (why a unit is not firing)
  - DESIGN_Peek_And_Fire.md — OWNS §9.8 cover and the EQS verdict, and the PeekAndFire gizmo (§7 P-6)
  - DESIGN_Building_Interiors.md — OWNS the Cover layer's database (§3l C7)
-->

# Debug gizmos — how to turn them on and how to read them

## 1. Three switches

| switch | where | what it does |
|---|---|---|
| **Layers** | main menu **View → Tactical Map Layers…** | turns a whole layer on or off for the map (table below) |
| **Selected only** | same panel, the `…SelectedOnly` boxes | per gizmo **family**: draw for every unit, or only for selected and pinned units |
| **Pin** | right-click a unit → **Pin gizmos** → a family, *Pin all*, *Unpin all* | that unit keeps drawing the family even when not selected |

⭐ A unit's **sensors are child entities** — selecting or pinning the unit is enough; their gizmos follow it.

| layer | default | gizmos on it |
|---|---|---|
| Entities | on | unit symbols, labels, health bars, selection rings, heading, **EQS** lines and verdict dots |
| Perception | on | vision cones, contact lines |
| AiHelpers | on | navigation-target arrows, danger areas |
| FireTraces | on | rounds fired |
| Doors | on | door leaves by state |
| Paths | on | planned path, authored route |
| Blast | on | burst radii and fragment rays |
| Hearing | on | sound rings, heard estimates |
| Roads | on | road network |
| **Cover** | **off** | cover points (hundreds in a town) |
| **Navmesh** | **off** | navmesh polygons (Infantry by default; the panel picks Vehicle or both) |

| family *(the "Selected only" boxes)* | default scope |
|---|---|
| Path · Utility · Squad · Actions · **EQS** | selected and pinned only |
| Perception · Contacts | every unit |

## 2. Which gizmo answers my question

| question | turn on / select | read |
|---|---|---|
| **Why is it not firing?** | select the unit (Actions) | the **action status** lines under it — amber `W hold: not seen`, `W aiming 0.4/0.8s`, `W reloading` say exactly what the weapon waits for |
| …does it even know the target? | Perception layer, Contacts family | a **red line** to each remembered target, fading with age; a dashed circle = how unsure the position is. No line ⇒ it has no contact |
| …can it see it? | Perception layer | the **cyan cone** is its field of view. ⚠ The cone ignores walls — for "is the line blocked" use `GET /perception/los` (runbook) |
| **Where will it take cover — and is that spot safe?** | select the unit (EQS) + Cover layer | EQS: a dot per point its cover sensor considered — **green = kept** (hidden from the threat), **red = rejected** (seen); lines + `#1 (0.82)` = the top answers and scores. Cover layer = every point that exists |
| **What is PeekAndFire doing?** | select the unit (Actions) | `PF hidden 2.1s` above it = phase and timer; green ring = hide point, blue = peek point; a ring per remembered window, yellow → red with heat, `h1.7 ×3` = heat and uses; solid red = burned |
| **Where is it going and why that way?** | select (Path) + Paths layer | blue polyline = planned path; purple square = a door on it; orange dot = progress; magenta dot = the look-ahead it steers at. Orange/yellow route with dots = the authored route it follows. Blue arrow (AiHelpers) = its navigation target |
| …why can it not get there? | Navmesh layer | no polygon under a spot = not walkable; a doorway polygon takes its door's colour |
| **What happened to the rounds?** | FireTraces layer | muzzle → end of each round: **red** hit · **orange** stopped by terrain · **grey** expired · **yellow** in flight; green tick = a wall it went through, orange ✕ = what stopped it |
| **What did the grenade do?** | Blast layer | orange dashed ring = fragment radius, red = blast radius; a ray to every body point rated — green got through, orange less, ✕ at the obstacle; exposure and damage per target |
| **What did it hear?** | Hearing layer | rings from sound sources (blue moving, yellow shot, red-orange detonation); from a listener, a dashed line to where it *thinks* the sound was, with an uncertainty circle |
| **Why does the squad do that?** | select the commander (Squad) | a line to each member coloured by element, `E#R#` = element/role, the phase at the commander, shared contacts (magenta), the danger area being crossed (orange) |
| **Why did the utility AI pick that?** | select (Utility) | one line per decision at the unit: decision · winner · margin over the runner-up |
| **Is the route dangerous?** | AiHelpers layer | danger boxes yellow (low) → red (high threat), labelled with kind and rating |
| **Is the terrain / door / zone right?** | Doors, Roads layers | doors: **grey** closed · **green** open (swung 90°) · **purple** locked · **red ✕** destroyed. Roads: grey band = lane width, yellow centre line, blue nodes. A terrain zone's stroke shows whether its terrain is loaded **on this node** |

## 3. Colours that mean the same everywhere

| colour | meaning |
|---|---|
| green | allowed / passed / safe / open |
| red | blocked / hit / rejected / burned / destroyed |
| orange / amber | stopped, held, waiting (and the progress point) |
| grey | finished, expired, closed |
| lighter green *(Cover layer)* | cover beside a **standing vehicle** — live, gone when it drives off |
| blue *(Cover layer)* | a window firing position (dot size = stance: small prone, mid crouch, large stand) |

## 4. Things that trip people up

| symptom | reason |
|---|---|
| a family draws nothing | it is "selected only" and the unit is neither selected nor pinned — or its layer is off |
| EQS dots all green | the sensor has no fresh threat, so the hidden-from-threat filter does not run |
| Navmesh layer empty on CGF / IG / Replay | those hosts bake no navmesh — look on SimHost or the Editor |
| a static obstacle missing from Cover in the Replay Browser | the browser does not bake obstacles (`DESIGN_Peek_And_Fire.md` §9.5) |
| fire traces, bursts, hearing, paths, doors, danger areas, PeekAndFire rings **replay** | they draw from recorded components — a seek shows them again |
| vision cone says "visible" but the unit does not fire | the cone ignores walls and body stance; read the action status line instead |

*Also present, rarely needed:* entity heading (orange), health bar (green/yellow/red), selection ring (green primary, yellow others),
spatial-grid cells (a settings toggle), stance text (`Prone` / `→ Prone`, nothing when standing), and the hill-attack demo's lines
(green baseline, blue firing line).
