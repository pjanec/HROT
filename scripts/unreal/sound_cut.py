"""CE-4651 (U-18n) - cut a sound library's long takes into the IG's loops and one-shots.

Reads WAVs (PCM 16/24/32, float 32; mono, stereo, ambisonic AmbiX or surround), and per recipe writes 16-bit WAVs at 48 kHz
(or the source rate when it is not a multiple of 48 kHz):

  loop    - the steadiest window of the take (lowest spread of its 0.25 s loudness), made seamless by an equal-power crossfade of
            its tail into the audio just before it; beds stereo, point sources mono
  shots   - the N loudest separate onsets, each from 10 ms before its onset until it decays 50 dB (or MaxLen), faded out

  whole   - a whole spoken line / short cue (one file, matched by its EXACT name): leading and trailing audio below -50 dB of its peak
            trimmed (30 ms margin), mono, peak-normalised; the output index is the recipe's count field (vox/<set>_NN)

  burst   - runs of N consecutive rounds (N = 2..8) of an automatic-fire take (U-19c): the onsets are found as for shots (55 ms apart at the
            least), every run of N is scored (steady spacing, a quiet lead-in, an ending that is the burst's own) and the best run per
            source file and N is cut: from 10 ms before onset 1 to where the decay after onset N falls 50 dB (or 0.6 s past it, or the next
            round), faded out over 30 ms, peak-levelled like the shots. The recipe's `src` is several substrings joined by `|`, its count the
            variants kept per N (the steadiest), its name the stem: <stem>b<N>_NN

  bed     - a whole recording (one file, matched by its EXACT name) kept whole, stereo, RMS-levelled (a loud one is held at a -0.4 dBFS peak):
            the war ambience layers (U-19d); the output index is the recipe's count field (amb/<set>_NN)

Usage: python sound_cut.py <raw root> <out root> [--set gdc|gdc23|pro|vox|burst|amb] [--only <name substring>]
The recipes are below; a recipe's `src` is a substring of the file's path under the raw root. Nothing here reads the network.
"""
import math, os, struct, sys
import numpy as np

# name, src (substring), mode, seconds (loop length or a shot's max length), channels (1 or 2), count (shots), gain target dBFS,
# [a shot's min length: it is not ended by the decay rule before that]
R = [
    # ---- ambience beds (stereo loops) -----------------------------------------------------------------------------------------
    ("amb/countryside_day", "Field Wheat Dry Sizzle Insects", "loop", 60, 2, 0, -24),
    ("amb/meadow_wind", "Meadow Open Plane Windy", "loop", 60, 2, 0, -24),
    ("amb/pasture_horses", "Ranch Horse Pasture", "loop", 60, 2, 0, -24),
    ("amb/forest_day", "Morning Small Birch Forest", "loop", 60, 2, 0, -24),
    ("amb/forest_wind", "WINDVege_Mixed Forest Wind", "loop", 60, 2, 0, -25),
    ("amb/trees_gusts", "Wind - Aspen - Gusts", "loop", 60, 2, 0, -25),
    ("amb/trees_calm", "Wind - Oak - Calm", "loop", 60, 2, 0, -27),
    ("amb/night_woods", "Nighttime-Woodlands", "loop", 60, 2, 0, -27),
    ("amb/town_night", "Suburban Residential Area Main Street", "loop", 60, 2, 0, -25),
    ("amb/town_rain_night", "Urban Side Street Empty Rain", "loop", 60, 2, 0, -25),
    ("amb/town_day", "Berlin Balcony Late Afternoon", "loop", 60, 2, 0, -24),
    ("amb/traffic_city", "HAUTEUR VILLE DOWNMIX", "loop", 60, 2, 0, -24),
    ("amb/traffic_motorway", "UK Motorway", "loop", 60, 2, 0, -25),
    ("amb/rain_heavy", "Rain, Heavy with Distant Thunders", "loop", 60, 2, 0, -22),
    ("amb/rain_light", "Rain at Night Slow Crescendo", "loop", 60, 2, 0, -25),
    ("amb/rain_gutter", "Rain, Water Gurgling and Spraying in Gutter", "loop", 60, 2, 0, -24),
    ("amb/creek", "Stefano Cremona - Rivers", "loop", 60, 2, 0, -25),
    ("amb/crowd_unrest", "SAPST01", "loop", 60, 2, 0, -24),
    ("amb/walla_inside", "INT WALLA_03", "loop", 60, 2, 0, -26),
    # ---- animals ---------------------------------------------------------------------------------------------------------------
    ("animal/horse_snort", "Snort_Uberduo_HORS_Stereo", "shots", 2.5, 1, 6, -3),
    ("animal/horse_snort_b", "Snort_Uberduo_HORS_Mono", "shots", 2.5, 1, 4, -3),
    ("animal/horse_trot", "Trot_Uberduo_HORS_Stereo", "loop", 4, 1, 0, -16),
    ("animal/pig", "Pig Squeal and Snort", "shots", 3, 1, 5, -3),
    ("animal/chicken", "Chick Squawks", "shots", 2, 1, 6, -3),
    ("animal/goose", "Goose Baby And Adult", "shots", 3, 1, 5, -3),
    ("animal/rooster", "Rooster Call", "shots", 4, 1, 3, -3),
    ("animal/raptor", "Red Kite Call", "shots", 3, 1, 5, -3),
    # ---- vehicles (mono loops, pitched by speed in the IG) ----------------------------------------------------------------------
    ("veh/tracked_run", "Bandkanon 1C - t6 - EXT - STEADY RPMS", "loop", 8, 1, 0, -16),
    ("veh/tracked_idle", "Bandkanon 1C - t1 - ONBRD - SLOW", "loop_quiet", 8, 1, 0, -18),
    ("veh/moto_idle", "Onboard, idle, Mix, Loop, Short", "loop", 3, 1, 0, -16),
    ("veh/moto_run", "Onboard, Cruising, City", "loop", 6, 1, 0, -16),
    ("veh/car_run", "Range Rover SDV8 - t3 - EXT - MEDIUM", "loop", 6, 1, 0, -16),
    ("veh/car_idle", "Range Rover SDV8 - t10 - ONBRD - CIRCLING", "loop_quiet", 6, 1, 0, -18),
    ("veh/quad_run", "Medium Steady Rpm Loop", "loop", 6, 1, 0, -16),
    ("veh/heli", "Helicopter - Military - FLY - By", "loop_loud", 6, 1, 0, -14),
    ("veh/plane_prop", "Airplane - FLY - By - Overhead", "loop_loud", 8, 1, 0, -15),
    ("veh/horn", "Man Lions Coach - VAR SFX - Horn", "shots", 2.5, 1, 4, -3),
    # ---- weapons ----------------------------------------------------------------------------------------------------------------
    ("wpn/sniper_far", "SOURCE Single Shots 30m Front", "shots", 3, 1, 6, -1, 1.5),
    ("wpn/shotgun", "Saiga 12 Cal.12 SOURCE Single Shots", "shots", 2.5, 1, 6, -1),
    ("wpn/heavy_rifle", "Lynx 50 - FIRING - Take 2 - 4m Left", "shots", 3, 1, 5, -1),
    ("wpn/pistol", "Desert Eagle - FIRING - Take 1", "shots", 2, 1, 6, -1),
    ("wpn/burst_hall", "AK5 7,62mm - FIRING - Large Hall", "shots", 3, 1, 4, -1),
    ("wpn/smg_hall", "MP5 9mm - FIRING - Large Hall", "shots", 2.5, 1, 4, -1),
    ("wpn/howitzer", "Haubits FH77 Howitzer", "shots", 6, 1, 4, -1, 3.5),
    ("wpn/rpg", "RPG - FIRING AND IMPACT", "shots", 3, 1, 2, -1, 1.5),
    ("expl/mine", "Tank Mine - EXPLOSION", "shots", 6, 1, 2, -1, 4),
    ("expl/medium", "Medium Realistic Explosion", "shots", 6, 1, 1, -1, 4),
    ("expl/car", "Designed Car Explosion", "shots", 6, 1, 1, -1, 4),
    ("expl/wood_crash", "Designed Wood Crash And Debris", "shots", 4, 1, 1, -1, 2.5),
    ("expl/debris_rattle", "Seismic_Activities", "shots", 4, 1, 6, -4),
    # ---- impacts ----------------------------------------------------------------------------------------------------------------
    ("hit/armor", "Hit Metal Armor Plate", "shots", 1.2, 1, 8, -2),
    ("hit/metal", "Metal Hit Sweeteners", "shots", 1.5, 1, 8, -2),
    ("hit/metal_debris", "Drop Fall Metal Rattle Scrap", "shots", 2.5, 1, 4, -3),
    ("hit/stone", "BluezoneCorp - Stone Impact", "shots", 1.2, 1, 8, -2),
    ("hit/wood", "InMotionAudio - Wood", "shots", 1.2, 1, 8, -2),
    ("hit/glass", "Glass Break Hit", "shots", 2.5, 1, 5, -2),
    ("hit/flesh", "Gore Splatter", "shots", 1.2, 1, 5, -4),
    ("hit/bone", "Bone Breaks Celery", "shots", 1.0, 1, 5, -4),
    # ---- the scene --------------------------------------------------------------------------------------------------------------
    ("door/slam", "Door Slams, Impacts", "shots", 2, 1, 5, -3),
    ("door/creak", "Wooden Door, Opening and Closing", "shots", 3, 1, 4, -4),
    ("scene/tree_fall", "Tree Crack and Fall", "shots", 8, 1, 1, -2, 5),
    ("scene/fire_crackle", "Fire Crackling In A Woodstove", "loop", 10, 1, 0, -18),
    ("scene/flame_roar", "Leaf Blower - ONBRD - WORK", "loop", 4, 1, 0, -14),
    ("scene/whoosh", "Distinct Whooshes", "shots", 2.5, 1, 4, -3),
    ("scene/parachute", "PARACHUTE DEPLOY", "shots", 3, 1, 2, -3),
]

# ---- the PURCHASED pack (Military Warfare SFX Pack, GfxSounds) — PRIVATE: its WAVs and cuts stay outside git, its assets go to
# /Game/Hrot/AudioPro (never published; shipped only cooked inside a packaged IG). These rows name its files, nothing more.
# `--set pro`. The same names as the free set where they mean the same thing: the IG loads AudioPro first, then Audio.
R_PRO = [
    # weapons: one shot per file (the pack's single shots), single ROUNDS cut out of the machine-gun bursts
    ("wpn/rifle", "assault rifle single shot", "shots", 1.5, 1, 12, -1),
    ("wpn/rifle_east", "AK 47 assault rifle single shot", "shots", 1.5, 1, 3, -1),
    ("wpn/rifle_suppressed", "assault rifle suppressed shot", "shots", 1.2, 1, 8, -1),
    ("wpn/pistol", "handgun single shot", "shots", 1.2, 1, 4, -1),
    ("wpn/pistol_b", "Beretta handgun shot", "shots", 1.2, 1, 4, -1),
    ("wpn/pistol_far", "Pistol distant shot", "shots", 2, 1, 6, -1),
    ("wpn/sniper", "Sniper rifle single shot", "shots", 3, 1, 8, -1),
    ("wpn/mg_round", "M240B machine gun automatic fire", "shots", 0.7, 1, 12, -1),
    ("wpn/mg_heavy_round", "Heavy machine gun automatic fire short", "shots", 0.8, 1, 12, -1),
    ("wpn/autocannon_round", "Slow firing machine gun short burst", "shots", 1.0, 1, 8, -1),
    ("wpn/mg_far", "Machine gun burst distant perspective", "shots", 3, 1, 3, -1),
    ("wpn/tank_gun", "Armored tank cannon fire", "shots", 6, 1, 5, -1, 3),
    ("wpn/howitzer", "Cannon fire single shot", "shots", 6, 1, 4, -1, 3),
    ("wpn/rocket_launch", "Rocket launcher single shot", "shots", 3, 1, 4, -1, 1.5),
    ("wpn/mortar", "Mortar strike launch and impact", "shots", 6, 1, 4, -1, 3),
    ("wpn/missile_flyby", "Missile flyby", "shots", 4, 1, 7, -2, 2),
    ("wpn/flame_burst", "Flamethrower burst", "shots", 4, 1, 5, -2, 1.5),
    ("scene/flame_roar", "Flamethrower burst", "loop", 2.5, 1, 0, -14),
    # rounds arriving
    ("hit/body", "Bullet body hit", "shots", 1, 1, 5, -3),
    ("hit/head", "Bullet headshot impact", "shots", 1, 1, 5, -3),
    ("hit/flesh", "Bullet body hit", "shots", 1, 1, 5, -3),
    ("hit/metal", "Bullet metal hit", "shots", 1, 1, 5, -2),
    ("hit/dirt", "Bullet hits on ground dirt", "shots", 1.2, 1, 6, -3),
    ("hit/armor", "Tank armor hit by enemy fire", "shots", 2.5, 1, 5, -1),
    ("crack/bullet", "BLLTBy_Bullet pass by", "shots", 1, 1, 6, -3),
    ("crack/sniper", "Sniper bullet pass by", "shots", 1.5, 1, 3, -3),
    # explosions by size
    ("expl/grenade", "Grenade explosion with debris", "shots", 5, 1, 2, -1, 2.5),
    ("expl/grenade_b", "Grenade blast with flying debris", "shots", 5, 1, 4, -1, 2.5),
    ("expl/medium", "Large grenade explosion close perspective", "shots", 6, 1, 3, -1, 3),
    ("expl/large", "Large grenade explosion with debris", "shots", 7, 1, 2, -1, 3.5),
    ("expl/mine", "Anti personnel mine explosion", "shots", 6, 1, 6, -1, 3),
    ("expl/c4", "C4 detonation explosion", "shots", 7, 1, 2, -1, 3.5),
    ("expl/wood_crash", "Small explosion wooden structure", "shots", 6, 1, 2, -1, 3),
    ("expl/car", "Large grenade explosion glass shattering", "shots", 7, 1, 1, -1, 3.5),
    ("expl/distant", "Distant bomb explosion", "shots", 7, 1, 5, -2, 3.5),
    ("expl/smoke", "Smoke grenade explosion, air hiss", "shots", 5, 1, 4, -3, 2),
    # vehicles (loops for engines, one-shots for the rest)
    ("veh/jeep_idle", "Military jeep engine idle loop", "loopfile", 0, 1, 0, -18),
    ("veh/jeep_run", "Military jeep on the road loop", "loopfile", 0, 1, 0, -16),
    ("veh/truck_idle", "Military truck engine steady idle loop", "loopfile", 0, 1, 0, -18),
    ("veh/truck_run", "Military truck engine steady running loop", "loopfile", 0, 1, 0, -16),
    ("veh/tracked_run", "Tank moving mechanical noise", "loop", 8, 1, 0, -15),
    ("veh/tracked_idle", "Tank moving interior perspective", "loop_quiet", 8, 1, 0, -18),
    ("veh/tank_turret", "Tank turret moving or turning", "shots", 3, 1, 6, -6, 1.5),
    ("veh/heli_idle", "Military helicopter idle engine loop", "loopfile", 0, 1, 0, -16),
    ("veh/heli", "Military helicopter flying forward loop", "loopfile", 0, 1, 0, -14),
    ("veh/jet_idle", "Military aircraft idle engine loop", "loopfile", 0, 1, 0, -16),
    ("veh/jet_pass", "Military aircraft fast pass by", "shots", 6, 1, 5, -1, 4),
    ("veh/truck_horn", "Military truck horn honk", "shots", 2.5, 1, 2, -3),
    # people
    ("foot/dirt", "single footstep on dirt", "shots", 0.6, 1, 4, -6),
    ("foot/mud", "single footstep on mud", "shots", 0.6, 1, 4, -6),
    ("foot/sand", "single footstep on sand", "shots", 0.6, 1, 4, -6),
    ("foot/street", "single footstep on rough street", "shots", 0.6, 1, 4, -6),
    ("foot/metal", "single footstep on metal platform", "shots", 0.6, 1, 4, -6),
    ("foot/glass", "single footstep on broken glass", "shots", 0.8, 1, 4, -6),
    ("body/fall_concrete", "Human body fall on concrete", "shots", 2, 1, 4, -4, 0.8),
    ("body/fall_dirt", "Human body fall on dirt grass", "shots", 2, 1, 3, -4, 0.8),
    ("body/fall_wood", "Human body fall on wood floor", "shots", 2, 1, 2, -4, 0.8),
    ("hit/bone", "Bone snap crack", "shots", 1, 1, 4, -4),
]

# ---- the PURCHASED pack's voice lines (U-19b, docs/designs/IG/DESIGN_IG_Squad_Radio.md §2) - PRIVATE like R_PRO. `--set vox`.
# whole-line recipes: (name, exact file stem, "whole", 0, 1, output index, -3). The synonyms of a set are its variants _01.._NN.
_VOX_PREFIX = "COMTran_Military radio voice, "
_VOX_SETS = [
    ("cmd_move", ["move out", "go", "push forward", "advancing on the enemy"]),
    ("cmd_hold", ["hold", "holding position"]),
    ("cmd_fallback", ["break contact"]),
    ("cmd_down", ["get down", "take cover"]),
    ("call_cover", ["covering fire", "cover me"]),
    ("call_suppress", ["suppressing fire"]),
    ("call_reload", ["reloading"]),
    ("call_fragout", ["grenade out"]),
    ("call_contact", ["enemy spotted", "hostiles in the area"]),
    ("call_grenade", ["incoming"]),
    ("ack", ["affirmative", "yes"]),
    ("call_clear", ["the area is clear"]),
    ("call_medic", ["need medical attention"]),
    ("rep_contact", ["enemy spotted", "target acquired"]),
    ("rep_spot", ["hostiles in the area"]),
    ("rep_spot_big", ["enemy forces approaching", "we have hostile forces incoming"]),
    ("rep_set", ["in position", "ready to move"]),
    ("rep_done", ["objective complete", "the area is clear"]),
    ("rep_fallback", ["break contact", "i'm moving to a better position for cover"]),
    ("rep_failed", ["the mission is a failure"]),
    ("rep_targetsdown", ["target neutralized", "the enemy has retreated"]),
    ("rep_losses", ["we're taking heavy losses"]),
    ("rep_status_ok", ["holding position"]),
    ("rep_advantage", ["we have the advantage"]),
    ("rep_tight", ["we're in a tight spot, requesting immediate assistance"]),
    ("rep_casevac", ["need medical attention"]),
    ("rep_fires", ["request immediate support"]),
    ("rep_backup", ["requesting backup", "need backup"]),
    ("rep_permission", ["permission to engage"]),
    ("ord_attack", ["attack", "open fire"]),
    ("ord_defend", ["defend", "we need to hold this position at all costs"]),
    ("ord_flank", ["move to flank"]),
    ("ord_move", ["move out"]),
    ("ord_standby", ["standby for orders"]),
    ("ack_order", ["affirmative", "ready to move"]),    # the receiver's answer to an order (the free-standing `ack` is "affirmative / yes")
]
_VOX_NAMES = ["Brown", "Cheen", "Cooper", "Garcia", "Johnson", "Lee", "Martinez", "Nguyen", "Patel", "Ramirez", "Taylor"]
R_VOX = [("vox/" + s, _VOX_PREFIX + t, "whole", 0, 1, i + 1, -3) for s, ts in _VOX_SETS for i, t in enumerate(ts)]
R_VOX += [("vox/sgt_" + n.lower(), _VOX_PREFIX + "this is Sergeant " + n, "whole", 0, 1, 1, -3) for n in _VOX_NAMES]
R_VOX += [("vox/over", _VOX_PREFIX + "over", "whole", 0, 1, 1, -3), ("vox/over", _VOX_PREFIX + "over 2", "whole", 0, 1, 2, -3)]
R_VOX += [("radio/beep", "COMTran_Military walkie talkie button beep" + ("" if i == 1 else " %d" % i), "whole", 0, 1, i, -3) for i in range(1, 8)]
R_VOX += [("radio/squelch", "COMTran_Military walkie talkie static squelch" + ("" if i == 1 else " %d" % i), "whole", 0, 1, i, -3) for i in range(1, 6)]
# ---- U-19d: the war ambience layers of the PURCHASED pack (docs/designs/IG/DESIGN_IG_Squad_Radio.md §6). PRIVATE like R_PRO. `--set amb`.
# (name, exact file stem, "bed", 0, 2 channels, output index, RMS dBFS). A set's recordings are its variants _01..
_A = "AMBWar_"
_B = "BOATMil_"
_AMB_SETS = [
    ("amb/war_distant", [_A + "Distant gunshots and indoor battlefield"]),
    ("amb/war_city", [_A + "City battlefield ambience"]),
    ("amb/war_night", [_A + "Nighttime battlefield with distant shots"]),
    ("amb/war_heavy", [_A + "Sniper shooting in the distance", _A + "Tank fire on the battlefield"]),   # heard 2026-11-22: the "Sniper" take is a CLOSE fight, "Tank fire" distant artillery
    ("amb/war_sniper", [_A + "Intense mid range battlefield"]),   # heard 2026-11-22: individual mid-distance shots over a far rumble - not a heavy battle
    ("amb/war_ambush", [_A + "Jungle patrol ambush ambience"]),
    ("amb/war_aftermath", [_A + "Burning house war zone ambience", _A + "Burning house war zone ambience 2", _A + "Burning house war zone ambience 3"]),
    ("amb/ev_airstrike", [_A + "Atomic bomb drop and explosion"]),   # heard 2026-11-22: a siren, a falling-bomb whistle, a distant blast = an air attack from the ground
    ("amb/pov_aircraft", [_A + "Tactical airstrike"]),               # heard: INSIDE a bomber with cockpit radio - a listener view (note category D), no kind yet
    ("amb/ev_drone", [_A + "Drone attack and explosion"]),
    ("amb/ev_rocket", [_A + "Nighttime rocket launch and explosion"]),   # the first launch + its explosion only (_SPANS): the take repeats it 3 times
    ("wpn/missile_launch", [_A + "Nighttime rocket launch and explosion"]),   # the launch alone (heard: "might be a SAM launch, which we need")
    ("amb/ev_convoy", [_A + "Armored convoy approaching"]),
    ("amb/ev_evacuation", [_A + "Military trucks evacuation ambience"]),
    ("amb/ev_evacuation_city", [_A + "Citywide evacuation"]),
    ("amb/ev_nuclear", [_A + "Atomic bomb drop and explosion"]),
    ("amb/al_airraid", [_A + "War air raid siren", _A + "War air raid siren 2", _A + "War air raid siren 3"]),
    ("amb/al_airraid_pre", [_A + "War air raid siren preliminary alert"]),   # not "... alert 2": heard as a car alarm
    ("amb/al_bell", [_A + "Emergency alarm bell ring", _A + "Emergency alarm bell ring 2", _A + "Emergency alarm bell ring 3", _A + "Emergency alarm bell ring 4"]),
    ("amb/al_intruder", [_A + "Enemy base intruder alarm", _A + "Enemy base intruder alarm 2", _A + "Enemy base intruder alarm 3"]),
    ("amb/al_emergency", [_A + "City emergency sirens and explosions"]),
    ("amb/nv_ship", [_B + "Military ship at sea ambience loop", _B + "Military ship at sea ambience loop 2", _B + "Military ship at sea ambience loop 3"]),
    ("amb/nv_warship", [_B + "Warship rumbling ambience", _B + "Warship rumbling ambience 2"]),
    ("amb/nv_underwater", [_A + "Underwater battlefield with explosions"]),
]
# a part of the take, (start s, end s, fade-out s), placed by ear 2026-11-22 (launch 0-6.3 s, its explosion 6.3-9.5 s)
_SPANS = {"amb/ev_rocket": (0.0, 11.0, 1.5), "wpn/missile_launch": (0.0, 6.3, 0.8)}
# a bed played as a positioned one-shot is MONO (a stereo source is not placed in the world) and levelled like a weapon, not a bed:
# the missile's launch (wired 2026-11-23 to ATGM / SAM / air-to-air Fire) - RMS -12 dBFS, held at the -0.4 dBFS peak when louder
_ONE_SHOT = {"wpn/missile_launch": (1, -12)}
R_AMB = [(n, s, "bed", 0, _ONE_SHOT.get(n, (2, -24))[0], i + 1, _ONE_SHOT.get(n, (2, -24))[1]) for n, ss in _AMB_SETS for i, s in enumerate(ss)]

# ---- U-19c: bursts of an exact round count. `--set burst`. (name stem, sources joined by |, "burst", tail seconds, 1, variants per N, level)
_W = "Military Warfare SFX Pack"
R_BURST = [
    ("wpn/rifle_", "HK416 assault rifle short burst|HK416 assault rifle full auto|HK416 firing multiple|M4 assault rifle short burst|M4 assault rifle full auto|"
                   "M16 assault rifle short burst|M16 assault rifle full auto|FN SCAR assault rifle short burst|FN SCAR assault rifle full auto|"
                   "Tactical assault rifle short burst|Tactical assault rifle full auto|Spec ops assault rifle short burst|Spec ops assault rifle full auto",
     "burst", 0.6, 1, 4, -1),
    ("wpn/rifle_east_", "AK 47 assault rifle full auto fire", "burst", 0.6, 1, 4, -1),   # not "AK 47 firing multiple bursts": heard 2026-11-22 as one soft round, not an AK
    ("wpn/mg_", "M240B machine gun automatic fire|AR10 machine gun firing|Modern machine gun fire", "burst", 0.6, 1, 4, -1),
    ("wpn/mg_heavy_", "Heavy machine gun automatic fire", "burst", 0.6, 1, 4, -1),
]

# ---- GDC 2023 additions (free): the animals the 2024 bundle lacks
R_GDC23 = [
    ("animal/goose_flock", "Excited gaggle vocals", "shots", 4, 1, 6, -3),
    ("animal/sheep", "Sheeps & Lamps", "shots", 3, 1, 8, -3),
    ("animal/horse_walk", "Draft Horse Walk On On Grass", "loop", 4, 1, 0, -16),
    ("animal/horse_trot", "Draft Horse Trot On Concrete", "loop", 4, 1, 0, -16),
    ("amb/crickets", "Crickets.wav", "loop", 30, 2, 0, -27),
]

OUT_RATE = 48000


# ---- reading -------------------------------------------------------------------------------------------------------------------
class Wav:
    def __init__(self, path):
        self.path = path
        with open(path, "rb") as f:
            head = f.read(12)
            if head[:4] not in (b"RIFF", b"RF64") or head[8:12] != b"WAVE": raise ValueError("not a WAV: " + path)
            fmt = None; data = None; size64 = None
            while True:
                h = f.read(8)
                if len(h) < 8: break
                cid, n = h[:4], struct.unpack("<I", h[4:])[0]
                pos = f.tell()
                if cid == b"ds64": size64 = struct.unpack("<QQQ", f.read(24))[1]
                elif cid == b"fmt ": fmt = f.read(n)
                elif cid == b"data":
                    data = (pos, size64 if (n == 0xFFFFFFFF and size64) else n)
                    break
                f.seek(pos + n + (n & 1))
        tag, ch, rate, _, align, bits = struct.unpack("<HHIIHH", fmt[:16])
        if tag == 0xFFFE: tag = struct.unpack("<H", fmt[24:26])[0]
        self.float = tag == 3
        self.ch, self.rate, self.bits, self.align = ch, rate, bits, align
        avail = os.path.getsize(path) - data[0]
        self.offset, self.frames = data[0], min(data[1], avail) // align

    def read(self, start, count):
        """frames [start, start+count) as float32 (frames, ch)"""
        count = max(0, min(count, self.frames - start))
        with open(self.path, "rb") as f:
            f.seek(self.offset + start * self.align)
            raw = f.read(count * self.align)
        b = self.bits // 8
        if self.float and b == 4: x = np.frombuffer(raw, "<f4").astype(np.float32)
        elif b == 2: x = np.frombuffer(raw, "<i2").astype(np.float32) / 32768.0
        elif b == 3:
            u = np.frombuffer(raw, np.uint8).reshape(-1, 3).astype(np.int32)
            v = u[:, 0] | (u[:, 1] << 8) | (u[:, 2] << 16)
            x = (np.where(v >= 1 << 23, v - (1 << 24), v)).astype(np.float32) / float(1 << 23)
        elif b == 4: x = np.frombuffer(raw, "<i4").astype(np.float32) / float(1 << 31)
        else: raise ValueError("bits %d" % self.bits)
        return x.reshape(-1, self.ch)


def to_channels(x, path, want):
    """downmix to `want` channels: AmbiX (W Y Z X) -> L/R = W +- Y; surround FL FR C LFE SL SR -> stereo"""
    ch = x.shape[1]
    if ch == 1: st = np.repeat(x, 2, axis=1)
    elif ch == 2: st = x
    elif "ambix" in path.lower() and ch >= 4:
        st = np.stack([x[:, 0] + 0.7 * x[:, 1], x[:, 0] - 0.7 * x[:, 1]], axis=1) * 0.7
    elif ch >= 6:
        st = np.stack([x[:, 0] + 0.7 * x[:, 2] + 0.7 * x[:, 4], x[:, 1] + 0.7 * x[:, 2] + 0.7 * x[:, 5]], axis=1) * 0.5
    else: st = x[:, :2]
    return st.mean(axis=1, keepdims=True) if want == 1 else st


def resample(x, rate):
    """integer decimation to 48 kHz (a windowed-sinc low-pass first); other rates are kept"""
    if rate == OUT_RATE or rate % OUT_RATE: return x, rate
    k = rate // OUT_RATE
    n = 64 * k + 1
    t = np.arange(n) - (n - 1) / 2
    h = np.sinc(t / k) / k * np.blackman(n)
    y = np.stack([np.convolve(x[:, c], h, mode="same")[::k] for c in range(x.shape[1])], axis=1)
    return y.astype(np.float32), OUT_RATE


def envelope(w, block):
    """0.25 s (or `block`) RMS of the mono mix over the whole file, read in chunks"""
    out = []; pos = 0; step = block * 400
    while pos < w.frames:
        x = w.read(pos, step).mean(axis=1)
        m = len(x) // block
        if m: out.append(np.sqrt(np.mean(x[: m * block].reshape(m, block) ** 2, axis=1) + 1e-12))
        pos += step
    return np.concatenate(out) if out else np.zeros(0)


def write(path, x, rate):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with open(path, "wb") as f:
        data = pcm.tobytes()
        ch = x.shape[1]
        f.write(b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, rate, rate * ch * 2, ch * 2, 16))
        f.write(b"data" + struct.pack("<I", len(data)) + data)


def db(v): return 20 * math.log10(max(v, 1e-9))


# ---- the cuts ------------------------------------------------------------------------------------------------------------------
def cut_loop(w, path, seconds, want, target, pick):
    block = w.rate // 4
    env = envelope(w, block)
    win = int(seconds * 4)
    fade_s = min(2.0, seconds * 0.15)
    pre = int(math.ceil(fade_s * 4)) + 1
    if len(env) < win + pre + 2: win = max(4, len(env) - pre - 2)
    ldb = 20 * np.log10(env + 1e-9)
    best, score = pre, None
    for s in range(pre, len(env) - win, max(1, win // 8)):
        seg = ldb[s:s + win]
        if pick == "loop_quiet": sc = np.mean(seg) + 0.5 * np.std(seg)
        elif pick == "loop_loud": sc = -np.mean(seg) + 0.5 * np.std(seg)
        else: sc = np.std(seg) + 0.15 * max(0.0, -55 - np.mean(seg)) + 0.25 * (np.max(seg) - np.mean(seg))
        if score is None or sc < score: best, score = s, sc
    start = best * block
    n = win * block
    fade = int(fade_s * w.rate)
    x = to_channels(w.read(start - fade, n + fade), path, want)
    head, body = x[:fade], x[fade:]
    # seamless: the body's last `fade` frames cross into what came just before the body's start
    t = np.linspace(0, 1, fade, dtype=np.float32)[:, None]
    body[-fade:] = body[-fade:] * np.cos(t * math.pi / 2) + head * np.sin(t * math.pi / 2)
    body, rate = resample(body, w.rate)
    rms = float(np.sqrt(np.mean(body ** 2)))
    return [body * (10 ** ((target - db(rms)) / 20))], rate, "window %.1f-%.1f s" % (start / w.rate, (start + n) / w.rate)


def cut_shots(w, path, seconds, want, count, target, min_s=0.0):
    block = max(1, w.rate // 200)                     # 5 ms
    env = envelope(w, block)
    if len(env) == 0: return [], w.rate, "empty"
    ldb = 20 * np.log10(env + 1e-9)
    floor = np.percentile(ldb, 20)
    rise = ldb[1:] - ldb[:-1]
    cand = np.where((rise > 9) & (ldb[1:] > floor + 20))[0] + 1
    gap = int(0.35 * 200)
    onsets = []
    for c in cand:
        if not onsets or c - onsets[-1] > gap: onsets.append(c)
    if not onsets: onsets = [int(np.argmax(ldb))]
    peaks = [(float(np.max(ldb[o:o + 40])), o) for o in onsets]
    peaks.sort(reverse=True)
    chosen = [o for _, o in peaks[:count]]                       # the loudest separate onsets
    outs = []
    maxb = int(seconds * 200)
    for o in sorted(chosen):
        pk = float(np.max(ldb[o:o + 40]))
        end = o + 8
        minb = max(20, int(min_s * 200))
        while end < min(len(ldb), o + maxb) and not (ldb[end] < pk - 50 and end > o + minb): end += 1
        s0 = max(0, (o - 2) * block)
        x = to_channels(w.read(s0, (end - o + 2) * block), path, want)
        f = min(len(x), int(0.05 * w.rate))
        x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)[:, None]
        x, rate = resample(x, w.rate)
        peak = float(np.max(np.abs(x)) + 1e-9)
        outs.append(x * (10 ** ((target - db(peak)) / 20)))
    return outs, OUT_RATE if w.rate % OUT_RATE == 0 else w.rate, "%d onsets, kept %d" % (len(onsets), len(outs))


def cut_whole(w, path, want, target):
    """a whole spoken line: trim the silence at both ends (below -50 dB of the peak, 30 ms margin), mono, 48 kHz, peak at `target`"""
    x = to_channels(w.read(0, w.frames), path, want)
    m = np.max(np.abs(x), axis=1)
    peak = float(np.max(m) + 1e-9)
    loud = np.where(m > peak * 10 ** (-50 / 20))[0]
    if len(loud) == 0: return [], w.rate, "silent"
    margin = int(0.03 * w.rate)
    x = x[max(0, int(loud[0]) - margin): int(loud[-1]) + 1 + margin]
    x, rate = resample(x, w.rate)
    return [x * (10 ** ((target - db(float(np.max(np.abs(x)) + 1e-9))) / 20))], rate, "trimmed to %.2f s" % (len(x) / rate)

BB = 400   # envelope blocks per second for bursts (2.5 ms): rounds 70 ms apart must not merge


def burst_period(ldb):
    """the take's cyclic rate, in envelope blocks: the strongest autocorrelation lag of the loud envelope in 55 .. 300 ms.
    A shot's own echo / action (~45-70 ms after it, 10-20 dB down) repeats at the same period, so it adds to the true lag, not a
    half one (measured 2026-11-22 against the user's count: M240B 145 ms, heavy MG 185 ms)"""
    e = 10 ** (np.maximum(ldb, np.max(ldb) - 40) / 20)
    e = e - np.mean(e)
    lags = np.arange(22, 121)
    ac = np.array([float(np.dot(e[:-l], e[l:])) / (len(e) - l) for l in lags])
    return int(lags[int(np.argmax(ac))])


def burst_onsets(ldb, period=None):
    """the rounds of an automatic-fire take: a 5 dB rise within 5 ms above the floor; two rises closer than 0.7 of the take's
    period are ONE round (the louder kept). (heard by the user 2026-11-22: cuts named 7 rounds held 3-4 - each shot's echo and
    ringing had been counted as rounds)"""
    floor = np.percentile(ldb, 20)
    rise = np.zeros_like(ldb)
    rise[2:] = ldb[2:] - ldb[:-2]
    cand = np.where((rise > 5) & (ldb > floor + 20))[0]
    gap = int(0.7 * (period or burst_period(ldb)))
    on, pk = [], []
    for c in cand:
        v = float(np.max(ldb[c:c + 12]))
        if on and c - on[-1] < gap:
            if v > pk[-1] + 3: on[-1], pk[-1] = int(c), v          # the louder attack is the round
            continue
        on.append(int(c)); pk.append(v)
    return on


def cut_bursts(w, path, tail_s, want, target, nmin=2, nmax=8, family_period=None):
    """for each N: the best run of N rounds of this file -> (N, x, rate, score, spacing ms list)"""
    block = max(1, w.rate // BB)
    env = envelope(w, block)
    if len(env) == 0: return []
    ldb = 20 * np.log10(env + 1e-9)
    period = burst_period(ldb)
    if family_period and not 0.75 * family_period <= period <= 1.35 * family_period: return []   # the take's rate is unreadable (heard 2026-11-22: such cuts held 1 round)
    on = burst_onsets(ldb, period)
    pks = [float(np.max(ldb[o:o + 12])) for o in on]
    ms = 1000.0 / BB
    out = []
    for n in range(nmin, nmax + 1):
        best = None
        for i in range(0, len(on) - n + 1):
            sp = np.diff(on[i:i + n]).astype(float)
            if np.min(sp) < 22 or np.max(sp) > 120: continue          # 55 .. 300 ms between rounds
            if min(pks[i:i + n]) < max(pks[i:i + n]) - 8: continue      # every round is loud: a bump in the echo is not one (heard 2026-11-22)
            ratio = float(np.max(sp) / np.min(sp))
            lead = (on[i] - on[i - 1]) * ms if i > 0 else 1000         # ms of quiet before the first round
            nxt = (on[i + n] - on[i + n - 1]) * ms if i + n < len(on) else 1000   # ms to the next round after the last
            score = ratio + (0.8 if lead < 300 else 0.0) + (3.0 if nxt < 300 else 0.0)   # a run cut before the next round has no tail (heard 2026-11-22: 'cut way too early') - a natural burst end wins + (0.0 if 55 <= float(np.mean(sp)) * ms <= 200 else 1.5)   # a plausible cadence
            if best is None or score < best[0]: best = (score, i, sp * ms)
        if best is None: continue
        score, i, sp = best
        o0, oN = on[i], on[i + n - 1]
        pk = float(np.max(ldb[oN:oN + 80]))
        end = oN + 16
        stop = min(len(ldb), oN + int(tail_s * BB))
        if i + n < len(on): stop = min(stop, on[i + n] - 4)
        while end < stop and not ldb[end] < pk - 50: end += 1
        s0 = max(0, (o0 - 4) * block)
        x = to_channels(w.read(s0, (end - o0 + 4) * block), path, want)
        # cut before the next round (continuous fire) => no tail of its own: borrow the take's NATURAL ending - the decay after a round
        # where the gunner stopped - from the same offset after its round, crossfaded in (heard 2026-11-22: "no tail ... cut way too early")
        ends = [j for j in range(len(on)) if (on[j + 1] if j + 1 < len(on) else len(ldb)) - on[j] >= int(0.3 * BB)]   # its last round may be quieter: the tail is level-matched below
        if i + n < len(on) and on[i + n] - oN < int(0.3 * BB) and ends:
            j = max(ends, key=lambda j: (on[j + 1] if j + 1 < len(on) else len(ldb)) - on[j])
            t0 = (on[j] + (end - oN)) * block
            lim = (on[j + 1] - 4) if j + 1 < len(on) else len(ldb)
            t1 = min(lim, on[j] + int(tail_s * BB))
            if t1 * block > t0:
                xf = min(len(x), int(0.025 * w.rate))
                tail = to_channels(w.read(t0 - xf, t1 * block - t0 + xf), path, want) * (10 ** (float(np.clip(pks[i + n - 1] - pks[j], -12, 12)) / 20))
                ramp = np.linspace(0, 1, xf, dtype=np.float32)[:, None]
                x = np.concatenate([x[:-xf], x[-xf:] * (1 - ramp) + tail[:xf] * ramp, tail[xf:]])
        f = min(len(x), int(0.03 * w.rate))
        x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)[:, None]
        x, rate = resample(x, w.rate)
        x = x * (10 ** ((target - db(float(np.max(np.abs(x)) + 1e-9))) / 20))
        out.append((n, x, rate, score, sp))
    return out


def main():
    raw, out = sys.argv[1], sys.argv[2]
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    files = []
    for d, _, fs in os.walk(raw):
        for f in fs:
            if f.lower().endswith(".wav"): files.append(os.path.join(d, f))
    total = 0
    recipes = {"gdc": R, "pro": R_PRO, "gdc23": R_GDC23, "vox": R_VOX, "burst": R_BURST, "amb": R_AMB}[sys.argv[sys.argv.index("--set") + 1] if "--set" in sys.argv else "gdc"]
    for rec in recipes:
        name, src, mode, seconds, want, count, target = rec[:7]
        min_s = rec[7] if len(rec) > 7 else 0.0
        if only and only not in name: continue
        if mode == "burst":
            alts = [a.lower() for a in src.split("|")]
            hits = sorted(p for p in files if any(a in os.path.basename(p).lower() for a in alts) and "weapons" in p.lower())
            if not hits: print("MISSING", name, "<-", src); continue
            cands = {}
            periods = []
            for p in hits:          # the gun family's rate: the median of its takes' own (a weapon fires at one rate; heard 2026-11-22)
                w = Wav(p); periods.append(burst_period(20 * np.log10(envelope(w, max(1, w.rate // BB)) + 1e-9)))
            fam = int(np.median(periods))
            print("%s family period %.0f ms over %d take(s)" % (name, fam * 1000.0 / BB, len(periods)))
            for p in hits:
                try: found = cut_bursts(Wav(p), p, seconds, want, target, family_period=fam)
                except Exception as e: print("FAILED", p, e); continue
                for n, x, rate, score, sp in found: cands.setdefault(n, []).append((score, p, x, rate, sp))
            for n in sorted(cands):
                keep = sorted(cands[n], key=lambda c: c[0])[:count]
                for k, (score, p, x, rate, sp) in enumerate(keep):
                    fn = os.path.join(out, *("%sb%d_%02d.wav" % (name, n, k + 1)).split("/"))
                    write(fn, x, rate); total += os.path.getsize(fn)
                    print("%sb%d_%02d  %5.2f s  spacing %s ms  score %.2f  <- %s" % (name, n, k + 1, len(x) / rate, " ".join("%d" % s for s in sp), score,
                          os.path.basename(p)[:46]), flush=True)
                if len(keep) < count: print("FEW %sb%d: %d variant(s) of %d sources" % (name, n, len(keep), len(hits)))
            continue
        if mode in ("whole", "bed"):   # exact: the file stem (without the pack's tail) IS the recipe's src
            hits = sorted(p for p in files if os.path.basename(p)[:-4].replace("_GfxSounds_MilitaryWarfare", "").lower() == src.lower())
        else: hits = sorted(p for p in files if src.lower() in p.lower())
        if not hits: print("MISSING", name, "<-", src); continue
        path = hits[0] if mode != "shots" else None
        try:
            if mode == "bed":
                w = Wav(hits[0])
                if name in _SPANS:
                    a, b, fo = _SPANS[name]
                    x = to_channels(w.read(int(a * w.rate), int((b - a) * w.rate)), hits[0], want)
                    f = min(len(x), int(fo * w.rate)); x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)[:, None]
                else: x = to_channels(w.read(0, w.frames), hits[0], want)
                x, rate = resample(x, w.rate)
                gain = 10 ** ((target - db(float(np.sqrt(np.mean(x ** 2))))) / 20)
                gain = min(gain, 0.955 / float(np.max(np.abs(x)) + 1e-9))   # never clipped: a loud recording is held at its peak
                outs, why = [x * gain], "%.0f s whole, rms %.1f dBFS" % (len(x) / rate, db(float(np.sqrt(np.mean((x * gain) ** 2)))))
            elif mode == "whole":
                outs, rate, why = cut_whole(Wav(hits[0]), hits[0], want, target)
            elif mode == "loopfile":   # a finished loop: converted whole (channels, rate, level)
                w = Wav(hits[0])
                x = to_channels(w.read(0, w.frames), hits[0], want)
                x, rate = resample(x, w.rate)
                rms = float(np.sqrt(np.mean(x ** 2)))
                outs, why = [x * (10 ** ((target - db(rms)) / 20))], "whole file"
            elif mode.startswith("loop"):
                w = Wav(hits[0])
                outs, rate, why = cut_loop(w, hits[0], seconds, want, target, mode)
            else:
                outs = []; why = []
                per = max(1, -(-count // len(hits)))
                for p in hits:
                    w = Wav(p)
                    o, rate, y = cut_shots(w, p, seconds, want, per, target, min_s)
                    outs += o; why.append(y)
                    if len(outs) >= count: break
                outs = outs[:count]; why = "; ".join(why)
        except Exception as e:
            print("FAILED", name, e); continue
        for i, x in enumerate(outs):
            fn = os.path.join(out, *(name + ("_%02d" % (i + 1) if mode == "shots" else "_%02d" % count if mode in ("whole", "bed") else "") + ".wav").split("/"))
            write(fn, x, rate)
            total += os.path.getsize(fn)
        src_desc = os.path.basename(hits[0])[:60]
        print("%-24s %-5s %2d file(s) %5.1f s  %s  <- %s" % (name, mode, len(outs), sum(len(x) for x in outs) / rate, why, src_desc), flush=True)
    print("total %.0f MB" % (total / 1e6))


if __name__ == "__main__":
    main()
