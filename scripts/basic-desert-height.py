#!/usr/bin/env python3
"""CE-3086 / Terrain Height H4 — writes basic-desert's height grid (ESRI ASCII, north row first).

The relief (docs/DESIGN_Terrain_Height.md §5d owns the numbers and why they sit where they do):
  * a RIDGE south of the track: crest RIDGE_H m high along y = RIDGE_Y from x = RIDGE_X0 to RIDGE_X1, falling to the
    plain over RIDGE_FALL m with a cosine shoulder (steepest = RIDGE_H * pi / (2 * RIDGE_FALL) — under the vehicles' 20 deg);
  * a WADI running north-south through the whole map at x = WADI_X: a WADI_D m deep flat bed WADI_BED m wide, banks
    WADI_BANK m wide each side;
  * the plain at 0 everywhere else — under every rock, the compound, the track's verges and every desert scenario's
    entity, so nothing that already stands on basic-desert moves.

Usage: python3 scripts/basic-desert-height.py > Hrot/Subsystems/Hrot.AI.Behaviors/Recipes/Terrain/basic-desert/basic-desert.height.asc
"""
import math

SIZE, CELL = 600, 10
RIDGE_H, RIDGE_Y, RIDGE_X0, RIDGE_X1, RIDGE_FALL = 10.0, 210.0, 230.0, 320.0, 50.0
WADI_X, WADI_D, WADI_BED, WADI_BANK = 520.0, 3.0, 20.0, 15.0


def shoulder(d, fall):
    """1 inside, a cosine fall to 0 over `fall` metres outside."""
    if d <= 0:
        return 1.0
    if d >= fall:
        return 0.0
    return 0.5 * (1.0 + math.cos(math.pi * d / fall))


def height(x, y):
    along = max(RIDGE_X0 - x, 0.0, x - RIDGE_X1)
    ridge = RIDGE_H * shoulder(abs(y - RIDGE_Y), RIDGE_FALL) * shoulder(along, RIDGE_FALL)
    wadi = -WADI_D * shoulder(abs(x - WADI_X) - WADI_BED / 2, WADI_BANK)
    return ridge + wadi


def main():
    n = SIZE // CELL + 1
    print(f"ncols {n}\nnrows {n}\nxllcenter 0\nyllcenter 0\ncellsize {CELL}\nNODATA_value -9999")
    for row in range(n):
        y = (n - 1 - row) * CELL  # north row first
        print(" ".join(f"{height(col * CELL, y):.2f}".replace("-0.00", "0.00") for col in range(n)))


if __name__ == "__main__":
    main()
