"""
"Learn ModForge" as a page and a PDF (1.10.0): the tutorials' story, "Dummy Days", step by step with a picture of
each step as the editor shows it, then the guide. Read from what the editor itself reads - the tutorials' catalog
(SMSModForge/Tutorials/*.cs), the guide's (Documentation/DocTopics.cs) and the English file - so the page says
what ships.

  python -I Tools/Docs/learnpage.py <step pictures folder> [out folder]

The step pictures are TutorialPictures.EveryStep's (SMSMODFORGE_SHOT_DIR + SMSMODFORGE_TUTORIAL_SHOTS=all), named
LL-id-NN-Theme.png. The out folder (default Tools/Docs/out, not tracked) gets learn-modforge.html, its img folder
and learn-modforge.pdf (printed by Microsoft Edge, headless, when it is installed).
"""
import io, os, re, sys, html, json, shutil, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
ED = os.path.join(REPO, "SMSModForge")
shots = sys.argv[1] if len(sys.argv) > 1 else ""
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "out")
os.makedirs(os.path.join(out, "img"), exist_ok=True)
e = html.escape

en = {}
for l in io.open(os.path.join(ED, "Languages", "en.txt"), encoding="utf-8-sig"):
    m = re.match(r"^([A-Za-z0-9_.\-]+) = (.*)$", l.rstrip("\r\n"))
    if m: en[m.group(1)] = m.group(2).replace("\\n", "\n")
T = lambda k: en.get(k, "")

# ── The tutorials, in ladder order ───────────────────────────────────────────
src = ""
for f in ("TutorialCatalog.cs", "TutorialsPart2.cs", "TutorialsUi.cs", "TutorialsQuests.cs", "TutorialsShow.cs", "TutorialsDeep.cs",
          "TutorialsMore.cs"):
    src += io.open(os.path.join(ED, "Tutorials", f), encoding="utf-8-sig").read() + "\n"
tutorials = []
for m in re.finditer(r'\bId = "([a-z\-]+)",', src):
    tid = m.group(1)
    if tid == "smoke": continue
    nxt = re.search(r"\n        new TutorialDef\n|\n    };", src[m.start():])
    block = src[m.start(): m.start() + nxt.start()] if nxt else src[m.start():]
    level = int(re.search(r"Level = (\d+),", block).group(1))
    # TutorialsDeep.cs names its group by a constant (Group = Group): "Going deeper".
    gm = re.search(r'Group = "([^"]+)"', block)
    group = gm.group(1) if gm else "tutorial.group.deeper"
    steps = []
    for sb in re.split(r"new TutorialStep\b", block)[1:]:
        key = lambda n: (re.search(n + r' = "([^"]+)"', sb) or [None, ""])[1]
        steps.append(dict(kind=re.search(r"Kind = StepKind\.(\w+)", sb).group(1), title=T(key("Title")),
                          body=T(key("Body")), more=T(key("More"))))
    tutorials.append(dict(level=level, id=tid, groupkey=group.replace("tutorial.group.", ""), group=T(group), title=T(re.search(r'Title = "([^"]+)"', block).group(1)),
                          summary=T(re.search(r'Summary = "([^"]+)"', block).group(1)), steps=steps))
tutorials.sort(key=lambda t: t["level"])

def picture(t, n):
    """The step's picture, made smaller into the page's img folder; its name there, or None."""
    if not shots: return None
    for theme in ("Light", "Frost", "Neon"):
        p = os.path.join(shots, "%02d-%s-%02d-%s.png" % (t["level"], t["id"], n, theme))
        if os.path.exists(p): break
    else:
        return None
    name = "%02d-%s-%02d.jpg" % (t["level"], t["id"], n)
    dst = os.path.join(out, "img", name)
    try:
        from PIL import Image
        im = Image.open(p).convert("RGB")
        # Closer in on what the step is about: the tutorial's ring and its box are drawn in the tutorial's own
        # colour (#FFC428); the picture is cut to both, with a margin, when they are not most of it already.
        try:
            import numpy as np
            a = np.asarray(im).astype(int)
            mask = (abs(a[:, :, 0] - 255) <= 6) & (abs(a[:, :, 1] - 196) <= 8) & (abs(a[:, :, 2] - 40) <= 10)
            ys, xs = np.nonzero(mask)
            if len(xs) > 200:
                pad = 28
                x0, x1 = max(0, xs.min() - pad), min(im.width, xs.max() + pad)
                y0, y1 = max(0, ys.min() - pad), min(im.height, ys.max() + pad)
                if (x1 - x0) * (y1 - y0) < 0.8 * im.width * im.height:
                    im = im.crop((int(x0), int(y0), int(x1), int(y1)))
        except ImportError:
            pass
        w = min(960, im.width)
        im = im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)
        im.save(dst, "JPEG", quality=78, optimize=True)
    except ImportError:
        name = name[:-4] + ".png"; shutil.copyfile(p, os.path.join(out, "img", name))
    return name

def art(name, src_dir, width):
    """A picture of the editor's Resources (a tutorial's banner, a guide topic's), smaller, into img; its name or None."""
    for ext in (".jpg", ".png"):
        p = os.path.join(ED, "Resources", src_dir, name + ext)
        if os.path.exists(p): break
    else:
        return None
    dst_name = src_dir.lower() + "-" + name.replace("/", "-") + ".jpg"
    try:
        from PIL import Image
        im = Image.open(p).convert("RGB")
        if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
        im.save(os.path.join(out, "img", dst_name), "JPEG", quality=80, optimize=True)
    except ImportError:
        dst_name = name + ext; shutil.copyfile(p, os.path.join(out, "img", dst_name))
    return dst_name

def para(text):
    return "".join("<p>%s</p>" % e(p) for p in text.split("\n\n") if p.strip())

body = []
toc = []
last_group = None
for t in tutorials:
    if t["group"] != last_group:
        toc.append('<li class="g">%s</li>' % e(t["group"]))
        last_group = t["group"]
    toc.append('<li><a href="#t-%s"><span class="n">%d</span> %s</a></li>' % (t["id"], t["level"], e(t["title"])))
    body.append('<section class="tutorial" id="t-%s">' % t["id"])
    banner = art(t["id"], "TutorialHeaders", 960) or art(t["groupkey"], "TutorialHeaders", 960)
    if banner:
        body.append('<img class="banner" src="img/%s" alt="">' % banner)
    body.append('<p class="eyebrow">%s &middot; Tutorial %d</p><h2>%s</h2><p class="lede">%s</p>'
                % (e(t["group"]), t["level"], e(t["title"]), e(t["summary"])))
    for n, s in enumerate(t["steps"], 1):
        img = picture(t, n)
        kind = {"Read": "Read", "Do": "Do", "Free": "Your call"}.get(s["kind"], s["kind"])
        body.append('<article class="step"><div class="words"><p class="num"><span>%d</span>%s</p><h3>%s</h3>%s'
                    % (n, e(kind), e(s["title"]), para(s["body"])))
        if s["more"]:
            body.append("<details><summary>Tell me more</summary>%s</details>" % para(s["more"]))
        body.append("</div>")
        if img:
            body.append('<figure><img src="img/%s" alt="%s" loading="lazy"></figure>' % (img, e(s["title"])))
        body.append("</article>")
    body.append("</section>")

# ── The guide ────────────────────────────────────────────────────────────────
gsrc = io.open(os.path.join(ED, "Documentation", "DocTopics.cs"), encoding="utf-8-sig").read()
gsrc = gsrc[gsrc.index("Parts { get; } = new[]"):]
guide = []
tok = re.compile(r'new Doc(Part|Topic|Section|Bullet)\(\s*"([^"]*)"(?:\s*,\s*"([^"]*)")?')
for m in tok.finditer(gsrc):
    kind, a, b = m.group(1), m.group(2), m.group(3)
    if kind == "Part": guide.append(("part", T(a)))
    elif kind == "Topic": guide.append(("topic", T(a), T(b) if b else "", a.replace("docs.", "").replace(".title", "")))
    elif kind == "Section": guide.append(("section", T(a), a))
    else:
        term, textkey = (a, b) if b is not None else ("", a)
        guide.append(("bullet", T(term), T(textkey), T(textkey + ".more"), textkey))
g = ['<section class="guide" id="guide"><p class="eyebrow">Reference</p><h2>The guide</h2>'
     '<p class="lede">Every tab and what is on it - the same guide as the ModForge tab\'s.</p>']
open_topic = False
pending_pic = ""
for item in guide:
    if item[0] == "part":
        if open_topic: g.append("</div>"); open_topic = False
        g.append('<h3 class="part">%s</h3>' % e(item[1]))
    elif item[0] == "topic":
        if open_topic: g.append("</div>")
        g.append('<div class="topic"><h4>%s</h4><p class="summary">%s</p>' % (e(item[1]), e(item[2])))
        shot = art(item[3], "GuidePictures", 900) if len(item) > 3 else None
        if shot:
            g.append('<figure class="guidepic"><img src="img/%s" alt="" loading="lazy"></figure>' % shot)
        open_topic = True
    elif item[0] == "section":
        if item[1]: g.append('<h5>%s</h5>' % e(item[1]))
        # A section's own picture (Resources/GuidePictures/Sections), named by its heading's key, or by its first
        # bullet's when it has no heading - as DocSection.PictureName.
        pending_pic = item[2][5:] if item[2].startswith("docs.") else ""
        if pending_pic:
            pic = art("Sections/" + pending_pic, "GuidePictures", 640)
            if pic: g.append('<figure class="sectionpic"><img src="img/%s" alt="" loading="lazy"></figure>' % pic)
            pending_pic = ""
        else:
            pending_pic = None   # taken from the first bullet below
    else:
        _, term, text, more, textkey = item
        if pending_pic is None:
            pic = art("Sections/" + (textkey[5:] if textkey.startswith("docs.") else textkey), "GuidePictures", 640)
            if pic: g.append('<figure class="sectionpic"><img src="img/%s" alt="" loading="lazy"></figure>' % pic)
            pending_pic = ""
        g.append('<p class="bullet">%s%s%s</p>' % ("<b>%s</b> - " % e(term) if term else "", e(text),
                 "<details><summary>More</summary>%s</details>" % para(more) if more else ""))
if open_topic: g.append("</div>")
g.append("</section>")

page = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Learn ModForge</title>
<style>
  :root { --ground:#F6F5F8; --surface:#FFFFFF; --ink:#1F1B29; --muted:#625C70; --accent:#B4386E; --rule:#E3DEE9; --chip:#F1E7EE; }
  @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
    --ground:#16131C; --surface:#201B29; --ink:#ECE6F2; --muted:#A79FB3; --accent:#F07AAE; --rule:#332B3F; --chip:#2C2236; } }
  :root[data-theme="dark"] { --ground:#16131C; --surface:#201B29; --ink:#ECE6F2; --muted:#A79FB3; --accent:#F07AAE; --rule:#332B3F; --chip:#2C2236; }
  * { box-sizing:border-box; }
  body { margin:0; background:var(--ground); color:var(--ink); font:16px/1.6 "Segoe UI", system-ui, sans-serif; }
  .wrap { max-width:62rem; margin:0 auto; padding:3rem 16px 5rem; }
  header { border-bottom:2px solid var(--accent); padding-bottom:1.2rem; margin-bottom:2rem; }
  h1 { font-size:2.2rem; line-height:1.15; margin:.2rem 0 .6rem; letter-spacing:-.02em; }
  .eyebrow { font-size:.72rem; letter-spacing:.14em; text-transform:uppercase; color:var(--accent); font-weight:600; margin:0; }
  .lede { color:var(--muted); margin:0 0 1.5rem; max-width:44rem; }
  nav ol { columns:2; column-gap:2rem; list-style:none; padding:0; margin:0 0 3rem; }
  nav li { break-inside:avoid; padding:.15rem 0; }
  nav li.g { font-size:.72rem; letter-spacing:.12em; text-transform:uppercase; color:var(--muted); margin-top:.6rem; }
  nav a { color:var(--ink); text-decoration:none; } nav a:hover { color:var(--accent); }
  .n { display:inline-block; min-width:1.6rem; color:var(--accent); font-weight:600; font-variant-numeric:tabular-nums; }
  section.tutorial { margin:0 0 4rem; break-before:page; }
  section.tutorial h2, .guide h2 { font-size:1.6rem; margin:.2rem 0 .3rem; }
  .step { display:grid; grid-template-columns:minmax(0,1fr) minmax(0,1.35fr); gap:1.25rem; background:var(--surface);
          border:1px solid var(--rule); border-radius:6px; padding:1rem 1.1rem; margin:0 0 1rem; break-inside:avoid; }
  .step h3 { font-size:1.05rem; margin:.1rem 0 .4rem; }
  .step p { margin:0 0 .5rem; }
  .num { font-size:.72rem; letter-spacing:.1em; text-transform:uppercase; color:var(--muted); margin:0; }
  .num span { display:inline-block; background:var(--chip); color:var(--accent); font-weight:700; border-radius:3px;
              padding:0 .45rem; margin-right:.5rem; font-variant-numeric:tabular-nums; }
  details { margin:.3rem 0 0; color:var(--muted); font-size:.93rem; }
  summary { cursor:pointer; color:var(--accent); }
  figure { margin:0; } figure img { width:100%; height:auto; border:1px solid var(--rule); border-radius:4px; display:block; }
  .guide { break-before:page; }
  img.banner { width:100%; height:auto; aspect-ratio:12/5; object-fit:cover; border-radius:8px; margin:0 0 1rem; display:block; }
  img.hero { width:100%; height:auto; aspect-ratio:12/5; object-fit:cover; border-radius:10px; margin:0 0 1.2rem; display:block; }
  figure.guidepic { margin:.4rem 0 1rem; }
  figure.sectionpic { margin:.3rem 0 .7rem; max-width:620px; } figure.sectionpic img { width:auto; max-width:100%; }
  .guide .part { font-size:.78rem; letter-spacing:.14em; text-transform:uppercase; color:var(--muted);
                 border-bottom:1px solid var(--rule); padding-bottom:.4rem; margin:2.5rem 0 1.2rem; }
  .topic { margin:0 0 2rem; } .topic h4 { font-size:1.15rem; margin:0 0 .2rem; }
  .summary { color:var(--muted); font-style:italic; margin:0 0 .8rem; }
  .topic h5 { font-size:.72rem; letter-spacing:.1em; text-transform:uppercase; color:var(--accent); margin:1.2rem 0 .4rem; }
  .bullet { margin:0 0 .5rem; }
  @media (max-width:44rem) { .step { grid-template-columns:1fr; } nav ol { columns:1; } }
  @media print { body { background:#fff; } .step { border-color:#ddd; } details { display:block; }
                 details > summary { display:none; } details > p { color:#555; } }
</style></head><body><div class="wrap">
<header>@HERO@<p class="eyebrow">ModForge @VERSION@</p><h1>Learn ModForge: Dummy Days</h1>
<p class="lede">In an empty gallery downtown stands a mannequin - the Dummy. When nobody is looking she moves, and she
dreams of being a Starmaker model. Each tutorial adds a piece of her story to one mod, made in the editor as you
read: these are its @N@ tutorials, step by step as the editor shows them, then the guide to every tab.</p></header>
<nav><ol>@TOC@<li class="g">Reference</li><li><a href="#guide"><span class="n">&#9670;</span> The guide</a></li></ol></nav>
@BODY@
@GUIDE@
</div></body></html>
"""
hero = art("home", "TutorialHeaders", 1200)
page = page.replace("@HERO@", '<img class="hero" src="img/%s" alt="">' % hero if hero else "")
for k, v in (("@VERSION@", e(T("app.version") or "1.10.0")), ("@N@", str(len(tutorials))), ("@TOC@", "".join(toc)),
             ("@BODY@", "\n".join(body)), ("@GUIDE@", "\n".join(g))):
    page = page.replace(k, v)
page_path = os.path.join(out, "learn-modforge.html")
io.open(page_path, "w", encoding="utf-8").write(page)
print("wrote", page_path, len(page), "bytes,", len(tutorials), "tutorials")

# The PDF: Edge prints the page as it is, its print styles applied (fold-outs open).
edge = next((p for p in (r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                         r"C:\Program Files\Microsoft\Edge\Application\msedge.exe") if os.path.exists(p)), None)
if edge:
    pdf = os.path.join(out, "learn-modforge.pdf")
    subprocess.run([edge, "--headless=new", "--disable-gpu", "--no-pdf-header-footer", "--print-to-pdf=" + pdf,
                    "file:///" + page_path.replace("\\", "/")], check=False, timeout=300,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print("pdf:", pdf if os.path.exists(pdf) else "not written")
else:
    print("pdf: Microsoft Edge not found")
