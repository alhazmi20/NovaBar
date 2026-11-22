#!/usr/bin/env python3
"""Check the reference widget gallery for widgets missing from data/reference-widgets.json.

Usage:
  scripts/check-new-widgets.py            # print new / removed / changed widgets
  scripts/check-new-widgets.py --update   # also rewrite data/reference-widgets.json
  scripts/check-new-widgets.py --html FILE  # parse a saved copy instead of fetching
Stdlib only. Exit code: 0 = in sync, 1 = differences found, 2 = fetch/parse error.
"""
import argparse
import datetime
import html
import json
import os
import re
import sys
import urllib.request

URL = "https://lumabar.xyz/widgets"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(ROOT, "data", "reference-widgets.json")


def clean(s):
    s = re.sub(r"<[^>]+>", "", s)
    s = html.unescape(s)
    s = s.replace(" ", " ")
    return re.sub(r"\s+", " ", s).strip()


def fetch(url):
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (widget-checklist script)", "Accept": "text/html"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read().decode("utf-8", "replace")


def parse(page):
    """Return list of {category, name, description} in page order (skips the Highlights section)."""
    out, seen = [], set()
    # Split into <section id=...> blocks with an <h2> category title.
    sections = list(re.finditer(r'<section\b[^>]*\bid="([^"]*)"[^>]*>', page))
    for i, m in enumerate(sections):
        sid = m.group(1)
        end = sections[i + 1].start() if i + 1 < len(sections) else len(page)
        block = page[m.end():end]
        h2 = re.search(r"<h2\b[^>]*>(.*?)</h2>", block, re.S)
        if not h2:
            continue
        cat = clean(h2.group(1))
        if sid.lower() == "highlights" or cat.lower() == "highlights":
            continue
        cards = re.findall(r'<h3\b[^>]*class="[^"]*wcard-name[^"]*"[^>]*>(.*?)</h3>\s*(?:<p\b[^>]*class="[^"]*wcard-text[^"]*"[^>]*>(.*?)</p>)?', block, re.S)
        if not cards:  # heuristic fallback: any h3 followed by a p
            cards = re.findall(r"<h3\b[^>]*>(.*?)</h3>\s*(?:<p\b[^>]*>(.*?)</p>)?", block, re.S)
        for name, desc in cards:
            name = clean(re.sub(r"<span\b.*?</span>", "", name, flags=re.S))  # drop badges like "Preview"
            if not name or name.lower() in seen:
                continue
            seen.add(name.lower())
            out.append({"category": cat, "name": name, "description": clean(desc or "")})
    if not out:  # last resort: image alt texts "<Name> widget"
        for name in re.findall(r'alt="([^"]+?) widget"', page):
            name = clean(name)
            if name.lower() not in seen:
                seen.add(name.lower())
                out.append({"category": "Uncategorized", "name": name, "description": ""})
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--update", action="store_true", help="rewrite data/reference-widgets.json with the live list")
    ap.add_argument("--html", help="parse this saved HTML file instead of fetching")
    ap.add_argument("--ref", default=REF, help="reference json path")
    a = ap.parse_args()

    try:
        page = open(a.html, encoding="utf-8").read() if a.html else fetch(URL)
    except Exception as e:
        print("error: could not fetch %s: %s" % (URL, e), file=sys.stderr)
        return 2
    live = parse(page)
    if not live:
        print("error: no widgets found on the page (layout changed?)", file=sys.stderr)
        return 2

    try:
        ref = json.load(open(a.ref, encoding="utf-8"))
    except Exception:
        ref = {"widgets": []}
    refw = ref.get("widgets", [])
    rmap = {w["name"].lower(): w for w in refw}
    lmap = {w["name"].lower(): w for w in live}

    new = [w for w in live if w["name"].lower() not in rmap]
    removed = [w for w in refw if w["name"].lower() not in lmap]
    moved = [(rmap[k], w) for k, w in lmap.items() if k in rmap and rmap[k].get("category") != w["category"]]

    print("live: %d widgets in %d categories | reference: %d" % (len(live), len({w["category"] for w in live}), len(refw)))
    if new:
        print("\nNEW (%d):" % len(new))
        for w in new:
            print("  + [%s] %s - %s" % (w["category"], w["name"], w["description"]))
    if removed:
        print("\nREMOVED from site (%d):" % len(removed))
        for w in removed:
            print("  - [%s] %s" % (w.get("category", "?"), w["name"]))
    if moved:
        print("\nCATEGORY CHANGED (%d):" % len(moved))
        for r, w in moved:
            print("  ~ %s: %s -> %s" % (w["name"], r.get("category"), w["category"]))
    if not (new or removed or moved):
        print("in sync - no new widgets")

    if a.update and (new or removed or moved):
        # keep existing descriptions (they may have been hand-edited), take everything else from live
        merged = []
        for w in live:
            r = rmap.get(w["name"].lower())
            merged.append({"category": w["category"], "name": w["name"],
                           "description": (r.get("description") if r and r.get("description") else w["description"])})
        out = {"source": URL, "fetched": datetime.date.today().isoformat(), "count": len(merged), "widgets": merged}
        with open(a.ref, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=1, ensure_ascii=False)
            f.write("\n")
        print("\nupdated %s (%d widgets)" % (a.ref, len(merged)))
    return 1 if (new or removed or moved) else 0


if __name__ == "__main__":
    sys.exit(main())
