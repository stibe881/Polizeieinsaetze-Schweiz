#!/usr/bin/env python3
"""Downloads the currently valid consolidated German text of Swiss federal acts from Fedlex (SPARQL + filestore)
and stores plain text + metadata (SR number, consolidation date, source URL, SHA-256) under docs/law/.
Usage: fetch_fedlex.py <out_dir> <SR> [<SR> ...]     e.g. fetch_fedlex.py docs/law 741.01 741.11
Source: https://fedlex.data.admin.ch (SPARQL endpoint). Texts are official publications (Art. 5 URG: not protected by copyright)."""
import sys, os, json, re, html, hashlib, subprocess, datetime, urllib.request

ENDPOINT = 'https://fedlex.data.admin.ch/sparqlendpoint'

def sparql(query):
    r = subprocess.run(['curl', '-sS', '-m', '120', '-H', 'Accept: application/sparql-results+json', '--data-urlencode', 'query=' + query, ENDPOINT], capture_output=True, text=True)
    return json.loads(r.stdout)['results']['bindings']

def find_work(sr):
    q = f'''PREFIX j: <http://data.legilux.public.lu/resource/ontology/jolux#>
SELECT DISTINCT ?w WHERE {{ ?w j:historicalLegalId "{sr}" . ?w a j:ConsolidationAbstract . }} LIMIT 5'''
    rows = sparql(q)
    if not rows:   # fall back: any work with that id
        q = q.replace('?w a j:ConsolidationAbstract .', '')
        rows = sparql(q.replace('LIMIT 5','LIMIT 20'))
    return [b['w']['value'] for b in rows if not re.search(r'/(de|fr|it|rm|en)$', b['w']['value'])]

def latest_consolidation(work, today):
    q = f'''PREFIX j: <http://data.legilux.public.lu/resource/ontology/jolux#>
SELECT ?c ?d WHERE {{ ?c j:isMemberOf <{work}> . ?c j:dateApplicability ?d . FILTER(?d <= "{today}"^^<http://www.w3.org/2001/XMLSchema#date>) }} ORDER BY DESC(?d) LIMIT 1'''
    rows = sparql(q)
    return (rows[0]['c']['value'], rows[0]['d']['value']) if rows else (None, None)

def html_file(cons):
    for fmt in ('/html', '/xml'):
        q = f'''PREFIX j: <http://data.legilux.public.lu/resource/ontology/jolux#>
SELECT ?f WHERE {{ <{cons}> j:isRealizedBy ?e . ?e j:language <http://publications.europa.eu/resource/authority/language/DEU> . ?e j:isEmbodiedBy ?m . ?m j:isExemplifiedBy ?f . FILTER(CONTAINS(STR(?m), "{fmt}")) }} LIMIT 1'''
        rows = sparql(q)
        if rows: return rows[0]['f']['value']
    return None

def to_text(h):
    h = re.sub(r'(?is)<(script|style).*?</\1>', '', h)
    h = re.sub(r'(?i)<br\s*/?>|</p>|</div>|</h\d>|</li>|</tr>', '\n', h)
    h = re.sub(r'(?s)<[^>]+>', '', h)
    t = html.unescape(h)
    t = re.sub(r'[ \t\xa0]+', ' ', t); t = re.sub(r'\n\s*\n+', '\n', t)
    return t.strip()

def main():
    out = sys.argv[1]; srs = sys.argv[2:]; os.makedirs(out, exist_ok=True)
    today = datetime.date.today().isoformat(); index = []
    for sr in srs:
        works = find_work(sr)
        if not works: print(f'SR {sr}: not found'); continue
        cons = date = url = work = None
        best = ''
        for w in works:                      # several works may carry the same SR (repealed predecessors, language variants): take the newest usable one
            c, d = latest_consolidation(w, today)
            if not c: continue
            u = html_file(c)
            if u and d > best: best = d; work, cons, date, url = w, c, d, u
        if not url: print(f'SR {sr}: no usable consolidation ({len(works)} works)'); continue
        raw = urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'SwissPoliceSim-law-fetch'}), timeout=120).read()
        text = to_text(raw.decode('utf-8', 'replace'))
        path = os.path.join(out, f'SR_{sr}.txt')
        open(path, 'w', encoding='utf-8').write(text)
        meta = {'sr': sr, 'work': work, 'consolidation': cons, 'applicable_from': date, 'source': url, 'retrieved': today, 'sha256': hashlib.sha256(text.encode()).hexdigest(), 'chars': len(text)}
        index.append(meta); print(f"SR {sr}: {date} {len(text)} chars  {text[:70]!r}")
    p = os.path.join(out, 'index.json'); old = []
    if os.path.exists(p): old = [m for m in json.load(open(p)) if m['sr'] not in srs]
    json.dump(old + index, open(p, 'w'), indent=1, ensure_ascii=False)

main()
