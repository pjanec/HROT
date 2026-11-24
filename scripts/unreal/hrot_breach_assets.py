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
    """radial cracks: thin lines where sin(7.3 * angle + 3 * r) is near 0, out to r 0.5; a broken ring at r ~0.12; white, unlit"""
    m = fresh("/Game/Hrot/Fx", "M_HrotGlassCrack")
    m.set_editor_property("blend_mode", unreal.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    m.set_editor_property("two_sided", True)
    tc = E(m, unreal.MaterialExpressionTextureCoordinate, -1400, 0)
    d = op(m, unreal.MaterialExpressionSubtract, tc, c1(m, 0.5, -1400, 120), -1250, 0)
    mx = E(m, unreal.MaterialExpressionComponentMask, -1100, -60); mx.set_editor_property("r", True); mx.set_editor_property("g", False)
    my = E(m, unreal.MaterialExpressionComponentMask, -1100, 60); my.set_editor_property("r", False); my.set_editor_property("g", True)
    MEL.connect_material_expressions(d, "", mx, ""); MEL.connect_material_expressions(d, "", my, "")
    ang = E(m, unreal.MaterialExpressionArctangent2, -950, 0)
    MEL.connect_material_expressions(my, "", ang, "Y"); MEL.connect_material_expressions(mx, "", ang, "X")
    r = one(m, unreal.MaterialExpressionLength, d, -950, 150)
    wav = op(m, unreal.MaterialExpressionAdd, op(m, unreal.MaterialExpressionMultiply, ang, c1(m, 5.0, -800, 60), -650, 0),
             op(m, unreal.MaterialExpressionMultiply, r, c1(m, 1.2, -800, 220), -650, 160), -500, 60)
    s = one(m, unreal.MaterialExpressionAbs, one(m, unreal.MaterialExpressionSine, wav, -380, 60), -260, 60)
    width = op(m, unreal.MaterialExpressionMultiply, r, c1(m, 0.22, -380, 200), -260, 200)   # measured: 0.12 drew 1-2 px lines, 0.5 a spirograph
    spokes = one(m, unreal.MaterialExpressionSaturate, op(m, unreal.MaterialExpressionSubtract, c1(m, 1.0, -150, 0),
                 op(m, unreal.MaterialExpressionDivide, s, op(m, unreal.MaterialExpressionAdd, width, c1(m, 0.05, -260, 300), -150, 260), -50, 120), 80, 60), 220, 60)
    ring = one(m, unreal.MaterialExpressionSaturate, op(m, unreal.MaterialExpressionSubtract, c1(m, 1.0, -150, 420),
               op(m, unreal.MaterialExpressionMultiply, one(m, unreal.MaterialExpressionAbs, op(m, unreal.MaterialExpressionSubtract, r, c1(m, 0.05, -380, 480), -260, 420), -150, 500),
                  c1(m, 70.0, -150, 580), -30, 520), 80, 420), 220, 420)
    fade = one(m, unreal.MaterialExpressionSaturate, op(m, unreal.MaterialExpressionMultiply, op(m, unreal.MaterialExpressionSubtract, c1(m, 0.5, -150, 700), r, -30, 700),
               c1(m, 6.0, -30, 800), 80, 720), 220, 720)
    lines = op(m, unreal.MaterialExpressionMultiply, op(m, unreal.MaterialExpressionMax, spokes, ring, 360, 200), fade, 500, 300)
    MEL.connect_material_property(op(m, unreal.MaterialExpressionMultiply, lines, c1(m, 0.8, 500, 450), 650, 300), "", unreal.MaterialProperty.MP_OPACITY)
    MEL.connect_material_property(c3(m, (0.9, 0.93, 0.95), 650, 100), "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    MEL.recompile_material(m); return m


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
