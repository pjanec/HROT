"""hrot_camo.py — CE-4651 (U-18k): TEXTURED liveries for a textured vehicle pack, run as an editor COMMANDLET.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_CAMO_JOB = { "liveries": {name: {"tintAmount", "tint",
                                                                                 "gain", "roughMin", "specular"}}, "vehicles": [{"name", "color", "normal",
                                                                                 "aoRough"}], "meshes": [{"mesh", "material"}], "out" }

One base material, /Game/Hrot/Paint/M_HrotCamo (made once): the vehicle's own colour, normal and packed AO (R) / roughness (G) textures;
BaseColor = lerp(colour, desaturated colour × Tint × Gain, TintAmount) — so a GREEN livery keeps the camouflage's pattern as its
brightness, not a flat paint; Roughness = max(the texture's, RoughMin) — a MATTE floor (U-17m: a military finish must not shine);
Specular a parameter (rubber and matte paint must not glint). Per vehicle and livery: MI_Camo_<vehicle>_<livery>. "meshes" sets a
mesh asset's material (the cut parts' default livery; their wheels the matte rubber paint).
"""
import json, os, traceback
import unreal

JOB = json.load(open(os.environ["HROT_CAMO_JOB"], encoding="utf-8"))
R = {"log": [], "materials": {}}
EAL, MEL = unreal.EditorAssetLibrary, unreal.MaterialEditingLibrary
AT = unreal.AssetToolsHelpers.get_asset_tools()
BASE = "/Game/Hrot/Paint/M_HrotCamo"


def log(m):
    unreal.log_warning(f"[HROT-CAMO] {m}"); R["log"].append(m)


def base(defaults):
    if EAL.does_asset_exist(BASE): return unreal.load_asset(BASE)
    folder, name = BASE.rsplit("/", 1)
    m = AT.create_asset(name, folder, unreal.Material, unreal.MaterialFactoryNew())
    E = lambda cls, x, y: MEL.create_material_expression(m, cls, x, y)

    def tex(pname, path, sampler, x, y):
        e = E(unreal.MaterialExpressionTextureSampleParameter2D, x, y)
        e.set_editor_property("parameter_name", pname)
        e.set_editor_property("texture", unreal.load_asset(path))
        e.set_editor_property("sampler_type", sampler)
        return e

    def scalar(pname, v, x, y):
        e = E(unreal.MaterialExpressionScalarParameter, x, y)
        e.set_editor_property("parameter_name", pname); e.set_editor_property("default_value", v)
        return e

    col = tex("Color", defaults["color"], unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, -1200, -200)
    nor = tex("Normal", defaults["normal"], unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL, -1200, 200)
    aor = tex("AoRough", defaults["aoRough"], unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR, -1200, 500)
    tint = E(unreal.MaterialExpressionVectorParameter, -900, -400)
    tint.set_editor_property("parameter_name", "Tint"); tint.set_editor_property("default_value", unreal.LinearColor(1, 1, 1, 1))
    amt = scalar("TintAmount", 0.0, -500, -450)
    gain = scalar("Gain", 1.0, -900, -100)
    rmin = scalar("RoughMin", 0.7, -900, 650)
    spec = scalar("Specular", 0.3, -300, 300)
    des = E(unreal.MaterialExpressionDesaturation, -900, -250)
    MEL.connect_material_expressions(col, "RGB", des, "")
    m1 = E(unreal.MaterialExpressionMultiply, -700, -300); MEL.connect_material_expressions(des, "", m1, "A"); MEL.connect_material_expressions(tint, "", m1, "B")
    m2 = E(unreal.MaterialExpressionMultiply, -550, -250); MEL.connect_material_expressions(m1, "", m2, "A"); MEL.connect_material_expressions(gain, "", m2, "B")
    lerp = E(unreal.MaterialExpressionLinearInterpolate, -350, -250)
    MEL.connect_material_expressions(col, "RGB", lerp, "A"); MEL.connect_material_expressions(m2, "", lerp, "B"); MEL.connect_material_expressions(amt, "", lerp, "Alpha")
    MEL.connect_material_property(lerp, "", unreal.MaterialProperty.MP_BASE_COLOR)
    mx = E(unreal.MaterialExpressionMax, -600, 550); MEL.connect_material_expressions(aor, "G", mx, "A"); MEL.connect_material_expressions(rmin, "", mx, "B")
    MEL.connect_material_property(mx, "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.connect_material_property(aor, "R", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)
    MEL.connect_material_property(nor, "RGB", unreal.MaterialProperty.MP_NORMAL)
    MEL.connect_material_property(spec, "", unreal.MaterialProperty.MP_SPECULAR)
    MEL.recompile_material(m)
    EAL.save_loaded_asset(m, False)
    log("base M_HrotCamo made")
    return m


try:
    vs = JOB["vehicles"]
    parent = base(vs[0])
    for v in vs:
        for lname, L in JOB["liveries"].items():
            path = f"/Game/Hrot/Paint/Camo/MI_Camo_{v['name']}_{lname}"
            mi = unreal.load_asset(path) if EAL.does_asset_exist(path) else \
                AT.create_asset(path.rsplit("/", 1)[1], path.rsplit("/", 1)[0], unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
            MEL.set_material_instance_parent(mi, parent)
            MEL.set_material_instance_texture_parameter_value(mi, "Color", unreal.load_asset(v["color"]))
            MEL.set_material_instance_texture_parameter_value(mi, "Normal", unreal.load_asset(v["normal"]))
            MEL.set_material_instance_texture_parameter_value(mi, "AoRough", unreal.load_asset(v["aoRough"]))
            t = L.get("tint", [1, 1, 1])
            MEL.set_material_instance_vector_parameter_value(mi, "Tint", unreal.LinearColor(t[0], t[1], t[2], 1))
            MEL.set_material_instance_scalar_parameter_value(mi, "TintAmount", float(L.get("tintAmount", 0)))
            MEL.set_material_instance_scalar_parameter_value(mi, "Gain", float(L.get("gain", 1)))
            MEL.set_material_instance_scalar_parameter_value(mi, "RoughMin", float(L.get("roughMin", 0.7)))
            MEL.set_material_instance_scalar_parameter_value(mi, "Specular", float(L.get("specular", 0.3)))
            MEL.update_material_instance(mi)
            EAL.save_loaded_asset(mi, False)
            R["materials"][f"{v['name']}_{lname}"] = path
    for m in JOB.get("meshes") or []:   # a mesh's own material (slot 0)
        sm = unreal.load_asset(m["mesh"]); mat = unreal.load_asset(m["material"])
        sm.set_material(0, mat)
        EAL.save_loaded_asset(sm, False)
    log(f"{len(R['materials'])} liveries, {len(JOB.get('meshes') or [])} meshes set")
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
