"""Normalise the vendor marks in docs/design/marks into single-colour glyphs.

Reads the original SVGs, keeps only the strokes that form the mark, drops backgrounds,
gradients and filters, maps shaded faces to opacities of one colour, and writes:
  docs/design/marks/normalized/<id>.svg   currentColor, square viewBox
  docs/design/marks/normalized/marks.json path data per mark, for the apps
  docs/design/marks/normalized/opencode.png raster mask when no vector exists
Run from the repo root: python tools/marks.py
"""
import base64
import json
import os
import re

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "docs", "design", "marks")
OUT = os.path.join(ROOT, "normalized")
os.makedirs(OUT, exist_ok=True)


def read(name):
    return open(os.path.join(ROOT, name), encoding="utf-8", errors="replace").read()


def attr(tag, name):
    m = re.search(r'\b%s="([^"]*)"' % name, tag)
    return m.group(1) if m else None


def polygon_to_path(points):
    nums = [float(x) for x in re.findall(r"-?\d*\.?\d+(?:e-?\d+)?", points)]
    pairs = list(zip(nums[0::2], nums[1::2]))
    return "M" + " L".join("%g,%g" % p for p in pairs) + " Z"


def shapes_from(svg, keep=None):
    """Every <path d> and <polygon points> outside <defs>, as shape dicts."""
    body = re.sub(r"<defs>.*?</defs>", "", svg, flags=re.S)
    body = re.sub(r"<clipPath.*?</clipPath>", "", body, flags=re.S)
    out = []
    for m in re.finditer(r"<(path|polygon)\b([^>]*)/?>", body):
        tag, attrs = m.group(1), m.group(2)
        if keep and not keep(attrs):
            continue
        d = attr(attrs, "d") if tag == "path" else polygon_to_path(attr(attrs, "points") or "")
        if d:
            out.append({"d": re.sub(r"\s+", " ", d).strip(), "opacity": 1.0, "fillRule": "nonzero"})
    return out


def rounded_rect(x, y, w, h, r):
    return ("M%g,%g h%g a%g,%g 0 0 1 %g,%g v%g a%g,%g 0 0 1 %g,%g h%g a%g,%g 0 0 1 %g,%g v%g a%g,%g 0 0 1 %g,%g z"
            % (x + r, y, w - 2 * r, r, r, r, r, h - 2 * r, r, r, -r, r, -(w - 2 * r), r, r, -r, -r, -(h - 2 * r), r, r, r, -r))


MARKS = {}


def emit(mark_id, viewbox, shapes):
    x, y, w, h = (float(v) for v in viewbox.split())
    side = max(w, h)
    dx = x - (side - w) / 2
    dy = y - (side - h) / 2
    MARKS[mark_id] = {"viewBox": [dx, dy, side, side], "shapes": shapes}
    paths = ""
    for s in shapes:
        extra = "" if s["opacity"] == 1 else ' opacity="%s"' % s["opacity"]
        if s["fillRule"] == "evenodd":
            extra += ' fill-rule="evenodd"'
        paths += '<path d="%s"%s/>' % (s["d"], extra)
    svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="%g %g %g %g" fill="currentColor">%s</svg>' % (dx, dy, side, side, paths)
    open(os.path.join(OUT, mark_id + ".svg"), "w", encoding="utf-8").write(svg)


# Claude: the sunburst, one path.
svg = read("claude.svg")
emit("claude", attr(svg, "viewBox"), shapes_from(svg))

# Codex: the cloud with the prompt cut out, one path with a gradient we drop.
svg = read("codex.svg")
emit("codex", attr(svg, "viewBox"), shapes_from(svg))

# Copilot: three black paths; the clip rectangles are the artboard.
svg = read("copilot.svg")
emit("copilot", attr(svg, "viewBox"), shapes_from(svg))

# Cursor: the shaded cube needs three tones, which a one-colour glyph cannot carry; the
# official monochrome mark (cube outline with the inner face) reads at 16 pt instead.
svg = read("cursor.simpleicons.svg")
shapes = shapes_from(svg)
for s in shapes:
    s["fillRule"] = "evenodd"
emit("cursor", attr(svg, "viewBox"), shapes)

# Antigravity: blurred colour fields clipped to an arch; the arch outline is the mark.
svg = read("antigravity.svg")
clip = re.search(r"<clipPath[^>]*>\s*<path d=\"([^\"]+)\"", svg).group(1)
emit("antigravity", attr(svg, "viewBox"), [{"d": clip, "opacity": 1.0, "fillRule": "nonzero"}])

# GLM: a dark rounded square with a white Z. In one colour the square becomes a ring and the Z stays solid.
svg = read("glm.svg")
z = shapes_from(svg, keep=lambda a: (attr(a, "class") or "") == "st23")
ring = {"d": rounded_rect(1.49, 1.49, 27.02, 27.02, 4) + " " + rounded_rect(3.29, 3.29, 23.42, 23.42, 2.6), "opacity": 1.0, "fillRule": "evenodd"}
emit("glm", attr(svg, "viewBox"), [ring] + z)

# Grok: one path.
svg = read("grok.svg")
emit("grok", attr(svg, "viewBox"), shapes_from(svg))

# OpenCode: only a raster exists; keep it as an alpha mask the apps tint.
svg = read("opencode.svg")
png = re.search(r'href="data:image/png;base64,([^"]+)"', svg).group(1)
open(os.path.join(OUT, "opencode.png"), "wb").write(base64.b64decode(png))
MARKS["opencode"] = {"raster": "opencode.png"}

json.dump(MARKS, open(os.path.join(OUT, "marks.json"), "w", encoding="utf-8"), indent=1)
print("wrote", sorted(MARKS))
