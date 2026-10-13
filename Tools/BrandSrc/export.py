"""Render the opening banner and the app icon set from brand.html.

Overwrites the PNGs in place so their .meta GUIDs, and the ProjectSettings
references to them, stay untouched. Needs Playwright's Chromium:
    uv run --with playwright python Tools/BrandSrc/export.py [out_dir]
"""
import sys
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright

here = Path(__file__).resolve().parent
out = Path(sys.argv[1]) if len(sys.argv) > 1 else here.parents[1] / "Assets" / "_SafetyProto" / "Art"

A = "Icons/AppIcon/"
JOBS = [
    ("banner", "Splash/splash_treinamento_seguranca_virtual_1920x1080.png", 0, "", ""),
    # Not a Unity asset: the README shows it to readers on a light GitHub theme.
    ("banner", here.parents[1] / "docs" / "images" / "splash-light.png", 0, "light", ""),
    ("icon", A + "Android/app_icon_adaptive_background.png", 1024, "bg", "none"),
    ("icon", A + "Android/app_icon_adaptive_foreground.png", 1024, "fg", "none"),
    ("icon", A + "Android/app_icon_legacy.png", 1024, "full", "squircle"),
    ("icon", A + "Android/app_icon_round.png", 1024, "full", "circle"),
    ("icon", A + "app_icon_approved_transparent_1024.png", 1024, "full", "squircle"),
]
for s in [1024, 512, 432, 324, 216, 192, 162, 144, 108, 96, 81, 72, 48, 36]:
    JOBS.append(("icon", f"{A}Android/png/app_icon_{s}.png", s, "full", "squircle"))
WIN_SIZES = [1024, 512, 256, 128, 64, 48, 32, 24, 16]
for s in WIN_SIZES:
    JOBS.append(("icon", f"{A}Windows/png/app_icon_{s}.png", s, "full", "squircle"))

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 1920, "height": 1080}, device_scale_factor=1)
    pg.goto((here / "brand.html").as_uri())
    for kind, rel, size, layer, mask in JOBS:
        pg.evaluate("([k,s,l,m]) => show(k,s,l,m)", [kind, size, layer, mask])
        pg.wait_for_timeout(150)
        dst = out / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        el = pg.locator(".banner") if kind == "banner" else pg.locator("#stage svg")
        el.screenshot(path=str(dst), omit_background=True)
        print(rel)
    b.close()

# Pillow downsamples the largest frame for each .ico entry, so feed it the 256 render.
win = out / A / "Windows"
Image.open(win / "png" / "app_icon_256.png").save(
    win / "app_icon.ico", sizes=[(s, s) for s in WIN_SIZES if s <= 256])
print(A + "Windows/app_icon.ico")
