"""Preflight: lay every sheet's rows and columns over each other and list what will fail or is unfinished.

  python3 tools/preflight.py            build gate: unfilled cells, type errors, broken references,
                                        code that disagrees with the sheets (exit 1 if any)
  python3 tools/preflight.py --release  also fails on unverified cells and systems not verified in game

Every row x column crossing is a checkbox: it is unchecked while unfilled, or while the column is in the
row's "unverified" list.
"""
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sheets import ROOT, check_cell, ids, is_unfilled, load_all  # noqa: E402

GM_DIR = os.path.join(ROOT, "addons", "silent_katamari", "gamemodes", "silentkatamari")
CV_DIR = os.path.join(ROOT, "converter", "SilentKatamari.Converter")


def resolve_file(path):
    if path.startswith("GM/"):
        return os.path.join(GM_DIR, path[3:])
    if path.startswith("CV/"):
        return os.path.join(CV_DIR, path[3:])
    return os.path.join(ROOT, path)


def read_sources(pattern_dir, exts):
    out = {}
    for ext in exts:
        for p in glob.glob(os.path.join(pattern_dir, "**", "*" + ext), recursive=True):
            with open(p, encoding="utf-8", errors="replace") as f:
                out[os.path.relpath(p, ROOT)] = f.read()
    return out


def main():
    release = "--release" in sys.argv
    errors, unverified, unimplemented = [], [], []
    try:
        sheets = load_all()
    except Exception as e:  # malformed JSON stops everything
        print(f"PREFLIGHT FAILED: {e}")
        return 1

    cells = checked = 0
    for name, sheet in sheets.items():
        cols = sheet.get("columns") or {}
        if "id" not in cols:
            errors.append(f"{name}: no 'id' column")
        seen = set()
        for i, row in enumerate(sheet.get("rows", [])):
            rid = row.get("id", f"#{i}")
            where = f"{name}[{rid}]"
            if rid in seen:
                errors.append(f"{where}: duplicate id")
            seen.add(rid)
            unv = row.get("unverified", [])
            for c in unv:
                if c not in cols:
                    errors.append(f"{where}: 'unverified' names unknown column {c!r}")
            for key in row:
                if key not in cols and key != "unverified":
                    errors.append(f"{where}: column {key!r} is not defined in the sheet")
            for col, spec in cols.items():
                cells += 1
                if col not in row or is_unfilled(row[col]):
                    errors.append(f"{where}.{col}: UNFILLED")
                    continue
                for p in check_cell(sheets, spec["type"], row[col]):
                    errors.append(f"{where}.{col}: {p}")
                if col in unv:
                    unverified.append(f"{where}.{col} = {row[col]!r}")
                else:
                    checked += 1
            for col, want in (sheet.get("release_requires") or {}).items():
                if row.get(col) != want:
                    unverified.append(f"{where}.{col} = {row.get(col)!r} (release needs {want!r})")

    # --- cross-sheet rules that a column type can't express ---
    for row in sheets.get("strings", {}).get("rows", []):
        en, es = set(re.findall(r"\{(\w+)\}", row.get("en", ""))), set(re.findall(r"\{(\w+)\}", row.get("es", "")))
        if en != es:
            errors.append(f"strings[{row['id']}]: placeholders differ between en {sorted(en)} and es {sorted(es)}")
    tiers = sheets.get("tiers", {}).get("rows", [])
    for t in tiers:
        if t["size_min_cm"] >= t["size_max_cm"]:
            errors.append(f"tiers[{t['id']}]: size_min_cm must be below size_max_cm")
        if t["ring_min_m"] >= t["ring_max_m"]:
            errors.append(f"tiers[{t['id']}]: ring_min_m must be below ring_max_m")
        if t["kinds"] > t["count"]:
            errors.append(f"tiers[{t['id']}]: kinds is more than count")
    for a, b in zip(tiers, tiers[1:]):
        if a["size_max_cm"] > b["size_min_cm"]:
            errors.append(f"tiers[{a['id']}] and [{b['id']}]: size ranges overlap")
    rules = {r["id"]: r for r in sheets.get("rules", {}).get("rows", [])}
    for st in sheets.get("stage", {}).get("rows", []):
        if st["goal_cm"] <= st["start_diameter_cm"]:
            errors.append(f"stage[{st['id']}]: goal must be bigger than the start size")
        if "fog_end_max_m" in rules and max(t["ring_max_m"] for t in tiers) > st["ground_size_m"] / 2:
            errors.append(f"stage[{st['id']}]: object rings reach past the ground plane")
    for ch in sheets.get("characters", {}).get("rows", []):
        for clip in ch["clips"]:
            row = next((a for a in sheets["anims"]["rows"] if a["id"] == clip), None)
            if row and row["character"] != ch["id"]:
                errors.append(f"characters[{ch['id']}].clips: {clip!r} belongs to {row['character']!r}")
    games = {g["id"]: g for g in sheets.get("games", {}).get("rows", [])}
    for g in games.values():
        if g["role"] == "secondary" and g["missing_text"] == "-":
            errors.append(f"games[{g['id']}]: a secondary game needs a missing_text")

    # --- code against sheets ---
    lua = read_sources(os.path.join(ROOT, "addons"), [".lua"])
    cs = read_sources(os.path.join(ROOT, "converter"), [".cs"])
    systems = sheets.get("systems", {}).get("rows", [])
    for s in systems:
        exists = os.path.exists(resolve_file(s["file"]))
        if s["status"] in ("built", "verified") and not exists:
            errors.append(f"systems[{s['id']}]: status {s['status']} but {s['file']} does not exist")
        if not exists:
            unimplemented.append(f"system {s['id']} ({s['file']})")
    code = "\n".join(lua.values())
    hook_rows = sheets.get("hooks", {}).get("rows", [])
    for h in hook_rows:
        if h["id"].startswith("GM:"):
            found = re.search(r"function\s+GM:" + re.escape(h["id"][3:]) + r"\s*\(", code)
        else:
            found = re.search(r'hook\.Add\(\s*"' + re.escape(h["hook"]) + r'"\s*,\s*"' + re.escape(h["id"]) + '"', code)
        if not found:
            unimplemented.append(f"hook {h['id']} ({h['hook']})")
    known = {h["id"] for h in hook_rows}
    for m in re.finditer(r'hook\.Add\(\s*"(\w+)"\s*,\s*"([\w.:-]+)"', code):
        if m.group(2) not in known:
            errors.append(f"code: hook.Add({m.group(1)!r}, {m.group(2)!r}) has no row in hooks.json")
    for m in re.finditer(r"function\s+GM:(\w+)\s*\(", code):
        if f"GM:{m.group(1)}" not in known:
            errors.append(f"code: GM:{m.group(1)} has no row in hooks.json")
    for c in sheets.get("commands", {}).get("rows", []):
        if not re.search(r'(concommand\.Add|CreateConVar|CreateClientConVar)\(\s*"' + re.escape(c["id"]) + '"', code):
            unimplemented.append(f"command {c['id']}")
    for m in re.finditer(r'\bRule\(\s*"(\w+)"\s*\)', code):
        if m.group(1) not in rules:
            errors.append(f"code: Rule({m.group(1)!r}) has no row in rules.json")
    string_ids = set(ids(sheets["strings"])) if "strings" in sheets else set()
    for m in re.finditer(r'\bL\(\s*"(\w+)"', code):
        if m.group(1) not in string_ids:
            errors.append(f"code: L({m.group(1)!r}) has no row in strings.json")
    sound_ids = set(ids(sheets["sounds"])) if "sounds" in sheets else set()
    for m in re.finditer(r'Sounds\.\w+\([^)]*?"((?:ouak|sh2)\.[\w]+)"', code):
        if m.group(1) not in sound_ids:
            errors.append(f"code: sound {m.group(1)!r} has no row in sounds.json")
    extract_ids = set(ids(sheets["extract"])) if "extract" in sheets else set()
    for m in re.finditer(r'Extract\.(\w+)\b', "\n".join(cs.values())):
        if m.group(1) not in extract_ids and m.group(1) not in ("All", "ById"):
            errors.append(f"converter: Extract.{m.group(1)} has no row in extract.json")

    # --- report ---
    print(f"preflight: {len(sheets)} sheets, {cells} cells, {checked} checked, {len(unverified)} unverified, "
          f"{len(errors)} errors, {len(unimplemented)} not implemented")
    for title, items in (("ERRORS (fix before building)", errors),
                         ("NOT IMPLEMENTED YET", unimplemented),
                         ("UNVERIFIED (check in the real game before release)", unverified)):
        if items:
            print(f"\n{title}:")
            for x in items:
                print("  - " + x)
    if errors:
        return 1
    if release and (unverified or unimplemented):
        print("\nrelease gate: NOT clean")
        return 1
    print("\nbuild gate: clean" + (" | release gate: clean" if release else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())
