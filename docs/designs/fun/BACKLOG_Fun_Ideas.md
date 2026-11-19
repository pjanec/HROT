<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: the whole file — the fun-pack IDEA backlog (not a design: each idea becomes one when it is picked)
stale-below: nothing
related-designs:
  - ../weapons/DESIGN_Flame_Weapons.md — the shared flame (S-1, S-2) the dragon's breath (F-11) reuses
  - ../weapons/DESIGN_Bows_And_Crossbows.md — F-1 / F-2 / F-8's engine half: arrows in flight, stuck in surfaces and bodies, mounted archers, the archery target (CE-4649)
  - ../air-attack/DESIGN_Air_Combat.md — the jet dogfight: its pilot (mode 8, GCAS), MissileDefence and AirCombat behaviour are what F-25 (WWI) reuses with biplane numbers
  - ../../blueprints/RULINGS.md — R-4281: the fun pack is separated by CONVENTION (`scenarios/fun/`, its own content folders; shared engine pieces stay shared)
  - ../animals/DESIGN_Animals.md — the herds, horses, dogs and birds most games here ride on
  - ../vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md — ramming and the vehicle impact the derby and the escape bus need
  - ../../DESIGN_Scenario_Regression.md — every game is also a feature-test scenario (R-4277)
-->

# The FUN PACK — the idea backlog

🔒 User, `2026-11-16`: *"I would like to build it to the engine as a fun part … Don't hesitate to suggest something. It could be crazy, but funny."* ·
*"Ideally, it could be something that multiplayer can compete with each other."* · *"Please record all the ideas."*

⭐ Separated by convention (R-4281): scenarios in `scenarios/fun/<theme>/`, fun content in its own folders; a SHARED engine piece (a flame
effect, an arrow's stuck state, scoring) stays shared. ⭐ Every game is also a machine-checked feature-test scenario (R-4277). ⛔ No recreation
of a specific existing game (its name, characters or art) — our own take on the genre.

| # | idea | play | the engine already has | new | tracker | the user said |
|---|---|---|---|---|---|---|
| F-1 | **Arrows** seen in flight, stuck in walls and in bodies | — | rounds are real entities with arcs, drawn per munition; lying rounds; hit location per body part | bow + arrow types, a stuck state, a stuck-in-body record, the arrow look | CE-4633 | "As in Western movies" |
| F-2 | **Wagon attacked by mounted archers**, the cavalry arrives | team | firing while riding (R-4269), horses | the wagon + horse team, circling raid play | CE-4633 | "would look nice" |
| F-3 | **Western figures**: cowboys, Native American riders, Mexican vaqueros and villagers, cavalry — with their hats | — | part-kit figures, riding pose | hats, bows, holsters, ponchos, uniforms (period-accurate, no caricature) | CE-4633 | "with their typical hats" |
| F-4 | **Bird and balloon shooting** (our own take on the genre) | competitive | flocks lift off when shot at; the anti-drone shotgun; the game score + scoreboard (CE-4231) | balloons (drift, pop), points per target | CE-4633 | "this one is funny" · "the balloons, they would be good" |
| F-5 | **Stampede through a city**, civilians panicking | demo | herd panic, fence breaking, crowd panic | a scenario | CE-4633 | "That's a great one" |
| F-6 | **Quick-draw duel** with a draw animation | 1 v 1 | weapon aim time | a revolver, the draw animation, the early-fire foul | CE-4633 | "funny if we have the right models" |
| F-7 | **Dynamite** with a burning fuse and sparks; hot-potato mode | competitive | throws, pick-up of lying rounds, timed charges | the munition, fuse sparks + fizz | CE-4633 | "a good idea" |
| F-8 | **Archery contest** | competitive | (F-1) | ring targets reading the hit ring | CE-4633 | "nice" |
| F-9 | **Cattle drive** and **bull riding** | competitive | animals flee what approaches (herding emerges) | a pen score; a bucking bull you ride | CE-4633 | "perfect" · "riding like a bull" |
| F-10 | **Cattle racing with attacks on the others** (thrown items, a lasso to slow a rival) | competitive | riding, throws, rules with checkpoints | lap / checkpoint score, a hit slows the rider | CE-4639 | "there would need to be some attacks on the other players" |
| F-11 | **Zeppelins** and **fire-breathing dragons**, rideable | demo / team | manual flight of a hovering body, bird flight, firing while riding | the zeppelin, the dragon, flame breath (the shared flame effect, CE-4635) | CE-4634 | "And the player could ride them" |
| F-12 | **UFOs** — a funny AIR TARGET, and abducting cows | competitive / demo | air defence and shooting at flyers, aircraft carrying passengers | the saucer, the beam, an animal as a passenger | CE-4641 | "UFOs can be included as a target" |
| F-13 | **Giant monster** in the city (our own creature, kaiju-style) | demo | whole-building collapse, breaking walls by contact, crowd panic | a huge walker that breaks walls | CE-4638 | "not bad at all" |
| F-14 | **Zombies**: hunt, survival waves, infection tag, the escape bus, the military vs the horde, a zombie farm | all | civilian brain, crowds, melee, contagion, hit location | the fearless biter, head-only kill, side change when bitten | CE-4636 | "Maybe some zombie hunt" |
| F-15 | **Demolition derby** with AI drivers | competitive | ramming + vehicle impact (CE-4622, queued) | an AI driver play (seek, ram, avoid), last car running wins | CE-4640 | "would require some behavior for the AI players" |
| F-16 | **Tank / kart racing** through checkpoints, fences as shortcuts | competitive | possessed driving, breaking barriers, rules | a lap counter | CE-4639 | "not a bad idea" |
| F-17 | **Sheepdog trials** — the player is the dog | competitive | dogs, sheep, flight-zone herding | a timer | — | (suggested) |
| F-18 | **Artillery golf** — fewest mortar shots to the hole | competitive | fire solutions, real arcs, adjusting fire | a distance score | — | (suggested) |
| F-19 | **Parachute accuracy** — land nearest the cross | competitive | the paratrooper drop | a steered canopy, a distance score | — | (suggested) |
| F-20 | **Paintball capture-the-flag** | team | hit location, decals, capture rules | a paint round + splat colour | — | (suggested) |
| F-21 | **Snowball fight** | team | throws, the throw arc, weather | the snowball, a hit tally | — | (suggested) |
| F-22 | **Fireworks** over the city at night | demo | arcing shells that burst in the air, night | coloured bursts | — | (suggested) |
| F-23 | **Western train robbery** | team | — | trains (CE-4632) | CE-4632 | "we are missing the train" |
| F-24 | **Power-ups** — pick-up items in the world that give a timed effect or an item: speed, a shield, extra throws, a lasso, a smoke cloud, double points, rapid fire, a med kit, ammo, planks | all games | picking up a LYING round is built and arbitrated by its owner (one grant wins — `RoundPickUp`, CE-3235); a picked item goes to the man's hands (`PersonHands.Give`); a live item burns down a timer in his hands (`HandsFuzeSystem`); macros respawn units | a `PowerUp` pickup entity (kind, value, respawn time), a per-unit `ActiveEffects` record (a few timed slots, recorded), each effect read at an existing seam (speed, aim time, the unseat rule, the score), the HUD icon | CE-4642 | "Racing, and maybe not just that, requires some concept of picking power-ups." |
| F-25 | **WWI air combat** — biplanes and triplanes, a machine gun fixed in front of the pilot, occasional jams to clear, bombs and grenades thrown by hand over the side; dogfights between players | competitive / team | a fixed-wing flight model with a MOUSE-AIM manual pilot (`FixedWingModel`, Air Ops slice 6); forward-fixed guns on a plane (gun runs); level bombing with the release inheriting the plane's velocity (`BombBallistics`); ground air defence engages aircraft (`EngageAircraft`); weapons can be BROKEN (`WeaponSystems.IsBroken`) | WWI aircraft types (slow, tight-turning), the plane kits, a temporary JAM + an unjam action, a throw from a moving seat, an air-to-air AI play for AI opponents (none today), period targets (observation balloons, trenches) | CE-4643 | "what about the World War I air combat?" |

## Serious spin-offs (shared engine, NOT fun content)

| # | idea | tracker |
|---|---|---|
| S-1 | the **flamethrower** (man-portable, vehicle-mounted) — the flame area effect F-11 shares | CE-4635 |
| S-2 | the **napalm bomb** — a burning ground area with its visual and its damage | CE-4637 |
| S-3 | **military convoys in SUMO traffic** until they must act | CE-4631 |
| S-4 | **trains** | CE-4632 |
| S-5 | **weapon stoppages and the immediate-action drill** — a weapon jams now and then (by rounds fired, seeded) and is cleared by an action (a player key; the AI after its drill time): realistic for training, and F-25's jam | CE-4644 |
