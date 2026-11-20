"""hrot_paint.py — runs in the Unreal editor COMMANDLET (CE-4651, U-17d): a plain paint for meshes a pack ships UNTEXTURED.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_PAINT_JOB = { "paints", "rules", "meshes", "out" }
    paints   {name: {"color": [r, g, b] (linear), "roughness", "metallic", "specular"}} — one material instance each, /Game/Hrot/Paint/MI_Paint_<name>
    rules    [[regex, paint], …] over each material SLOT's name + its current material's name, first match wins (glass, tyres, metal)
    meshes   [{"mesh" | "kit", "body": paint}] — a slot no rule matches gets the mesh's body paint

⚠ measured (U-17c): the 10-pack aircraft, the OH-1 and the first fighter ship FBX materials whose textures were never imported — they
draw white. This replaces the MESH's slot materials (the vendor's mesh asset, outside git), so every user of the mesh is painted.
"""
import json, os, re, traceback
import unreal

JOB = json.load(open(os.environ["HROT_PAINT_JOB"], encoding="utf-8"))
R = {"log": []}
EAL = unreal.EditorAssetLibrary
MEL = unreal.MaterialEditingLibrary
AT = unreal.AssetToolsHelpers.get_asset_tools()
BASE = "/Game/Hrot/Paint/M_HrotPaint"


def log(m):
    unreal.log_warning(f"[HROT-PAINT] {m}"); R["log"].append(m)


def base():
    """One opaque material: Color / Roughness / Metallic parameters (a flat paint — no texture to lose)."""
    if EAL.does_asset_exist(BASE):
        m = unreal.load_asset(BASE)
        if "Specular" not in [str(n) for n in MEL.get_scalar_parameter_names(m)]:   # ⭐ added later (U-17m): rubber must not shine
            e = MEL.create_material_expression(m, unreal.MaterialExpressionScalarParameter, -400, 400)
            e.set_editor_property("parameter_name", "Specular"); e.set_editor_property("default_value", 0.5)
            MEL.connect_material_property(e, "", unreal.MaterialProperty.MP_SPECULAR)
            MEL.recompile_material(m); EAL.save_loaded_asset(m, False)
            log("base: Specular parameter added")
        return m
    folder, name = BASE.rsplit("/", 1)
    m = AT.create_asset(name, folder, unreal.Material, unreal.MaterialFactoryNew())
    def par(cls, pname, x, y, value):
        e = MEL.create_material_expression(m, cls, x, y); e.set_editor_property("parameter_name", pname)
        e.set_editor_property("default_value", value); return e
    col = par(unreal.MaterialExpressionVectorParameter, "Color", -400, 0, unreal.LinearColor(0.2, 0.2, 0.2, 1))
    rough = par(unreal.MaterialExpressionScalarParameter, "Roughness", -400, 200, 0.6)
    metal = par(unreal.MaterialExpressionScalarParameter, "Metallic", -400, 300, 0.0)
    MEL.connect_material_property(col, "", unreal.MaterialProperty.MP_BASE_COLOR)
    MEL.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.connect_material_property(metal, "", unreal.MaterialProperty.MP_METALLIC)
    MEL.recompile_material(m); EAL.save_loaded_asset(m, False)
    return m


def instance(name, p, parent):
    path = f"/Game/Hrot/Paint/MI_Paint_{name}"
    mi = unreal.load_asset(path) if EAL.does_asset_exist(path) else \
        AT.create_asset(f"MI_Paint_{name}", "/Game/Hrot/Paint", unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    MEL.set_material_instance_parent(mi, parent)
    MEL.set_material_instance_vector_parameter_value(mi, "Color", unreal.LinearColor(*p["color"], 1.0))
    MEL.set_material_instance_scalar_parameter_value(mi, "Roughness", float(p.get("roughness", 0.6)))
    MEL.set_material_instance_scalar_parameter_value(mi, "Metallic", float(p.get("metallic", 0.0)))
    MEL.set_material_instance_scalar_parameter_value(mi, "Specular", float(p.get("specular", 0.5)))   # ⚠ measured: rubber at 0.5 read grey
    MEL.update_material_instance(mi); EAL.save_loaded_asset(mi, False)
    return mi


def meshes_of(entry):
    if entry.get("mesh"): return [unreal.load_asset(entry["mesh"])]
    kit = unreal.load_asset(entry["kit"])
    return [p.get_editor_property("mesh") for p in kit.get_editor_property("parts")]


def main():
    parent = base()
    mis = {n: instance(n, p, parent) for n, p in JOB["paints"].items()}
    rules = [(re.compile(rx, re.I), paint) for rx, paint in JOB.get("rules", [])]
    for e in JOB["meshes"]:
        for sm in meshes_of(e):
            if sm is None: raise RuntimeError(f"no mesh for {e}")
            mats = list(sm.get_editor_property("static_materials"))
            for i, sl in enumerate(mats):
                old = sl.get_editor_property("material_interface")
                key = f"{sl.get_editor_property('material_slot_name')} {old.get_name() if old else ''}"
                paint = next((p for rx, p in rules if rx.search(key)), "body")
                if paint == "body": paint = e["body"]   # a rule may say "body" (e.g. a nacelle named "…_Metal")
                sl.set_editor_property("material_interface", mis[paint]); mats[i] = sl
                log(f"{sm.get_name():24} slot {i:2} {key:44} -> {paint}")
            sm.set_editor_property("static_materials", mats)
            EAL.save_loaded_asset(sm, False)
            b = sm.get_bounds()
            log(f"{sm.get_name():24} bounds origin {b.origin} extent {b.box_extent}")


try:
    main(); R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
