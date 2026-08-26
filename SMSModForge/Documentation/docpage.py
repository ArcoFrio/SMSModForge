import io,json,html,os
parts=json.load(io.open('/tmp/docs.json',encoding='utf-8'))
e=html.escape
SP=os.environ['SP']

ntop=sum(len(p['topics']) for p in parts)
nbul=sum(len(s['bullets']) for p in parts for t in p['topics'] for s in t['sections'])

body=[]
for p in parts:
    body.append(f'<section class="part"><h2 class="part-name">{e(p["name"])}</h2>')
    for t in p['topics']:
        body.append('<article class="topic">')
        body.append(f'<h3>{e(t["title"])}</h3>')
        body.append(f'<p class="summary">{e(t["summary"])}</p>')
        for s in t['sections']:
            if s['heading']:
                body.append(f'<h4>{e(s["heading"])}</h4>')
            termed=[b for b in s['bullets'] if b['term']]
            plain=[b for b in s['bullets'] if not b['term']]
            if termed:
                body.append('<dl>')
                for b in termed:
                    body.append(f'<dt>{e(b["term"])}</dt><dd>{e(b["text"])}</dd>')
                body.append('</dl>')
            for b in plain:
                body.append(f'<p class="note">{e(b["text"])}</p>')
        body.append('</article>')
    body.append('</section>')
body='\n'.join(body)

page=f'''<title>ModForge Reference</title>
<style>
  :root {{
    --ground:#F7F8FA; --surface:#FFFFFF; --ink:#1B2733; --muted:#5C6B7A;
    --accent:#3F6184; --rule:#DDE3EA; --term:#243444; --chip:#EDF1F6;
  }}
  @media (prefers-color-scheme: dark) {{
    :root:not([data-theme="light"]) {{
      --ground:#141C28; --surface:#1A2634; --ink:#DCE6F2; --muted:#8FA3BA;
      --accent:#7FA8D4; --rule:#26374B; --term:#CFE0F2; --chip:#223247;
    }}
  }}
  :root[data-theme="dark"] {{
    --ground:#141C28; --surface:#1A2634; --ink:#DCE6F2; --muted:#8FA3BA;
    --accent:#7FA8D4; --rule:#26374B; --term:#CFE0F2; --chip:#223247;
  }}

  * {{ box-sizing:border-box; }}
  body {{
    background:var(--ground); color:var(--ink); margin:0;
    font-family:"Segoe UI","Segoe UI Variable Text",system-ui,-apple-system,sans-serif;
    font-size:16px; line-height:1.6;
  }}
  .wrap {{ max-width:52rem; margin:0 auto; padding:3rem 1.5rem 5rem; }}

  header {{ border-bottom:2px solid var(--accent); padding-bottom:1.25rem; margin-bottom:.5rem; }}
  .eyebrow {{
    font-size:.72rem; letter-spacing:.14em; text-transform:uppercase;
    color:var(--accent); font-weight:600; margin:0 0 .5rem;
  }}
  h1 {{ font-size:2rem; line-height:1.15; margin:0 0 .6rem; letter-spacing:-.02em; text-wrap:balance; }}
  .lede {{ color:var(--muted); margin:0; max-width:42rem; }}

  .meta {{ display:flex; flex-wrap:wrap; gap:.5rem; margin:1.25rem 0 2.5rem; }}
  .chip {{
    background:var(--chip); color:var(--muted); border-radius:2px;
    padding:.25rem .6rem; font-size:.78rem;
    font-variant-numeric:tabular-nums;
  }}
  .chip b {{ color:var(--ink); font-weight:600; }}

  .brief {{
    background:var(--surface); border:1px solid var(--rule);
    border-left:3px solid var(--accent);
    padding:1.1rem 1.25rem; margin:0 0 3rem;
  }}
  .brief h2 {{ font-size:.78rem; letter-spacing:.1em; text-transform:uppercase;
               color:var(--accent); margin:0 0 .6rem; }}
  .brief ul {{ margin:0; padding-left:1.1rem; color:var(--muted); font-size:.92rem; }}
  .brief li {{ margin-bottom:.3rem; }}
  .brief li:last-child {{ margin-bottom:0; }}

  .part-name {{
    font-size:.78rem; letter-spacing:.14em; text-transform:uppercase;
    color:var(--muted); font-weight:600;
    padding-bottom:.5rem; border-bottom:1px solid var(--rule); margin:0 0 1.75rem;
  }}
  .topic {{ margin:0 0 2.75rem; }}
  .topic h3 {{ font-size:1.2rem; margin:0 0 .2rem; letter-spacing:-.01em; }}
  .summary {{ color:var(--muted); font-style:italic; margin:0 0 1rem; font-size:.95rem; }}
  .topic h4 {{
    font-size:.72rem; letter-spacing:.1em; text-transform:uppercase;
    color:var(--accent); margin:1.5rem 0 .6rem; font-weight:600;
  }}

  dl {{ display:grid; grid-template-columns:minmax(7rem,13rem) 1fr; gap:.1rem 1.5rem; margin:0; }}
  dt {{
    color:var(--term); font-weight:600; font-size:.92rem;
    padding:.45rem 0; border-top:1px solid var(--rule);
  }}
  dd {{ margin:0; padding:.45rem 0; border-top:1px solid var(--rule); }}
  dl > dt:first-of-type, dl > dt:first-of-type + dd {{ border-top:none; }}

  .note {{ color:var(--muted); margin:.7rem 0 0; font-size:.95rem; }}

  footer {{ margin-top:1rem; padding-top:1.5rem; border-top:1px solid var(--rule);
            color:var(--muted); font-size:.9rem; }}
  footer b {{ color:var(--ink); }}

  @media (max-width:36rem) {{
    dl {{ grid-template-columns:1fr; gap:0; }}
    dt {{ padding-bottom:0; }}
    dd {{ border-top:none; padding-top:.15rem; padding-bottom:.7rem; }}
    .wrap {{ padding-top:2rem; }}
  }}
</style>

<div class="wrap">
  <header>
    <p class="eyebrow">All 8 batches &middot; ready for review</p>
    <h1>ModForge Reference</h1>
    <p class="lede">End-user documentation for the ModForge editor, shown in the ModForge tab's
    Documentation section. This page renders the same catalog the editor reads, so what you see
    here is exactly what ships.</p>
  </header>

  <div class="meta">
    <span class="chip"><b>{ntop}</b> topics</span>
    <span class="chip"><b>{nbul}</b> bullets</span>
    <span class="chip">All five parts &middot; consistency pass done</span>
  </div>

  <div class="brief">
    <h2>What to check</h2>
    <ul>
      <li>Claims that are simply wrong. Everything here was read out of the editor or the
          runtime, but a description can be accurate and still misleading.</li>
      <li>Anything that reads as internals rather than something an author would recognise.</li>
      <li>Places a second sentence was needed and is missing &mdash; or is there and earns nothing.</li>
      <li>Terminology: the reference follows the UI, so where the UI itself is inconsistent
          (runtime name vs key) the docs say so rather than picking a side silently.</li>
    </ul>
  </div>

{body}

  <footer>
    <b>Verified mechanically:</b> every cross-reference resolves to a real topic; no leftover
    internal vocabulary; tab topics ordered as the tabs are. Remaining repeated labels differ
    on purpose &mdash; the concept topic explains, the tab topic describes the field.
  </footer>
</div>
'''
out=os.path.join(SP,'modforge-reference.html')
io.open(out,'w',encoding='utf-8').write(page)
print('wrote',out,len(page),'bytes')
