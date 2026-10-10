"""Builds the README's animated SVGs, a light and a dark copy of each.

    python docs/assets/build-svgs.py

Colours are Paper's palette from windows/src/VoxScribe.App/Design/ThemeCatalog.cs, and the
fonts are the app's own bundled faces, subset to the glyphs used and embedded as base64 WOFF:
an SVG shown through <img> cannot fetch web fonts. #E5484D is the recording dot and nothing
else, as in the app. Needs fontTools (pip install fonttools).
"""

import base64
import io
import pathlib

from fontTools import subset
from fontTools.ttLib import TTFont

HERE = pathlib.Path(__file__).parent
FONTS = HERE.parents[1] / "windows/src/VoxScribe.App/Assets/Fonts"

PALETTES = {
    "light": dict(ground="#F4EFE6", surface="#FBF8F2", card="#FFFFFF", edge="#E3DBCD",
                  hover="#EAE2D3", ink="#1E1A15", muted="#6B6153", accent="#6A3D9A",
                  shadow="rgba(30,26,21,.10)"),
    "dark": dict(ground="#17130F", surface="#211B16", card="#2A231C", edge="#3A322A",
                 hover="#2C251E", ink="#EFE7DA", muted="#A89A86", accent="#C3A4EC",
                 shadow="rgba(0,0,0,.45)"),
}
RED = "#E5484D"

# Every string drawn below, so each subset carries exactly the glyphs it needs.
SERIF = "Voxscribe listening typing"
SANS = ("Hold a key, speak, release. Clean text lands wherever you type. "
        "Notes — Team update Hi all, Let's ship the release notes on Thursday, after the design review. "
        "Right Ctrl 1 2 3 Hold Speak Release — it's typed Push-to-talk dictation for Windows "
        "On-device by default 0123456789 ’")
MONO = "PUSH-TO-TALK DICTATION · FOR WINDOWS REC CLEAN RAW 0:07 ON-DEVICE SPEECH · FIVE THEMES"


def face(path, text, family, style="normal", weight=400):
    font = TTFont(FONTS / path)
    options = subset.Options()
    options.flavor = "woff"
    options.layout_features = ["kern", "liga"]
    sub = subset.Subsetter(options)
    sub.populate(text=text + " ")
    sub.subset(font)
    buf = io.BytesIO()
    font.flavor = "woff"
    font.save(buf)
    data = base64.b64encode(buf.getvalue()).decode()
    return (f"@font-face{{font-family:'{family}';font-style:{style};font-weight:{weight};"
            f"src:url(data:font/woff;base64,{data}) format('woff')}}")


FONT_CSS = "".join([
    face("InstrumentSerif/InstrumentSerif-Regular.ttf", SERIF, "VS Serif"),
    face("InstrumentSerif/InstrumentSerif-Italic.ttf", SERIF, "VS Serif", "italic"),
    face("Geist/Geist-Regular.ttf", SANS, "VS Sans"),
    face("Geist/Geist-Medium.ttf", SANS, "VS Sans", weight=500),
    face("GeistMono/GeistMono-Regular.ttf", MONO, "VS Mono"),
])

BASE_CSS = """
.serif{font-family:'VS Serif',Georgia,serif}
.sans{font-family:'VS Sans','Segoe UI',Helvetica,Arial,sans-serif}
.mono{font-family:'VS Mono',Consolas,monospace;letter-spacing:.08em}
.bar{transform-box:fill-box;transform-origin:center;animation:wave 1.1s ease-in-out infinite alternate}
.dot{transform-box:fill-box;transform-origin:center;animation:pulse 1.4s ease-in-out infinite}
@keyframes wave{from{transform:scaleY(.25)}to{transform:scaleY(1)}}
@keyframes pulse{0%,100%{opacity:1;transform:scale(1)}50%{opacity:.55;transform:scale(.82)}}
"""


def bars(x, y, count, gap, width, height, color):
    """A waveform: bars of varied height, each bouncing on its own phase."""
    out = []
    for i in range(count):
        h = height * (0.35 + 0.65 * abs(((i * 7) % 11) / 10 - 0.5) * 2)
        delay = -((i * 0.37) % 1.1)
        out.append(f'<rect class="bar" x="{x + i * gap}" y="{y - h / 2:.1f}" width="{width}" '
                   f'height="{h:.1f}" rx="{width / 2}" fill="{color}" style="animation-delay:{delay:.2f}s"/>')
    return "".join(out)


def keycap(x, y, label, c, cls="key"):
    return f"""<g class="{cls}">
  <rect x="{x}" y="{y + 6}" width="150" height="58" rx="12" fill="{c['edge']}"/>
  <g class="keytop"><rect x="{x}" y="{y}" width="150" height="58" rx="12" fill="{c['card']}" stroke="{c['edge']}" stroke-width="1.5"/>
  <text x="{x + 75}" y="{y + 36}" text-anchor="middle" class="sans" font-size="19" font-weight="500" fill="{c['ink']}">{label}</text></g>
</g>"""


def banner(c):
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="420" viewBox="0 0 1280 420" role="img" aria-label="VoxScribe — push-to-talk dictation for Windows">
<style>{FONT_CSS}{BASE_CSS}
.glow{{transform-box:fill-box;transform-origin:center;animation:drift 14s ease-in-out infinite alternate}}
@keyframes drift{{from{{transform:translate(-30px,10px) scale(1)}}to{{transform:translate(40px,-20px) scale(1.15)}}}}
.keytop{{animation:press 2.8s ease-in-out infinite}}
@keyframes press{{0%,12%,88%,100%{{transform:translateY(0)}}18%,82%{{transform:translateY(5px)}}}}
.pill{{animation:float 6s ease-in-out infinite alternate}}
@keyframes float{{from{{transform:translateY(0) rotate(-2deg)}}to{{transform:translateY(-8px) rotate(-1.4deg)}}}}
@media (prefers-reduced-motion:reduce){{*{{animation:none!important}}}}
</style>
<defs>
  <radialGradient id="g" cx="50%" cy="50%" r="50%"><stop offset="0" stop-color="{c['accent']}" stop-opacity=".22"/><stop offset="1" stop-color="{c['accent']}" stop-opacity="0"/></radialGradient>
  <filter id="s" x="-20%" y="-40%" width="140%" height="180%"><feDropShadow dx="0" dy="14" stdDeviation="16" flood-color="{c['shadow']}"/></filter>
  <clipPath id="frame"><rect width="1280" height="420" rx="24"/></clipPath>
</defs>
<g clip-path="url(#frame)">
  <rect width="1280" height="420" fill="{c['ground']}"/>
  <ellipse class="glow" cx="960" cy="200" rx="380" ry="260" fill="url(#g)"/>
  <line x1="64" y1="70" x2="1216" y2="70" stroke="{c['edge']}"/>
  <line x1="64" y1="350" x2="1216" y2="350" stroke="{c['edge']}"/>
  <text x="64" y="52" class="mono" font-size="13" fill="{c['muted']}">PUSH-TO-TALK DICTATION · FOR WINDOWS</text>
  <text x="1216" y="52" text-anchor="end" class="mono" font-size="13" fill="{c['muted']}">ON-DEVICE SPEECH · FIVE THEMES</text>

  <text x="60" y="222" class="serif" font-size="140" fill="{c['ink']}">Vox<tspan font-style="italic" fill="{c['accent']}">scribe</tspan></text>
  <text x="66" y="290" class="sans" font-size="26" fill="{c['muted']}">Hold a key, speak, release. Clean text lands wherever you type.</text>

  <g class="pill" style="transform-origin:960px 170px">
    <g filter="url(#s)"><rect x="770" y="118" width="380" height="104" rx="6" fill="{c['surface']}" stroke="{c['edge']}"/></g>
    <circle class="dot" cx="812" cy="170" r="10" fill="{RED}"/>
    <text x="838" y="182" class="serif" font-size="34" font-style="italic" fill="{c['ink']}">listening</text>
    {bars(990, 170, 14, 10.5, 5, 46, c['accent'])}
  </g>
  {keycap(885, 254, "Right Ctrl", c)}
  <text x="64" y="388" class="mono" font-size="13" fill="{c['muted']}">REC · CLEAN</text>
  <text x="1216" y="388" text-anchor="end" class="mono" font-size="13" fill="{c['muted']}">0:07</text>
</g>
</svg>"""


def demo(c):
    # One 10 s loop: idle, hold the key (pill listens), release, the line is typed, reset.
    line = "Let's ship the release notes on Thursday, after the design review."
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="600" viewBox="0 0 1280 600" role="img" aria-label="Hold Right Ctrl, speak, release: the sentence is typed into the focused window">
<style>{FONT_CSS}{BASE_CSS}
.loop{{animation-duration:10s;animation-iteration-count:infinite;animation-timing-function:ease-in-out}}
.keytop{{animation-name:hold}}
@keyframes hold{{0%,8%,50%,100%{{transform:translateY(0)}}10%,48%{{transform:translateY(5px)}}}}
.pillbox{{animation-name:pill}}
@keyframes pill{{0%,8%{{opacity:0;transform:translateY(16px)}}12%,56%{{opacity:1;transform:translateY(0)}}62%,100%{{opacity:0;transform:translateY(16px)}}}}
.listen{{animation-name:listen}}
@keyframes listen{{0%,49%{{opacity:1}}51%,100%{{opacity:0}}}}
.typing{{animation-name:typing}}
@keyframes typing{{0%,49%{{opacity:0}}51%,100%{{opacity:1}}}}
.cover{{animation-name:reveal}}
@keyframes reveal{{0%,54%{{transform:translateX(0)}}80%,100%{{transform:translateX(768px)}}}}
.typed{{animation-name:clear}}
@keyframes clear{{0%,93%{{opacity:1}}97%,100%{{opacity:0}}}}
.caret{{animation:blink 1s steps(1) infinite}}
@keyframes blink{{50%{{opacity:0}}}}
.s1{{animation-name:s1}} .s2{{animation-name:s2}} .s3{{animation-name:s3}}
@keyframes s1{{0%,6%,52%,100%{{opacity:.35}}9%,48%{{opacity:1}}}}
@keyframes s2{{0%,12%,52%,100%{{opacity:.35}}15%,48%{{opacity:1}}}}
@keyframes s3{{0%,50%,92%,100%{{opacity:.35}}54%,88%{{opacity:1}}}}
@media (prefers-reduced-motion:reduce){{*{{animation:none!important}}.cover{{display:none}}.pillbox{{opacity:0}}}}
</style>
<defs>
  <filter id="s" x="-10%" y="-20%" width="120%" height="150%"><feDropShadow dx="0" dy="16" stdDeviation="18" flood-color="{c['shadow']}"/></filter>
  <clipPath id="frame"><rect width="1280" height="600" rx="24"/></clipPath>
  <clipPath id="doc"><rect x="141" y="89" width="998" height="254"/></clipPath>
</defs>
<g clip-path="url(#frame)">
  <rect width="1280" height="600" fill="{c['ground']}"/>

  <g filter="url(#s)"><rect x="140" y="44" width="1000" height="300" rx="12" fill="{c['card']}" stroke="{c['edge']}"/></g>
  <rect x="140.5" y="44.5" width="999" height="44" rx="12" fill="{c['surface']}"/>
  <rect x="140.5" y="70" width="999" height="18" fill="{c['surface']}"/>
  <line x1="140" y1="88" x2="1140" y2="88" stroke="{c['edge']}"/>
  <text x="164" y="72" class="sans" font-size="15" fill="{c['muted']}">Notes — Team update</text>
  <g stroke="{c['muted']}" stroke-width="1.4" fill="none">
    <line x1="1032" y1="66" x2="1044" y2="66"/><rect x="1068" y="60" width="11" height="11"/>
    <line x1="1104" y1="60" x2="1115" y2="71"/><line x1="1115" y1="60" x2="1104" y2="71"/>
  </g>
  <text x="190" y="150" class="sans" font-size="26" fill="{c['ink']}">Hi all,</text>
  <g clip-path="url(#doc)">
  <g class="typed loop">
    <text x="190" y="206" class="sans" font-size="26" fill="{c['ink']}">{line.replace("'", "&#8217;")}</text>
  </g>
  <g class="cover loop">
    <rect x="186" y="178" width="960" height="40" fill="{c['card']}"/>
    <rect class="caret" x="187" y="180" width="2.5" height="34" fill="{c['accent']}"/>
  </g>
  </g>

  <g class="pillbox loop">
    <g filter="url(#s)"><rect x="470" y="372" width="340" height="76" rx="6" fill="{c['surface']}" stroke="{c['edge']}"/></g>
    <g class="listen loop">
      <circle class="dot" cx="504" cy="410" r="9" fill="{RED}"/>
      <text x="526" y="420" class="serif" font-size="28" font-style="italic" fill="{c['ink']}">listening</text>
    </g>
    <g class="typing loop">
      <text x="498" y="420" class="serif" font-size="28" font-style="italic" fill="{c['muted']}">typing</text>
    </g>
    {bars(668, 410, 12, 10.5, 5, 36, c['accent'])}
  </g>

  {keycap(140, 478, "Right Ctrl", c).replace('class="keytop"', 'class="keytop loop"')}
  <g class="sans" font-size="20">
    <g class="s1 loop"><text x="360" y="516" fill="{c['accent']}" font-weight="500">1</text><text x="384" y="516" fill="{c['ink']}">Hold Right Ctrl</text></g>
    <g class="s2 loop"><text x="600" y="516" fill="{c['accent']}" font-weight="500">2</text><text x="624" y="516" fill="{c['ink']}">Speak</text></g>
    <g class="s3 loop"><text x="760" y="516" fill="{c['accent']}" font-weight="500">3</text><text x="784" y="516" fill="{c['ink']}">Release — it&#8217;s typed</text></g>
  </g>
</g>
</svg>"""


for mode, palette in PALETTES.items():
    for name, build in (("banner", banner), ("demo", demo)):
        path = HERE / f"{name}-{mode}.svg"
        path.write_text(build(palette), encoding="utf-8")
        print(path.name, f"{path.stat().st_size // 1024} KB")
