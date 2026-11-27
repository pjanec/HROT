<!--STATUS
state: LIVE — a DRAFT (ideas recorded, not a design; no INVENTORY, no UML, nothing ruled)
updated: 2026-11-16
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../devices/DESIGN_Simulated_Devices.md — SIMULATED DEVICES: a trainee's equipment as entities (carried in this station / scope / view), a generic I/O model with adapters, a recorded state block
  - ../air-attack/DESIGN_Air_Combat.md — the fighter's gun jam is a malfunction (this draft), not a pilot lapse (AC-12)
  - ../../DESIGN_Node_Extensions.md — OWNS node extensions (CE-4293, R-4158): the door a physical device's driver comes through
  - ../../reference/NED_DDS_Protocol.md — OWNS the wire as an outsider sees it; an external gateway (the DIS/HLA one) is a node like any other
  - ../../DESIGN_Entity_Control.md — OWNS stations and possession: a station already drives a possessed weapon
  - ../fun/BACKLOG_Fun_Ideas.md — S-5 / F-25: the WWI gun's jam is this draft's first, simplest user
-->

# DRAFT — weapon malfunctions, and physical devices in the loop (CE-4644, CE-4645)

🔒 User, `2026-11-16`, verbatim: *"Weapon malfunction is a very important concept. We might need to introduce that into the serious part as well.
Not sure how, because there are many kinds of malfunctions that can happen, and the scenario for instructor can trigger one of those. While
sometimes the trainee can fix those by applying a correct procedure. But it's all usually works on some physical replicas of the weapons, so it's not
exactly doable in our virtual only world easily or effectively. So I do not think we could finish it, but at least we can design the infrastructure
for that. Because in the future, in that engine, I would definitely like to be able to add some module that simulates some real weapon, or
observation device. Or some weapon system which is operated physically by the player, by the trainee, but has some consequences in the virtual
world, like firing a weapon"* · *"Just please record the ideas for now … not really a detailed design, but just some draft."*

⚠ **A draft.** Before it becomes a design: an INVENTORY (graph + grep), the UML, the decisions with leans, the user's rulings.

## 1. Weapon malfunctions (CE-4644) — the engine owns the STATE and the JUDGEMENT

| piece | the idea |
|---|---|
| **kinds as DATA** | a catalogue per weapon type — failure to feed, double feed, stovepipe, failure to extract, misfire, hangfire, runaway gun, overheating, a dead optic or sight, a radio / battery failure on a device… Each kind: its EFFECT (cannot fire · fires late · fires uncontrolled · aims wrong), its natural CHANCE (per round fired, seeded; heat and dirt may raise it), its CLEARING PROCEDURE |
| **the procedure** | ordered STEPS (e.g. tap → rack → assess → fire; a double feed: lock the action → magazine out → rack ×3 → magazine in → rack), each with a time limit; a wrong or skipped step can make it worse or not clear it |
| **the state** | a malfunction on the WEAPON ENTITY (weapons are entities), recorded and replicated — a temporary sibling of the permanent "broken" (`WeaponSystems.IsBroken`) |
| **who CAUSES it** | chance · the INSTRUCTOR (an inject from the scenario rules — *"a stovepipe on the trainee's rifle at the next shot"* — or live from the instructor's station / the HTTP API) · a physical device (§2) |
| **who CLEARS it** | a virtual trainee (keys or a radial menu for the steps) · the AI (its drill time per kind) · a physical replica (§2 reports the steps it sensed) — ⭐ the engine judges every one against the SAME procedure |
| **the review** | time to clear, right / wrong / skipped steps, shots lost — in the after-action review and, in games, the scoreboard |
| **first user** | the WWI machine gun's jam (fun pack F-25) — one kind, one step ("unjam") |

## 2. Physical devices in the loop (CE-4645) — a DRIVER plugs into existing seams

| seam (exists) | a device uses it to |
|---|---|
| a **node extension** (`extensions/<name>/`, CE-4293) | load its own driver DLL into every node and talk to the hardware (USB, serial, network) — the engine never knows the device |
| a **station driving a possessed weapon** (the ZU-23, CE-2317) | send the device's AIM and TRIGGER as the station's input ⇒ the shot is a REAL simulated round (ballistics, hits, damage) |
| an **external gateway node** (the DIS/HLA gateway) | let a commercial trainer that speaks only DIS / HLA fire into the world |
| the **malfunctions** (§1) | report the replica's procedure steps (bolt racked, magazine out); be told a fault was injected (a replica that can lock its own trigger) |
| **observation devices** | send the device's POINTING; the engine returns what it would see (targets, a range reading) or renders the view — ⚠ a live video feed to a real eyepiece is the hardest part, left to the device module |

⭐ The proven practice this follows: replica weapons in military trainers talk through a driver that sends aim / trigger events, and whole
trainers interoperate through a standard protocol (DIS / HLA). Here: the node extension + the station input; the existing gateway for DIS trainers.
⛔ Nothing is built for §2 until a real device arrives.

## 3. Open questions for when we return

- the catalogue's home: per weapon type in the TKB, or a separate malfunction table keyed by weapon family
- how a virtual trainee performs steps: keys, a radial menu, or a timed "hold to clear"
- what a replica reports: raw sensor events (the engine judges) or finished steps (the device judges) — the lean is raw events, so every device is judged the same way
- the AI's drill: one time per kind, or the steps played with their times (visible in the replay)
