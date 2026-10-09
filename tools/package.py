"""Build the release: every check, the Windows converter, notices, the zip and the Melty recipe.

  python3 tools/package.py            -> dist/silent-katamari-<version>.zip, dist/melty.json, dist/files.json
Needs: dotnet 10 SDK on PATH (DOTNET_ROOT), lua5.1 + luacheck (optional), network for the pinned vgmstream.
Nothing from Once Upon A KATAMARI or SILENT HILL 2 is ever packaged: it only exists on players' PCs.
"""
import glob
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
BUILD = os.path.join(ROOT, "build")
CSPROJ = os.path.join(ROOT, "converter", "SilentKatamari.Converter", "SilentKatamari.Converter.csproj")
VGMSTREAM = ("https://github.com/vgmstream/vgmstream/releases/download/r2117/vgmstream-win64.zip",
             "6c4a8a3813864fefed081bbd337dbc0ad93bf88e0b92f5db98d7ab258b22dc6c")
CUE4PARSE_NOTICE = "https://raw.githubusercontent.com/FabianFG/CUE4Parse/master/NOTICE"


def run(cmd, **kw):
    print("$ " + " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=ROOT, **kw)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def version():
    return re.search(r"<Version>([^<]+)</Version>", open(CSPROJ).read()).group(1)


def checks():
    run([sys.executable, "tools/gen.py", "--check"])
    run([sys.executable, "tools/preflight.py"])
    if shutil.which("lua5.1"):
        run(["lua5.1", "tools/bench/katamath_bench.lua"])
    if shutil.which("luacheck"):
        run(["luacheck", "addons", "tools/bench", "--no-color", "-q"])
    run(["dotnet", "test", "converter/SilentKatamari.Converter.Tests", "--nologo", "-v", "q"])


def vgmstream(dest):
    cache = os.path.join(BUILD, "vgmstream-win64.zip")
    os.makedirs(BUILD, exist_ok=True)
    if not os.path.exists(cache) or sha256(cache) != VGMSTREAM[1]:
        urllib.request.urlretrieve(VGMSTREAM[0], cache)
    if sha256(cache) != VGMSTREAM[1]:
        raise SystemExit("vgmstream download does not match its pinned SHA-256")
    with zipfile.ZipFile(cache) as z:
        z.extractall(dest)


def notices(dest):
    os.makedirs(dest, exist_ok=True)
    assets = json.load(open(os.path.join(ROOT, "converter", "SilentKatamari.Converter", "obj", "project.assets.json")))
    target = next(iter(assets["targets"].values()))
    rows = []
    for key in sorted(target):
        lib = assets["libraries"].get(key)
        if not lib or lib["type"] != "package":
            continue
        name, ver = key.split("/")
        spec = glob.glob(os.path.expanduser(f"~/.nuget/packages/{name.lower()}/{ver.lower()}/*.nuspec"))
        text = open(spec[0], encoding="utf-8", errors="replace").read() if spec else ""
        lic = (re.search(r'<license type="expression">([^<]+)</license>', text) or
               re.search(r"<licenseUrl>([^<]+)</licenseUrl>", text) or
               re.search(r'<license type="file">([^<]+)</license>', text))
        authors = re.search(r"<authors>([^<]+)</authors>", text)
        url = re.search(r"<projectUrl>([^<]+)</projectUrl>", text)
        rows.append(f"| {name} | {ver} | {lic.group(1) if lic else '?'} | {authors.group(1) if authors else ''} | {url.group(1) if url else ''} |")
        for f in glob.glob(os.path.expanduser(f"~/.nuget/packages/{name.lower()}/{ver.lower()}/LICENSE*")):
            shutil.copy(f, os.path.join(dest, f"{name}-{os.path.basename(f)}"))
    with open(os.path.join(dest, "NOTICES.md"), "w", encoding="utf-8") as f:
        f.write("# Third-party software in silent-katamari-convert.exe\n\n"
                "| Package | Version | License | Authors | Project |\n|---|---|---|---|---|\n" + "\n".join(rows) + "\n\n"
                "The .NET 10 runtime is bundled self-contained (MIT, .NET Foundation and Contributors).\n"
                "Full license texts: CUE4Parse-NOTICE.txt (MIT, MS-PL, Apache-2.0, BSD-2-Clause, ISC, zlib) and the\n"
                "package LICENSE files in this folder.\n")
    urllib.request.urlretrieve(CUE4PARSE_NOTICE, os.path.join(dest, "CUE4Parse-NOTICE.txt"))


def recipe(zip_name):
    games = [{"role": "primary", "slug": "garrys-mod"},
             {"role": "secondary", "slug": "custom-once-upon-a-katamari"},
             {"role": "secondary", "slug": "custom-silent-hill-2"}]
    exe = "{managed}/converter/silent-katamari-convert.exe"
    return {
        "schemaVersion": 1,
        "mode": "installed",
        "games": games,
        "components": [{"id": "main", "kind": "main", "label": "Silent Katamari gamemode and converter",
                        "fileName": zip_name, "required": True}],
        "mappings": [
            {"component": "main", "from": "addons/", "to": "{game}/garrysmod/addons"},
            {"component": "main", "from": "converter/", "to": "{managed}/converter"},
            {"component": "main", "from": "README.md", "to": "{game}/garrysmod/addons/silent_katamari"},
            {"component": "main", "from": "CREDITS.md", "to": "{game}/garrysmod/addons/silent_katamari"},
            {"component": "main", "from": "LICENSE", "to": "{game}/garrysmod/addons/silent_katamari"},
        ],
        "setup": {
            "label": "Katamari and SILENT HILL 2 content",
            "launch": {"kind": "exe", "path": exe, "args": ["convert", "--gmod", "{game}"]},
            "done": {"file": "{game}/garrysmod/addons/silent_katamari_content/data_static/silent_katamari/status.json"},
        },
        "launch": {"kind": "exe", "path": exe, "cwd": "{game}",
                   "args": ["play", "--gmod", "{game}", "-steam", "+gamemode", "silentkatamari", "+map", "gm_flatgrass",
                            "+sv_loadingurl", "asset://garrysmod/html/silentkatamari/loading.html"]},
        "runtimeData": ["{localappdata}/SilentKatamari"],
        "notes": {"firstLaunch": "The first Play reads James Sunderland, a street texture and sounds from your own SILENT HILL 2, "
                                 "and the objects, their sizes, the katamari's core and sounds from your own Once Upon A KATAMARI, "
                                 "into garrysmod/addons/silent_katamari_content (a few minutes, only once, and again after either "
                                 "game updates). If one of them isn't installed, Garry's Mod still starts and the game says which one."},
        "requirements": [],
    }


def main():
    v = version()
    if "--skip-checks" not in sys.argv:
        checks()
    stage = os.path.join(BUILD, "stage")
    shutil.rmtree(stage, ignore_errors=True)
    os.makedirs(stage)
    run(["dotnet", "publish", CSPROJ, "-c", "Release", "-r", "win-x64", "-o", os.path.join(stage, "converter"), "--nologo", "-v", "q"])
    vgmstream(os.path.join(stage, "converter", "vgmstream"))
    notices(os.path.join(stage, "converter", "THIRD_PARTY_LICENSES"))
    shutil.copytree(os.path.join(ROOT, "addons", "silent_katamari"), os.path.join(stage, "addons", "silent_katamari"))
    for f in ("README.md", "CREDITS.md", "LICENSE"):
        shutil.copy(os.path.join(ROOT, f), stage)

    os.makedirs(DIST, exist_ok=True)
    zip_name = f"silent-katamari-{v}.zip"
    zpath = os.path.join(DIST, zip_name)
    files = []
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for dirpath, _, names in sorted(os.walk(stage)):
            for n in sorted(names):
                full = os.path.join(dirpath, n)
                rel = os.path.relpath(full, stage).replace(os.sep, "/")
                z.write(full, rel)
                files.append({"path": rel, "size": os.path.getsize(full)})
    json.dump(recipe(zip_name), open(os.path.join(DIST, "melty.json"), "w"), indent=2)
    json.dump({"zip": zip_name, "size": os.path.getsize(zpath), "sha256": sha256(zpath), "files": files},
              open(os.path.join(DIST, "files.json"), "w"), indent=2)
    print(f"\n{zip_name}: {os.path.getsize(zpath)} bytes, sha256 {sha256(zpath)}, {len(files)} files")


if __name__ == "__main__":
    main()
