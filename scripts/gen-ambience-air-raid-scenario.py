#!/usr/bin/env python3
"""CE-11034 (docs/designs/audio/DESIGN_Ambience.md 5 slice 1) - the feature scenario `ambience-air-raid`: a positionless battle bed from the start, and a
trigger at 30 s that raises an air-raid siren at a town for 60 s. Writes scenarios/<category>/ambience-air-raid/scenario.json (checks.json and golden.json
are authored beside it). No units: ambience layers are entities of their own, so the scenario judges them by their Ambience component."""
import sys
from scenario_builder import Scn
import scenario_builder
scenario_builder.ROOT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/HROT"

TOWN = (300, 300, 0)
s = Scn("ambience-air-raid", "a1103400")

# an AUTHORED layer: a scenario entity (TKB type 8841) with no position = heard everywhere, no end (Until 0)
g = s.guid()
s.ents[g] = {"TkbIdentity": {"TkbType": 8841}, "EntityInfo": {"Name": "Distant War"},
             "Ambience": {"Kind": "battle.distant", "Intensity": 0.6, "Radius": 0, "Until": 0}}

# the TRIGGER: at 30 s of scenario time, SetAmbience raises the siren AT THE TOWN for 60 s (SR-93: the same verb a behaviour or a director uses)
g = s.guid()
s.ents[g] = {"TkbIdentity": {"TkbType": 8861}, "EntityInfo": {"Name": "Air raid at 30 s", "ForceId": "Neutral"},
             "Behavior": {"Name": "Trigger",
                          "Params": {"common": {"title": "Air raid at 30 s"}, "when": {"$type": "ScenarioTime", "fromSeconds": 30},
                                     "do": [{"act": "SetAmbience", "args": {"kind": "alarm.airraid", "intensity": 1.0, "place": list(TOWN), "radius": 800, "duration": 60}}]},
                          "Origin": "Superior"}}

s.write("ambience-air-raid", "basic-desert",
        "What: CE-11034 (R-11014, DESIGN_Ambience slice 1) - on basic-desert, no units: ambience LAYERS, entities a renderer plays by KIND (a dotted name, never a sample). "
        "An authored layer 'Distant War' (kind battle.distant, intensity 0.6) has no position, so it is heard everywhere, from the start and for ever. A trigger "
        "'Air raid at 30 s' runs the SetAmbience action at 30 s: it asks the creation path for a layer 'Ambience alarm.airraid' (intensity 1, at the town (300, 300), "
        "heard within 800 m, 60 s), which ends by itself at its sim time. Judge (100 s): (1) from the start the Distant War sounds; (2) no siren before 30 s; (3) at "
        "30 s the siren appears, at the town, with its radius and an end 60 s after it began; (4) it is still sounding at 80 s; (5) by 95 s it has ended and is gone; "
        "(6) the Distant War is still sounding at the end. Check: scripts/scenario-regression.sh ambience-air-raid.")
