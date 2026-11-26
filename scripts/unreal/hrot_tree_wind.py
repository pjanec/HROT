"""hrot_tree_wind.py - calm the beech trees' wind (the user live on Stechovice, 2026-11-26: "they are bending way too much like if there was
extremely strong wind").

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi        HROT_TREE_WIND = <factor, default 0.3>  HROT_TREE_WIND_OUT = <json>

The EuropeanBeech pack animates its trees in the material (MAI_EuropeanBeech: "Wind Animation Strength", "Wind Noise Strength"; no wind actor).
Every material instance under its Simplewind folder gets both set to the PARENT's default x factor - absolute, so a re-run never compounds.
LFS assets come back READ-ONLY: clear the flag on the instances first. The instances are in the client's terrain kit: a change needs a client repackage.
"""
import json, os, traceback
import unreal

MEL = unreal.MaterialEditingLibrary
FACTOR = float(os.environ.get("HROT_TREE_WIND", "0.3"))
OUT = os.environ.get("HROT_TREE_WIND_OUT")
PARAMS = ("Wind Animation Strength", "Wind Noise Strength")
R = {"factor": FACTOR, "instances": {}, "log": []}
try:
    parent = unreal.load_asset("/Game/EuropeanBeech/Materials/Simplewind/MAI_EuropeanBeech")
    base = {p: MEL.get_material_default_scalar_parameter_value(parent, p) for p in PARAMS}
    R["parent_defaults"] = base
    for path in unreal.EditorAssetLibrary.list_assets("/Game/EuropeanBeech/Materials/Simplewind", recursive=False):
        mi = unreal.load_asset(path)
        if not isinstance(mi, unreal.MaterialInstanceConstant): continue
        names = [str(n) for n in MEL.get_scalar_parameter_names(mi)]
        row = {}
        for p in PARAMS:
            if p not in names: continue
            before = MEL.get_material_instance_scalar_parameter_value(mi, p)
            MEL.set_material_instance_scalar_parameter_value(mi, p, base[p] * FACTOR)
            row[p] = [before, MEL.get_material_instance_scalar_parameter_value(mi, p)]
        if row:
            MEL.update_material_instance(mi)
            row["saved"] = bool(unreal.EditorAssetLibrary.save_loaded_asset(mi, False))
            R["instances"][path.split(".")[0]] = row
except Exception:
    R["log"].append(traceback.format_exc())
unreal.log_warning("[HROT-TREE-WIND] " + json.dumps(R))
if OUT: json.dump(R, open(OUT, "w", encoding="utf-8"), indent=1)
