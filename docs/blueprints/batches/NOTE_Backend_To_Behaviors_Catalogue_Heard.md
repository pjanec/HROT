# NOTE — backend → behaviors: the built-in catalogue is now HEARD *(2026-10-20, CE-3281)*

**What changed** ([`CE-3281`](../Blueprint_Issues_Tracker.md) (the built-in military catalogue was silent to AI ears), user-approved;
design: [Sensing §8.6](../../DESIGN_Thermal_And_Acoustic_Sensing.md)):

| types | now heard |
|---|---|
| M1, T-72 | tracks 300 m · main gun 12 km (family Gun), burst 12 km |
| Bradley | tracks 300 m · 25 mm 1.5 km (family AaGun), burst 600 m |
| HMMWV | engine 100 m (its M2 was already heard, as a weapon system) |
| UH-60, AH-64 | rotor 3 km · AH-64 Hydra 800 m (Launcher), burst 800 m |
| F-16, A-10, C-130 | ⚠ class **Rotor** (an aircraft overhead — the class list is full) 15 km · A-10 GAU-8 1.5 km, its Mk-82 burst 10 km |
| persons 200–211 (rifleman, officer, MANPADS, paratrooper, SF, pilot, M249, PKM, sniper, marksmen) | footsteps 25 m · rifle 400 m (Rifle), LMG 600 m |
| security 1201–1203 (CE-3223) | baton round 200 m · stun / gas grenade burst 400 m |

**What it means for behaviours:** before, every one of these moved and fired UNHEARD by AI ears. Now a soldier hears them — a heard shot drives
the CE-2106 reactions, and a jet or helicopter overhead is heard as `Rotor` (`OfClass` danger 1). A scenario that relied on these types being
silent (a tank firing unheard, an air strike nobody hears coming) will play differently. Measured: the backend suites stay green (see the commit);
the live demos were not re-run here.

**Not changed:** SUMO traffic cars (`TrafficCar` 8851) stay silent — confirmed by the user (`2026-10-20`: *"sumo not heard ok"*), as is a jet heard as `Rotor` (*"Jets ok"*).
