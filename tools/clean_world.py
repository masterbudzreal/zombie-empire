"""Prepare a world's level.dat for release: empty inventory, adventure mode, at the hub."""
import sys
sys.path.insert(0, sys.argv[1])
import nbt

path = sys.argv[2]
name, root = nbt.load(path)
d = root["Data"][1]
p = d["Player"][1]
p["Inventory"] = (9, (10, []))
p["EnderItems"] = (9, (10, []))
p["SelectedItemSlot"] = (3, 0)
p["playerGameType"] = (3, 2)
p["XpLevel"], p["XpP"], p["XpTotal"], p["Score"] = (3, 0), (5, 0.0), (3, 0), (3, 0)
p["Pos"] = (9, (6, [25006.497, 41.0, 2725.62]))
p["Rotation"] = (9, (5, [-178.656, 16.5]))
d["GameType"] = (3, 2)
nbt.save(path, name, root)
print("inventory", len(p["Inventory"][1][1]), "mode", p["playerGameType"][1])
