"""Image handling: fetch bytes, prove they are an image, normalize them.

Two rules the app depends on:

1. The bytes MUST be an image. The app's own failure mode was a
   "web.archive.org" html wrapper being handed to ImageSharp, which then
   failed with "Image cannot be loaded. Available decoders: ...". Here an
   html/json body raises `NotAnImage` with the url and the first bytes, so
   the failure names the culprit instead of a decoder list.

2. The bytes MUST be a format the app can decode. The app decodes with
   ImageSharp, which has no AVIF decoder, and several sites serve AVIF
   (wallpapercave's /mwp/ path, desktopnexus' imgix `format=auto` when the
   client offers image/avif). Every asset is re-encoded to JPEG here, so the
   app never sees a format it cannot decode.

Videos (mp4/webm/mov) are NOT images: some categories (alphacoders
"comic") serve an animated file as the item's thumbnail. Those items are
rejected loudly, not silently skipped.
"""

from __future__ import annotations

import io
from dataclasses import dataclass

from PIL import Image

from .errors import NotAnImage
from .transport import Transport

# magic bytes -> format name
_MAGIC = (
    (b"\xff\xd8\xff", "jpeg"),
    (b"\x89PNG\r\n\x1a\n", "png"),
    (b"GIF87a", "gif"),
    (b"GIF89a", "gif"),
    (b"BM", "bmp"),
    (b"II*\x00", "tiff"),
    (b"MM\x00*", "tiff"),
    (b"RIFF", "webp"),  # refined below (webp vs wav)
)

IMAGE_FORMATS = {"jpeg", "png", "webp", "gif", "bmp", "tiff"}
VIDEO_FORMATS = {"mp4", "webm", "mov"}


@dataclass(frozen=True)
class ImageBytes:
    data: bytes
    format: str
    width: int | None = None
    height: int | None = None


def sniff_format(data: bytes) -> str:
    """Return the format name, or 'html' / 'unknown' / a video name."""
    if not data:
        return "empty"
    if data[:4] == b"\x1a\x45\xdf\xa3":  # matroska / webm
        return "webm"
    if data[4:8] == b"ftyp":
        brand = data[8:12].lower()
        if brand.startswith(b"avif") or brand.startswith(b"avis"):
            return "avif"
        if brand.startswith(b"heic") or brand.startswith(b"heix"):
            return "heic"
        if brand.startswith(b"mp4"):
            return "mp4"
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return "webp"
    for magic, name in _MAGIC:
        if data.startswith(magic):
            return name
    head = data[:512].lstrip()[:1].lower()
    if head in (b"<",):
        return "html"
    if head in (b"{", b"["):
        return "json"
    if data[:4] == b"\x00\x00\x00 ftyp" or data[4:8] == b"ftyp":
        return "mp4"
    return "unknown"


def describe_sniff(data: bytes) -> str:
    """A short, human-readable description of what the bytes actually are."""
    fmt = sniff_format(data)
    if fmt in IMAGE_FORMATS | VIDEO_FORMATS:
        return fmt
    head = data[:120].decode("utf-8", errors="replace").replace("\n", " ")
    return f"{fmt} (starts with: {head!r})"


def fetch_image(
    transport: Transport,
    url: str,
    referer: str | None = None,
) -> ImageBytes:
    """Fetch an image and PROVE it is one. Raises NotAnImage otherwise."""
    data = transport.get_bytes(url, referer=referer)
    fmt = sniff_format(data)

    if fmt in VIDEO_FORMATS:
        raise NotAnImage(
            f"{url} serves a {fmt} video, not an image - the app cannot decode "
            f"it as a wallpaper thumbnail."
        )
    if fmt not in IMAGE_FORMATS:
        raise NotAnImage(f"{url} returned {describe_sniff(data)}")

    width = height = None
    try:
        with Image.open(io.BytesIO(data)) as image:
            width, height = image.size
    except Exception as exc:  # pragma: no cover - corrupt image
        raise NotAnImage(f"{url} looks like a {fmt} but Pillow cannot read it: {exc}")

    return ImageBytes(data=data, format=fmt, width=width, height=height)


def to_jpeg(
    image: ImageBytes,
    max_width: int | None = 1800,
    quality: int = 88,
) -> tuple[bytes, int, int]:
    """Re-encode to JPEG so the app always gets a decodable format."""
    with Image.open(io.BytesIO(image.data)) as opened:
        if opened.mode in ("RGBA", "LA", "P"):
            opened = opened.convert("RGBA")
            background = Image.new("RGB", opened.size, (0, 0, 0))
            background.paste(opened, mask=opened.split()[-1])
            opened = background
        else:
            opened = opened.convert("RGB")

        if max_width and opened.width > max_width:
            ratio = max_width / opened.width
            opened = opened.resize(
                (max_width, max(1, int(opened.height * ratio))),
                Image.LANCZOS,
            )

        buffer = io.BytesIO()
        opened.save(buffer, format="JPEG", quality=quality, optimize=True)
        return buffer.getvalue(), opened.width, opened.height


def save_jpeg(
    path,
    image: ImageBytes,
    max_width: int | None = 1800,
    quality: int = 88,
) -> tuple[int, int, int]:
    """Write a normalized jpeg next to its source metadata. Returns (bytes, w, h)."""
    jpeg, width, height = to_jpeg(image, max_width=max_width, quality=quality)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(jpeg)
    return len(jpeg), width, height
