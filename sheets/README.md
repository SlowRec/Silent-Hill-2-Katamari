# Design sheets

These JSON sheets are the source of truth for the mashup. Change a sheet before changing the code.

- One file per group of like things. Each **row** is one thing; each **column** one of its properties.
- `tools/gen.py` turns every row into code: a Lua table in
  `addons/silent_katamari/gamemodes/silentkatamari/gamemode/sk/sh_sheets.lua` (the game) and a C# record in
  `converter/SilentKatamari.Converter/Sheets.g.cs` (the converter). Generated files are never edited by hand.
- Every row × column crossing is a checkbox. A cell is **unfilled** when it is missing, `null`, `""`, `"?"` or
  `"TBD"`. A cell is **unverified** while its column name is listed in the row's `unverified` array: the value
  is a plan or an assumption that has not yet been confirmed in the real game or the player's files.
  Write `"-"` for "does not apply" (that counts as filled).
- `tools/preflight.py` lays all sheets over each other and lists every unfilled cell, every reference to
  another sheet that does not resolve, and every unverified cell.
  - **Build gate:** no unfilled cells, no broken references, no type errors.
  - **Release gate:** additionally, no unverified cells.

## Sheet file format
```json
{
  "sheet": "rules",
  "about": "what the rows are",
  "columns": {
    "id":    { "type": "id",            "about": "unique key" },
    "value": { "type": "any",           "about": "..." },
    "used_by": { "type": "ref:systems", "about": "..." }
  },
  "rows": [ { "id": "...", "value": 1, "used_by": "growth", "unverified": ["value"] } ]
}
```
Column types: `id`, `string`, `text` (may contain anything non-empty), `number`, `int`, `bool`, `any`,
`enum:a|b|c`, `ref:<sheet>`, `list:<type>` (e.g. `list:ref:extract`, `list:number`).
