#!/usr/bin/env python3
"""Build Android and Windows icon assets from the approved generated icon.

Input image is the approved 1024x1024 generated icon with a white canvas. The script:
- removes the near-white canvas into transparency with antialiased edges;
- crops/recenters the actual icon art;
- exports Android density/adaptive assets;
- exports Windows PNG sizes and a multi-size .ico;
- writes Unity .meta files for the primary Android textures;
- patches Android icon slots in ProjectSettings.
"""
from __future__ import annotations

import hashlib
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path("/home/hermes-vm/.hermes/cache/images/openai_codex_gpt-image-2-medium_20260613_092722_c3576c0a.png")
OUT_ROOT = ROOT / "Assets" / "_SafetyProto" / "Art" / "Icons" / "AppIcon"
ANDROID_DIR = OUT_ROOT / "Android"
WINDOWS_DIR = OUT_ROOT / "Windows"
PROJECT_SETTINGS = ROOT / "ProjectSettings" / "ProjectSettings.asset"

# Fixed GUIDs so reruns are stable and PlayerSettings references don't churn.
GUID_ANDROID_LEGACY = "a1c641a1a90246a68ce6c2eac7bae928"
GUID_ANDROID_ROUND = "c326d396607f4c6c81fdf12ae0f4bbdd"
GUID_ANDROID_BG = "3731715aab4544c190f9427723d0eb66"
GUID_ANDROID_FG = "1b96f03edc3b44bf918a5084dc765c4b"
GUID_MASTER_TRANSPARENT = "1957907f45a440bc9b7f711205acdaa2"

TEXTURE_META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices: 
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName: 
  pSDRemoveMatte: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

DEFAULT_IMPORTER_META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
{folder_line}DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def deterministic_guid(path: Path) -> str:
    rel = path.relative_to(ROOT).as_posix()
    return hashlib.md5(("safety-training-proto:" + rel).encode("utf-8")).hexdigest()


def default_importer_meta(path: Path, *, folder: bool) -> str:
    return DEFAULT_IMPORTER_META_TEMPLATE.format(
        guid=deterministic_guid(path),
        folder_line="folderAsset: yes\n" if folder else "",
    )


def write_missing_unity_metas(*bases: Path) -> None:
    """Create Unity .meta files for generated folders and secondary icon exports."""
    for base in bases:
        for path in sorted([base, *base.rglob("*")], key=lambda item: item.as_posix()):
            meta = Path(str(path) + ".meta")
            if meta.exists():
                continue
            if path.is_dir():
                meta.write_text(default_importer_meta(path, folder=True), encoding="utf-8")
            elif path.suffix.lower() in {".png", ".jpg", ".jpeg"}:
                meta.write_text(TEXTURE_META_TEMPLATE.format(guid=deterministic_guid(path)), encoding="utf-8")
            elif path.suffix.lower() == ".ico":
                meta.write_text(default_importer_meta(path, folder=False), encoding="utf-8")


def remove_white_canvas(src: Image.Image) -> Image.Image:
    """Remove white/near-white outer canvas while preserving antialiased edges."""
    src = src.convert("RGBA")
    w, h = src.size
    out = Image.new("RGBA", src.size)
    for y in range(h):
        for x in range(w):
            r, g, b, a = src.getpixel((x, y))  # type: ignore[misc]
            # Distance from the white canvas. Smooth ramp prevents jagged edges.
            delta = max(255 - r, 255 - g, 255 - b)
            if delta <= 4:
                na = 0
            elif delta >= 42:
                na = a
            else:
                na = int(a * (delta - 4) / 38)
            out.putpixel((x, y), (r, g, b, na))
    return out


def alpha_bbox(img: Image.Image, threshold: int = 12) -> tuple[int, int, int, int] | None:
    alpha = img.getchannel("A")
    w, h = alpha.size
    min_x, min_y = w, h
    max_x, max_y = -1, -1
    for y in range(h):
        for x in range(w):
            if alpha.getpixel((x, y)) > threshold:  # type: ignore[operator]
                min_x = min(min_x, x)
                min_y = min(min_y, y)
                max_x = max(max_x, x)
                max_y = max(max_y, y)
    if max_x < min_x or max_y < min_y:
        return None
    return (min_x, min_y, max_x + 1, max_y + 1)


def crop_and_center(img: Image.Image, size: int = 1024, occupancy: float = 0.90) -> Image.Image:
    bbox = alpha_bbox(img)
    if not bbox:
        raise RuntimeError("No non-background icon pixels found after background removal.")
    crop = img.crop(bbox)
    target = int(size * occupancy)
    scale = min(target / crop.width, target / crop.height)
    new_size = (round(crop.width * scale), round(crop.height * scale))
    crop = crop.resize(new_size, Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(crop, ((size - new_size[0]) // 2, (size - new_size[1]) // 2))
    return out


def rounded_mask(size: int, radius: int) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def teal_background(size: int = 1024) -> Image.Image:
    img = Image.new("RGBA", (size, size), (6, 42, 52, 255))
    d = ImageDraw.Draw(img)
    for y in range(size):
        t = y / (size - 1)
        col = (int(6 * (1 - t) + 9 * t), int(38 * (1 - t) + 74 * t), int(48 * (1 - t) + 80 * t), 255)
        d.line([(0, y), (size, y)], fill=col)

    overlay = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay, "RGBA")
    for x in range(-size, size * 2, 128):
        od.line([(x, 0), (x + size, size)], fill=(255, 255, 255, 12), width=2)
    img = Image.alpha_composite(img, overlay)

    glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow, "RGBA")
    gd.ellipse([160, 130, 864, 820], fill=(20, 180, 165, 70))
    return Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(64)))


def save_png(path: Path, img: Image.Image, size: int | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    out = img if size is None else img.resize((size, size), Image.LANCZOS)
    out.save(path, optimize=True)


def write_meta(path: Path, guid: str) -> None:
    path.with_suffix(path.suffix + ".meta").write_text(TEXTURE_META_TEMPLATE.format(guid=guid), encoding="utf-8")


def write_icon_pack() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    transparent = crop_and_center(remove_white_canvas(source), occupancy=0.92)

    # Android primary assets.
    bg = teal_background()
    fg = transparent
    legacy = Image.alpha_composite(bg.copy(), transparent)
    legacy_masked = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    legacy_masked.paste(legacy, (0, 0), rounded_mask(1024, 220))
    round_icon = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    circle = Image.new("L", (1024, 1024), 0)
    ImageDraw.Draw(circle).ellipse([0, 0, 1023, 1023], fill=255)
    round_icon.paste(legacy, (0, 0), circle)

    save_png(OUT_ROOT / "app_icon_approved_transparent_1024.png", transparent)
    write_meta(OUT_ROOT / "app_icon_approved_transparent_1024.png", GUID_MASTER_TRANSPARENT)

    save_png(ANDROID_DIR / "app_icon_legacy.png", legacy_masked)
    write_meta(ANDROID_DIR / "app_icon_legacy.png", GUID_ANDROID_LEGACY)
    save_png(ANDROID_DIR / "app_icon_round.png", round_icon)
    write_meta(ANDROID_DIR / "app_icon_round.png", GUID_ANDROID_ROUND)
    save_png(ANDROID_DIR / "app_icon_adaptive_background.png", bg)
    write_meta(ANDROID_DIR / "app_icon_adaptive_background.png", GUID_ANDROID_BG)
    save_png(ANDROID_DIR / "app_icon_adaptive_foreground.png", fg)
    write_meta(ANDROID_DIR / "app_icon_adaptive_foreground.png", GUID_ANDROID_FG)

    for size in [36, 48, 72, 81, 96, 108, 144, 162, 192, 216, 324, 432, 512, 1024]:
        save_png(ANDROID_DIR / f"png" / f"app_icon_{size}.png", legacy_masked, size)

    # Windows pack: transparent PNG sizes plus multi-resolution ICO.
    for size in [16, 24, 32, 48, 64, 128, 256, 512, 1024]:
        save_png(WINDOWS_DIR / f"png" / f"app_icon_{size}.png", transparent, size)
    ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    transparent.save(WINDOWS_DIR / "app_icon.ico", sizes=ico_sizes)


def icon_texture_block(kind: int) -> str:
    if kind == 2:
        return (
            "m_Textures:\n"
            f"      - {{fileID: 2800000, guid: {GUID_ANDROID_BG}, type: 3}}\n"
            f"      - {{fileID: 2800000, guid: {GUID_ANDROID_FG}, type: 3}}"
        )
    if kind == 1:
        return (
            "m_Textures:\n"
            f"      - {{fileID: 2800000, guid: {GUID_ANDROID_ROUND}, type: 3}}"
        )
    return (
        "m_Textures:\n"
        f"      - {{fileID: 2800000, guid: {GUID_ANDROID_LEGACY}, type: 3}}"
    )


def patch_android_player_settings() -> None:
    text = PROJECT_SETTINGS.read_text(encoding="utf-8")
    start = text.index("  m_BuildTargetPlatformIcons:\n  - m_BuildTarget: Android")
    end = text.index("  m_BuildTargetBatching:", start)
    block = text[start:end]
    pattern = re.compile(
        r"    - m_Textures:(?: \[\])?(?:\n      - \{[^\n]+\})*\n"
        r"(?P<rest>      m_Width: (?P<width>\d+)\n      m_Height: (?P<height>\d+)\n      m_Kind: (?P<kind>\d+)\n      m_SubKind: )"
    )

    def repl(match: re.Match[str]) -> str:
        return "    - " + icon_texture_block(int(match.group("kind"))) + "\n" + match.group("rest")

    patched, count = pattern.subn(repl, block)
    if count != 18:
        raise RuntimeError(f"Expected to patch 18 Android icon slots, patched {count}.")
    PROJECT_SETTINGS.write_text(text[:start] + patched + text[end:], encoding="utf-8")


def main() -> None:
    if not SOURCE.exists():
        raise FileNotFoundError(SOURCE)
    write_icon_pack()
    write_missing_unity_metas(OUT_ROOT)
    patch_android_player_settings()
    print("Generated approved icon pack")
    print(f"- transparent master: {OUT_ROOT / 'app_icon_approved_transparent_1024.png'}")
    print(f"- Android pack:       {ANDROID_DIR}")
    print(f"- Windows pack:       {WINDOWS_DIR}")
    print("Patched Android PlayerSettings icon slots.")


if __name__ == "__main__":
    main()
