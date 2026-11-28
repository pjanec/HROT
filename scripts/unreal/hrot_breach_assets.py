"""hrot_breach_assets.py - CE-4651 (U-20): the materials of building damage, built by script (never by hand):

  /Game/Hrot/M_HrotGlass           the house glass: TRANSLUCENT (tinted, reflective, the room seen through it) - was near-black opaque, so a broken
                                    window could not be told from an intact one (measured bt-range 2026-11-24). Shared by every look level.
  /Game/Hrot/Fx/M_HrotGlassCrack   a crack star on a pane (translucent, unlit, two-sided): radial cracks + a ring - drawn on a small quad at HROT's
                                    crack point (deferred decals do not draw on translucent glass)
  /Game/Hrot/Fx/M_HrotWallScorch   a deferred decal for a blown hole: soot darkest at ~1/3 of the decal (the hole's edge), fading out raggedly

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      (clear the read-only flag on M_HrotGlass.uasset first)
"""
import unreal

AT = unreal.AssetToolsHelpers.get_asset_tools()
MEL = unreal.MaterialEditingLibrary
EAL = unreal.EditorAssetLibrary


def fresh(folder, name):
    path = f"{folder}/{name}"
    if EAL.does_asset_exist(path): EAL.delete_asset(path)
    return AT.create_asset(name, folder, unreal.Material, unreal.MaterialFactoryNew())


def E(m, cls, x, y): return MEL.create_material_expression(m, cls, x, y)


def c1(m, v, x, y):
    e = E(m, unreal.MaterialExpressionConstant, x, y); e.set_editor_property("r", v); return e


def c3(m, rgb, x, y):
    e = E(m, unreal.MaterialExpressionConstant3Vector, x, y); e.set_editor_property("constant", unreal.LinearColor(*rgb, 1.0)); return e


def op(m, cls, a, b, x, y):
    e = E(m, cls, x, y); MEL.connect_material_expressions(a, "", e, "A"); MEL.connect_material_expressions(b, "", e, "B"); return e


def one(m, cls, a, x, y):
    e = E(m, cls, x, y); MEL.connect_material_expressions(a, "", e, ""); return e


def glass():
    # changed IN PLACE when it exists: every house tile (Stechovice's too) references it - deleting it would leave them without glass
    m = unreal.load_asset("/Game/Hrot/M_HrotGlass") if EAL.does_asset_exist("/Game/Hrot/M_HrotGlass") else fresh("/Game/Hrot", "M_HrotGlass")
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property("translucency_lighting_mode", unreal.TranslucencyLightingMode.TLM_SURFACE)   # specular reflections on the pane
    m.set_editor_property("two_sided", True)
    MEL.connect_material_property(c3(m, (0.55, 0.63, 0.68), -300, 0), "", unreal.MaterialProperty.MP_BASE_COLOR)
    MEL.connect_material_property(c1(m, 0.03, -300, 100), "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.connect_material_property(c1(m, 1.0, -300, 200), "", unreal.MaterialProperty.MP_SPECULAR)
    MEL.connect_material_property(c1(m, 0.5, -300, 300), "", unreal.MaterialProperty.MP_OPACITY)
    MEL.recompile_material(m); return m


def crack():
    """a BULLET HOLE in a pane: the textures from hrot_glass_crack_tex.py (HROT_CRACK_PNG = their folder) imported to /Game/Hrot/Fx/Cracks,
    the material samples a parameter "Crack" (the client picks a variant per hit): translucent, unlit, two-sided, alpha = the cracks.
    (Replaced the procedural star, user 2026-11-24: "not too realistic".)"""
    import glob, os
    src = os.environ.get("HROT_CRACK_PNG", "")
    tasks = []
    for f in sorted(glob.glob(os.path.join(src, "T_HrotGlassCrack_*.png"))):
        t = unreal.AssetImportTask(); t.filename = f; t.destination_path = "/Game/Hrot/Fx/Cracks"; t.automated = True; t.replace_existing = True; t.save = True
        tasks.append(t)
    AT.import_asset_tasks(tasks)
    first = unreal.load_asset("/Game/Hrot/Fx/Cracks/T_HrotGlassCrack_0")
    m = fresh("/Game/Hrot/Fx", "M_HrotGlassCrack")
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    m.set_editor_property("two_sided", True)
    tex = E(m, unreal.MaterialExpressionTextureSampleParameter2D, -500, 0)
    tex.set_editor_property("parameter_name", "Crack")
    if first: tex.set_editor_property("texture", first)
    rgb = E(m, unreal.MaterialExpressionMultiply, -150, 0)   # measured: wiring the Multiply's (absent) "RGB" pin left emissive empty - the cracks drew BLACK
    MEL.connect_material_expressions(tex, "RGB", rgb, "A"); MEL.connect_material_expressions(c1(m, 0.8, -300, 150), "", rgb, "B")
    MEL.connect_material_property(rgb, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    a = E(m, unreal.MaterialExpressionMultiply, -150, 250)
    MEL.connect_material_expressions(tex, "A", a, "A"); MEL.connect_material_expressions(c1(m, 2.0, -300, 300), "", a, "B")   # thin lines lose alpha in the mips
    MEL.connect_material_property(one(m, unreal.MaterialExpressionSaturate, a, 0, 250), "", unreal.MaterialProperty.MP_OPACITY)
    MEL.recompile_material(m)
    unreal.log_warning(f"[HROT-BREACH-ASSETS] {len(tasks)} crack textures imported from {src}")
    return m


def wall_scorch():
    """soot: darkest in a band around r ~0.3 (where the decal meets the hole's edge), ragged out to r 1"""
    m = fresh("/Game/Hrot/Fx", "M_HrotWallScorch")
    m.set_editor_property("material_domain", unreal.MaterialDomain.MD_DEFERRED_DECAL)
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    tc = E(m, unreal.MaterialExpressionTextureCoordinate, -1000, 0)
    centred = op(m, unreal.MaterialExpressionMultiply, op(m, unreal.MaterialExpressionSubtract, tc, c1(m, 0.5, -900, 100), -800, 0), c1(m, 2.0, -800, 100), -650, 0)
    r = one(m, unreal.MaterialExpressionLength, centred, -500, 0)
    noise = E(m, unreal.MaterialExpressionNoise, -650, 250); noise.set_editor_property("scale", 0.05)
    ragged = op(m, unreal.MaterialExpressionAdd, r, op(m, unreal.MaterialExpressionMultiply, noise, c1(m, 0.22, -500, 350), -350, 250), -200, 100)
    soot = one(m, unreal.MaterialExpressionSaturate, op(m, unreal.MaterialExpressionMultiply,
               op(m, unreal.MaterialExpressionSubtract, c1(m, 1.0, -200, 250), ragged, -50, 150), c1(m, 2.2, -50, 300), 100, 150), 250, 150)
    MEL.connect_material_property(op(m, unreal.MaterialExpressionMultiply, soot, c1(m, 0.97, 250, 300), 400, 150), "", unreal.MaterialProperty.MP_OPACITY)
    MEL.connect_material_property(c3(m, (0.012, 0.010, 0.009), 400, 0), "", unreal.MaterialProperty.MP_BASE_COLOR)
    MEL.connect_material_property(c1(m, 0.95, 400, 400), "", unreal.MaterialProperty.MP_ROUGHNESS)
    MEL.recompile_material(m); return m


for f in (glass, crack, wall_scorch):
    m = f()
    unreal.log_warning(f"[HROT-BREACH-ASSETS] {m.get_path_name()} saved {EAL.save_loaded_asset(m, False)}")
