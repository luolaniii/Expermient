#!/usr/bin/env python3
"""
pack_maskmap.py

A small, production-friendly channel packer for Unity URP/HDRP.

Supports three outputs:
  1) urp-terrain mask map (RGBA):
     R = Metallic, G = Ambient Occlusion, B = Height, A = Smoothness
  2) hdrp mask map (RGBA):
     R = Metallic, G = Ambient Occlusion, B = Detail Mask, A = Smoothness
     (Height is typically a separate texture in HDRP terrain/material)
  3) urp-ra (MetallicSmooth) single texture using R/A:
     R = Metallic, A = Smoothness (Green/Blue channels ignored)

Features:
  - Accepts either Smoothness or Roughness input; can invert Roughness -> Smoothness
  - Auto-resizes channels to a common target resolution
  - Allows constant fallbacks per channel when a file is absent
  - Validates inputs and prints a clear summary

Usage examples:
  - URP Terrain mask map:
    python tools/pack_maskmap.py urp-terrain \
      --metallic path/to/metallic.png \
      --occlusion path/to/ao.png \
      --height path/to/height.png \
      --smoothness path/to/smooth.png \
      --out path/to/Mask_URP.png

  - HDRP mask map (DetailMask optional):
    python tools/pack_maskmap.py hdrp \
      --metallic path/to/metallic.png \
      --occlusion path/to/ao.png \
      --detail path/to/detailmask.png \
      --roughness path/to/roughness.png --invert-roughness \
      --out path/to/Mask_HDRP.png

  - URP Metallic+Smoothness RA texture:
    python tools/pack_maskmap.py urp-ra \
      --metallic path/to/metallic.png \
      --smoothness path/to/smooth.png \
      --out path/to/MetalSmooth.png
"""

from __future__ import annotations

import argparse
import os
import sys
from typing import Optional, Tuple, Dict, List

try:
    from PIL import Image
except Exception as import_error:  # pragma: no cover
    print("[ERROR] Pillow is not installed. Install with: pip install Pillow", file=sys.stderr)
    raise


def read_grayscale(path: Optional[str], fallback_value: int, target_size: Optional[Tuple[int, int]] = None) -> Image.Image:
    """Read an image as single channel (L). If path is None, return a solid image filled with fallback_value.

    Args:
        path: Path to an image file or None.
        fallback_value: 0-255 value to use if no file is provided.
        target_size: If provided, resize the image to this (width, height).

    Returns:
        PIL Image in mode "L".
    """
    if path is None:
        if target_size is None:
            target_size = (1024, 1024)
        solid = Image.new("L", target_size, color=int(fallback_value))
        return solid

    if not os.path.isfile(path):
        raise FileNotFoundError(f"Input image not found: {path}")

    img = Image.open(path)
    if img.mode not in ("L", "LA", "RGB", "RGBA"):
        img = img.convert("RGBA")

    # Convert to L
    img_l = img.convert("L")

    if target_size is not None and (img_l.width != target_size[0] or img_l.height != target_size[1]):
        img_l = img_l.resize(target_size, resample=Image.BICUBIC)

    return img_l


def determine_target_size(paths: List[str]) -> Tuple[int, int]:
    """Determine a common target size from the first existing image path.

    Falls back to 1024x1024 if none found.
    """
    for p in paths:
        if p and os.path.isfile(p):
            with Image.open(p) as im:
                return (im.width, im.height)
    return (1024, 1024)


def ensure_uint8(image_l: Image.Image) -> Image.Image:
    """Ensure the image is 8-bit grayscale."""
    if image_l.mode != "L":
        return image_l.convert("L")
    return image_l


def invert_l(image_l: Image.Image) -> Image.Image:
    """Invert a single-channel image (L)."""
    return Image.eval(ensure_uint8(image_l), lambda v: 255 - v)


def compose_rgba(r: Image.Image, g: Image.Image, b: Image.Image, a: Image.Image) -> Image.Image:
    r8 = ensure_uint8(r)
    g8 = ensure_uint8(g)
    b8 = ensure_uint8(b)
    a8 = ensure_uint8(a)
    return Image.merge("RGBA", (r8, g8, b8, a8))


def save_png(img: Image.Image, out_path: str) -> None:
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path, format="PNG", compress_level=6)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Pack channel maps for Unity URP/HDRP.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )

    subparsers = parser.add_subparsers(dest="mode", required=True)

    # URP Terrain: RGBA = M, AO, Height, Smoothness
    p_urp_terrain = subparsers.add_parser("urp-terrain", help="Pack URP Terrain mask map (R=Metal, G=AO, B=Height, A=Smoothness)")
    add_common_inputs(p_urp_terrain, include_detail=False, include_height=True)

    # HDRP: RGBA = M, AO, Detail, Smoothness
    p_hdrp = subparsers.add_parser("hdrp", help="Pack HDRP mask map (R=Metal, G=AO, B=DetailMask, A=Smoothness)")
    add_common_inputs(p_hdrp, include_detail=True, include_height=False)

    # URP RA: R=Metallic, A=Smoothness
    p_urp_ra = subparsers.add_parser("urp-ra", help="Pack URP Metallic+Smoothness RA texture (R=Metallic, A=Smoothness)")
    p_urp_ra.add_argument("--metallic", type=str, default=None, help="Metallic grayscale image path")
    add_smoothness_inputs(p_urp_ra)
    p_urp_ra.add_argument("--out", type=str, required=True, help="Output PNG path")

    return parser


def add_common_inputs(p: argparse.ArgumentParser, include_detail: bool, include_height: bool) -> None:
    p.add_argument("--metallic", type=str, default=None, help="Metallic grayscale image path")
    p.add_argument("--occlusion", "--ao", dest="occlusion", type=str, default=None, help="Ambient Occlusion grayscale image path")
    if include_height:
        p.add_argument("--height", type=str, default=None, help="Height grayscale image path")
    if include_detail:
        p.add_argument("--detail", type=str, default=None, help="Detail Mask grayscale image path (white=enabled)")
    add_smoothness_inputs(p)
    p.add_argument("--out", type=str, required=True, help="Output PNG path")


def add_smoothness_inputs(p: argparse.ArgumentParser) -> None:
    p.add_argument("--smoothness", type=str, default=None, help="Smoothness grayscale image path (Unity expects Smoothness)")
    p.add_argument("--roughness", type=str, default=None, help="Roughness grayscale image path (will invert to Smoothness)")
    p.add_argument("--invert-roughness", action="store_true", help="Invert roughness to get smoothness (equivalent to 1 - roughness)")
    p.add_argument("--default-smoothness", type=float, default=0.5, help="Fallback smoothness if neither smoothness nor roughness provided (0..1)")


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def main(argv: Optional[List[str]] = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    # Collect potential size sources
    candidate_paths: List[str] = []
    for attr in ("metallic", "occlusion", "height", "detail", "smoothness", "roughness"):
        if hasattr(args, attr):
            p = getattr(args, attr)
            if p:
                candidate_paths.append(p)

    target_size = determine_target_size(candidate_paths)

    # Prepare channels for each mode
    if args.mode == "urp-ra":
        # R=Metallic, A=Smoothness; G/B = 0 (ignored)
        metallic_l = read_grayscale(args.metallic, fallback_value=0, target_size=target_size)
        smooth_l = resolve_smoothness(args, target_size)
        g_ignored = Image.new("L", target_size, 0)
        b_ignored = Image.new("L", target_size, 0)
        out_img = compose_rgba(metallic_l, g_ignored, b_ignored, smooth_l)
        save_png(out_img, args.out)
        print_summary(args.mode, {
            "R (Metallic)": args.metallic or "const 0",
            "G (ignored)": "const 0",
            "B (ignored)": "const 0",
            "A (Smoothness)": args.smoothness or (args.roughness and f"invert({args.roughness})" if args.invert_roughness else args.roughness) or f"const {args.default_smoothness}",
        }, target_size, args.out)
        return 0

    if args.mode == "urp-terrain":
        metallic_l = read_grayscale(args.metallic, fallback_value=0, target_size=target_size)
        ao_l = read_grayscale(args.occlusion, fallback_value=255, target_size=target_size)
        height_l = read_grayscale(getattr(args, "height", None), fallback_value=128, target_size=target_size)
        smooth_l = resolve_smoothness(args, target_size)
        out_img = compose_rgba(metallic_l, ao_l, height_l, smooth_l)
        save_png(out_img, args.out)
        print_summary(args.mode, {
            "R (Metallic)": args.metallic or "const 0",
            "G (AO)": args.occlusion or "const 1",
            "B (Height)": args.height or "const 0.5",
            "A (Smoothness)": args.smoothness or (args.roughness and f"invert({args.roughness})" if args.invert_roughness else args.roughness) or f"const {args.default_smoothness}",
        }, target_size, args.out)
        return 0

    if args.mode == "hdrp":
        metallic_l = read_grayscale(args.metallic, fallback_value=0, target_size=target_size)
        ao_l = read_grayscale(args.occlusion, fallback_value=255, target_size=target_size)
        detail_l = read_grayscale(getattr(args, "detail", None), fallback_value=255, target_size=target_size)
        smooth_l = resolve_smoothness(args, target_size)
        out_img = compose_rgba(metallic_l, ao_l, detail_l, smooth_l)
        save_png(out_img, args.out)
        print_summary(args.mode, {
            "R (Metallic)": args.metallic or "const 0",
            "G (AO)": args.occlusion or "const 1",
            "B (DetailMask)": args.detail or "const 1",
            "A (Smoothness)": args.smoothness or (args.roughness and f"invert({args.roughness})" if args.invert_roughness else args.roughness) or f"const {args.default_smoothness}",
        }, target_size, args.out)
        return 0

    parser.error("Unknown mode")
    return 2


def print_summary(mode: str, mapping: Dict[str, str], size: Tuple[int, int], out_path: str) -> None:
    print("Mode:", mode)
    print("Resolution:", f"{size[0]}x{size[1]}")
    print("Output:", out_path)
    print("Channel mapping:")
    for k, v in mapping.items():
        print(f"  - {k}: {v}")


def resolve_smoothness(args: argparse.Namespace, target_size: Tuple[int, int]) -> Image.Image:
    # Priority: smoothness file -> roughness (invert if requested) -> default constant
    if getattr(args, "smoothness", None):
        return read_grayscale(args.smoothness, fallback_value=int(round(clamp01(args.default_smoothness) * 255)), target_size=target_size)
    if getattr(args, "roughness", None):
        rough_l = read_grayscale(args.roughness, fallback_value=0, target_size=target_size)
        if args.invert_roughness:
            return invert_l(rough_l)
        # If not inverted, assume author already provided smoothness-like map in roughness slot
        return rough_l
    # Constant fallback
    default_val = int(round(clamp01(args.default_smoothness) * 255))
    return Image.new("L", target_size, default_val)


if __name__ == "__main__":
    sys.exit(main())

