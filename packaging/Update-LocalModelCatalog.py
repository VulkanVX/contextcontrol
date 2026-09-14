"""Refresh the shipped discovery snapshot from Ollama's public library. Downloads metadata only."""
import concurrent.futures
import datetime
import html
import json
from pathlib import Path
import re
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'ide/ContextControl.Workbench/Features/LocalModels/Catalog/DiscoveredModels.json'


def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'ContextControl-catalog/1.0'})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read(4_000_000).decode('utf-8')


def plain(value):
    return ' '.join(html.unescape(re.sub('<[^>]+>', ' ', value)).split())


def family_models(family):
    source = 'https://ollama.com/library/' + family + '/tags'
    page = fetch(source)
    candidates = {}
    fallback = {}
    for identifier, inner in re.findall(r'<a\s+href="/library/([^"<>]+:[^"<>]+)"[^>]*>(.*?)</a>', page, re.S):
        identifier = html.unescape(identifier)
        tag = identifier.split(':', 1)[1]
        # Canonical size tags avoid exposing hundreds of redundant quantizations.
        canonical = re.fullmatch(r'(?:latest|cloud|e?\d+(?:\.\d+)?[bm]|\d+x\d+b)', tag)
        if not canonical and not tag.lower().endswith(('q4_k_m', '-cloud')): continue
        text = plain(inner)
        size = re.search(r'\b(\d+(?:\.\d+)?)\s*(GB|MB|TB)\b', text)
        context = re.search(r'\b(\d+(?:\.\d+)?[KM]?)\s+context', text, re.I)
        target = candidates if canonical else fallback
        old = target.get(identifier)
        if old is not None and (old['size'] or not size): continue
        target[identifier] = {'id': identifier, 'family': family,
                                 'size': size.group(0).replace(' ', '') if size else '',
                                 'sizeGb': float(size.group(1)) * {'GB': 1, 'MB': .001, 'TB': 1000}[size.group(2)] if size else 0,
                                 'context': context.group(1).upper() if context else '',
                                 'vision': 'Image' in text, 'source': 'https://ollama.com/library/' + identifier}
    if len(candidates) > 1: candidates.pop(family + ':latest', None)
    if not candidates: candidates = fallback
    if not candidates: raise ValueError(f'No canonical tags found for {family}; review the source page')
    return list(candidates.values())


def main():
    index = fetch('https://ollama.com/library?sort=newest')
    families = list(dict.fromkeys(re.findall(r'href="/library/([a-z0-9][a-z0-9._-]*)"', index)))
    if len(families) < 50: raise ValueError('Library index unexpectedly small; refusing to replace snapshot')
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as workers:
        rows = [row for results in workers.map(family_models, families) for row in results]
    document = {'checkedAt': datetime.datetime.now(datetime.timezone.utc).date().isoformat(),
                'source': 'https://ollama.com/library?sort=newest', 'families': len(families), 'models': rows}
    OUT.write_text(json.dumps(document, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'Verified {len(rows)} model tags across {len(families)} families; no weights downloaded.')


if __name__ == '__main__': main()
