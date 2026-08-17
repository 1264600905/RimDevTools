#!/usr/bin/env python3
"""Remove chroma-green backgrounds and normalize generated assets to 1024x1024 RGBA PNG.

Usage from the repository root:
    python .pi/scripts/process_flamethrower_green.py

Defaults:
    input:  .pi/raw-flamethrowers
    output: .pi/processed-flamethrowers
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


DEFAULT_INPUT = Path(".pi/raw-flamethrowers")
DEFAULT_OUTPUT = Path(".pi/processed-flamethrowers")


def chroma_alpha(rgb: np.ndarray) -> np.ndarray:
    """Return an alpha mask using the green screen color and edge feathering."""
    # Use int32: uint8/int16 subtraction can overflow for the green channel.
    pixels = rgb.astype(np.int32)
    # The generated background is intended to be #00FF00. Use color distance
    # rather than only G dominance so anti-aliased green edges become translucent.
    bg = np.array([0, 255, 0], dtype=np.int32)
    distance = np.sqrt(np.sum((pixels - bg) ** 2, axis=2))

    # Fully remove close green; preserve dark gray/black weapon pixels.
    low = 42.0
    high = 135.0
    distance_alpha = np.clip((distance - low) / (high - low), 0.0, 1.0)

    r = pixels[:, :, 0]
    g = pixels[:, :, 1]
    b = pixels[:, :, 2]
    green_excess = g - np.maximum(r, b)
    green_dominant = (g > 50) & (green_excess > 8)

    # Distance alone keeps dark green anti-aliased outlines opaque. For green
    # dominant pixels use chroma strength instead: saturated green is removed,
    # while neutral gray/black weapon pixels remain intact.
    chroma_alpha = np.clip((15.0 - green_excess) / 35.0, 0.0, 1.0)
    alpha = np.where(green_dominant, chroma_alpha, distance_alpha)
    return (alpha * 255.0 + 0.5).astype(np.uint8)


def despill_green(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """Neutralize green contamination on semi-transparent edge pixels."""
    out = rgb.astype(np.int32).copy()
    r = out[:, :, 0]
    g = out[:, :, 1]
    b = out[:, :, 2]
    green_dominant = (g > 50) & (g > np.maximum(r, b) + 8)
    # Green screen spill should not remain brighter than the red/blue channels
    # at the retained foreground edge.
    out[:, :, 1] = np.where(green_dominant, np.minimum(g, np.maximum(r, b) + 6), g)
    return np.clip(out, 0, 255).astype(np.uint8)


def content_centroid(image: Image.Image) -> tuple[float, float]:
    """Return the alpha-weighted content centroid in image coordinates."""
    alpha = np.asarray(image.getchannel("A"), dtype=np.float64)
    mass = alpha.sum()
    if mass <= 0:
        return image.width / 2.0, image.height / 2.0
    yy, xx = np.indices(alpha.shape, dtype=np.float64)
    return float((alpha * xx).sum() / mass), float((alpha * yy).sum() / mass)


def normalize_asset(
    source: Path,
    destination: Path,
    canvas_size: int = 1024,
    center_mode: str = "rectangle",
    vertical_offset: float = 0.0,
) -> None:
    image = Image.open(source).convert("RGB")
    rgb = np.asarray(image)
    alpha = chroma_alpha(rgb)
    rgb = despill_green(rgb, alpha)

    # Slightly soften only the key edge to avoid a harsh green fringe.
    alpha_image = Image.fromarray(alpha, mode="L").filter(ImageFilter.GaussianBlur(radius=0.7))
    rgba = Image.fromarray(np.dstack((rgb, np.asarray(alpha_image))), mode="RGBA")

    # Find the non-background object and crop away the green-screen margins.
    bbox = alpha_image.point(lambda value: 255 if value > 12 else 0).getbbox()
    if bbox is None:
        raise RuntimeError(f"No foreground detected in {source}")
    rgba = rgba.crop(bbox)

    # Keep generous transparent margins while fitting the whole weapon in a
    # deterministic square 1024x1024 canvas.
    margin = 0.08
    target_max = int(canvas_size * (1.0 - 2.0 * margin))
    scale = min(target_max / rgba.width, target_max / rgba.height)
    new_size = (max(1, round(rgba.width * scale)), max(1, round(rgba.height * scale)))
    rgba = rgba.resize(new_size, Image.Resampling.LANCZOS)

    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    if center_mode == "optical":
        # Optional mode for silhouettes whose visual mass is more useful than
        # the outer content rectangle.
        cx, cy = content_centroid(rgba)
        position = (
            round(canvas_size / 2.0 - cx),
            round(canvas_size / 2.0 - cy + vertical_offset),
        )
    else:
        # Default: center the actual foreground rectangle exactly on both axes.
        # This is predictable for game assets and avoids horizontal drift caused
        # by an asymmetric stock, magazine, or grip.
        position = (
            round((canvas_size - rgba.width) / 2.0),
            round((canvas_size - rgba.height) / 2.0 + vertical_offset),
        )
    canvas.alpha_composite(rgba, position)
    destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(destination, format="PNG", optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--center-mode",
        choices=("rectangle", "optical", "bbox"),
        default="rectangle",
        help="Center the foreground rectangle (default) or use alpha-mass optical centering.",
    )
    parser.add_argument(
        "--vertical-offset",
        type=float,
        default=0.0,
        help="Additional output-pixel shift; positive values move the asset down.",
    )
    args = parser.parse_args()

    sources = sorted(args.input_dir.glob("*.png"))
    if not sources:
        raise SystemExit(f"No PNG files found in {args.input_dir}")

    for source in sources:
        destination = args.output_dir / source.name
        normalize_asset(
            source,
            destination,
            center_mode=args.center_mode,
            vertical_offset=args.vertical_offset,
        )
        print(
            f"processed: {source} -> {destination} "
            f"(center={args.center_mode}, y_offset={args.vertical_offset:g})"
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
