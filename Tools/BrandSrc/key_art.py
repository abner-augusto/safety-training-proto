"""Turn a blue-on-white line drawing into accent-coloured line art on alpha.

The white paper must become transparency and the ink must take the banner
theme's accent: the dark-mode accent by default, or the hex passed third.
    python Tools/BrandSrc/key_art.py art/andaime-hermes.png art/andaime-linha.png
    python Tools/BrandSrc/key_art.py art/andaime-hermes.png art/andaime-linha-light.png 2264c4
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

hexa = sys.argv[3] if len(sys.argv) > 3 else "4d8df0"
ACCENT = tuple(int(hexa[i:i + 2], 16) for i in (0, 2, 4))

src, dst = (Path(__file__).resolve().parent / a for a in sys.argv[1:3])
rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
# Darkest channel tracks ink density for any hue; white paper reads ~255.
ink = 1.0 - rgb.min(axis=2) / 255.0
# Normalise so the densest strokes reach full opacity and paper noise drops out.
alpha = np.clip((ink - 0.04) / (np.percentile(ink[ink > 0.3], 90) - 0.04), 0, 1)
out = np.zeros((*alpha.shape, 4), np.uint8)
out[..., :3] = ACCENT
out[..., 3] = (alpha * 255).round().astype(np.uint8)
Image.fromarray(out, "RGBA").save(dst)
print(dst, out.shape[1], "x", out.shape[0])
