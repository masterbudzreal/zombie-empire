"""Assemble the Zombie Empire Lite package folder and zip.

Usage: py -3 -I build_lite.py <release_dir> <mod_jar> <icon_png> <src_dir> <pkg_dir> <zip_out>
"""
import os, shutil, sys, zipfile
from PIL import Image  # noqa: F401  (icon resize)

release, mod_jar, icon, src, pkg, zip_out = sys.argv[1:7]
mc = os.path.join(release, "instances", "ZombieEmpire", "minecraft")
top = os.path.join(pkg, "Zombie Empire Lite")
if os.path.exists(top):
    shutil.rmtree(top)
files = os.path.join(top, "files")
os.makedirs(os.path.join(files, "mods"))

for f in ("install.bat", "install.ps1", "README.txt"):
    shutil.copy2(os.path.join(src, f), top)
for f in ("mods.txt", "CREDITS.txt"):
    shutil.copy2(os.path.join(release, f), top)
Image.open(icon).resize((128, 128), Image.NEAREST).save(os.path.join(top, "icon.png"))
shutil.copy2(mod_jar, os.path.join(files, "mods"))

for sub in ("config", "defaultconfigs", "fancymenu_data", "resourcepacks", "shaderpacks"):
    s = os.path.join(mc, sub)
    if os.path.isdir(s) and any(os.scandir(s)):
        shutil.copytree(s, os.path.join(files, sub),
                        ignore=shutil.ignore_patterns("zombieempire*.properties"))
shutil.copytree(os.path.join(mc, "saves", "bossworld"), os.path.join(files, "saves", "bossworld"))
os.makedirs(os.path.join(files, "config"), exist_ok=True)
with open(os.path.join(files, "config", "zombieempire.properties"), "w", encoding="utf-8", newline="\r\n") as f:
    f.write("# Zombie Empire Lite\nmusic=true\nwelcome=true\n"
            "welcome_title=§4§lWelcome to Zombie Empire\nwelcome_subtitle=\n"
            "lan=true\nlan_port=25565\nlan_gamemode=adventure\nlan_cheats=false\n")

with zipfile.ZipFile(zip_out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for d, dirs, fs in os.walk(top):
        dirs.sort()
        for f in sorted(fs):
            p = os.path.join(d, f)
            z.write(p, os.path.relpath(p, pkg).replace(os.sep, "/"))
with zipfile.ZipFile(zip_out) as z:
    names = z.namelist()
    assert z.testzip() is None
print(len(names), "files,", round(os.path.getsize(zip_out) / 1e6, 1), "MB")
print(sorted({n.split("/")[2] for n in names if n.count("/") >= 2 and "/files/" in n}))
