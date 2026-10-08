"""Load and type-check the design sheets in sheets/*.json. Shared by gen.py and preflight.py."""
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS_DIR = os.path.join(ROOT, "sheets")


def load_all():
    sheets = {}
    for name in sorted(os.listdir(SHEETS_DIR)):
        if not name.endswith(".json"):
            continue
        with open(os.path.join(SHEETS_DIR, name), encoding="utf-8") as f:
            data = json.load(f)
        if data.get("sheet") != name[:-5]:
            raise ValueError(f"{name}: 'sheet' must be {name[:-5]!r}")
        sheets[data["sheet"]] = data
    return sheets


def ids(sheet):
    return [row.get("id") for row in sheet["rows"]]


def is_unfilled(value):
    """Missing, null, "", "?" or "TBD". An empty list is a valid "none"."""
    if isinstance(value, str):
        return value.strip() in ("", "?", "TBD")
    return value is None


def check_cell(sheets, ctype, value):
    """Returns a list of problems (strings) for one cell. ctype from the column definition."""
    if ctype.startswith("list:"):
        if not isinstance(value, list):
            return [f"expected a list, got {type(value).__name__}"]
        out = []
        for i, v in enumerate(value):
            for p in check_cell(sheets, ctype[5:], v):
                out.append(f"[{i}] {p}")
        return out
    if ctype.startswith("ref:"):
        target = ctype[4:]
        if target not in sheets:
            return [f"refers to unknown sheet {target!r}"]
        if value == "-":
            return []
        if value not in ids(sheets[target]):
            return [f"reference {value!r} not found in sheet {target!r}"]
        return []
    if ctype.startswith("enum:"):
        allowed = ctype[5:].split("|")
        return [] if value in allowed else [f"{value!r} not one of {allowed}"]
    if ctype in ("id", "string", "text"):
        if not isinstance(value, str):
            return [f"expected text, got {type(value).__name__}"]
        if ctype == "id" and not re.fullmatch(r"[A-Za-z0-9_.:\-]+", value):
            return [f"id {value!r} has characters outside A-Z a-z 0-9 _ . : -"]
        return []
    if ctype == "number":
        return [] if isinstance(value, (int, float)) and not isinstance(value, bool) else [f"expected a number, got {value!r}"]
    if ctype == "int":
        return [] if isinstance(value, int) and not isinstance(value, bool) else [f"expected an integer, got {value!r}"]
    if ctype == "bool":
        return [] if isinstance(value, bool) else [f"expected true/false, got {value!r}"]
    if ctype == "any":
        return []
    return [f"unknown column type {ctype!r}"]
