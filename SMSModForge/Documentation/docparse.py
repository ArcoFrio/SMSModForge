import io,json,re
BS=chr(92)
src=io.open('Documentation/DocTopics.cs',encoding='utf-8').read()
src=src[src.index('Parts { get; } = new[]'):]

def read_str(s,i):
    while s[i] in ' \n\r\t': i+=1
    if s[i]!='"': return None,i
    i+=1; out=[]
    while True:
        c=s[i]
        if c==BS: out.append(s[i+1]); i+=2; continue
        if c=='"': return ''.join(out), i+1
        out.append(c); i+=1

parts=[]; tok=re.compile(r'new Doc(Part|Topic|Section|Bullet)\(')
i=0
while True:
    m=tok.search(src,i)
    if not m: break
    kind=m.group(1); j=m.end()
    if kind=='Part':
        n,j=read_str(src,j); parts.append({'name':n,'topics':[]})
    elif kind=='Topic':
        t,j=read_str(src,j); j=src.index(',',j)+1
        smry,j=read_str(src,j); parts[-1]['topics'].append({'title':t,'summary':smry,'sections':[]})
    elif kind=='Section':
        h,j=read_str(src,j); parts[-1]['topics'][-1]['sections'].append({'heading':h,'bullets':[]})
    else:
        a,j2=read_str(src,j)
        k=j2
        while src[k] in ' \n\r\t': k+=1
        if src[k]==',':
            b,j2=read_str(src,k+1); bullet={'term':a,'text':b}
        else:
            bullet={'term':'','text':a}
        parts[-1]['topics'][-1]['sections'][-1]['bullets'].append(bullet)
        j=j2
    i=j
json.dump(parts,io.open('/tmp/docs.json','w',encoding='utf-8'),ensure_ascii=False,indent=1)
nb=sum(len(s['bullets']) for p in parts for t in p['topics'] for s in t['sections'])
print(f"parsed: {len(parts)} part(s), {sum(len(p['topics']) for p in parts)} topics, {nb} bullets")
for p in parts:
    for t in p['topics']:
        print('  -',t['title'],'|',sum(len(s['bullets']) for s in t['sections']),'bullets')
