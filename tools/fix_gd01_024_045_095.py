# -*- coding: utf-8 -*-
"""GD01-024 / 045 / 095 のみ timedEffects を書き直す。"""
import re
import runpy
from pathlib import Path

ROOT = Path(r"d:\game\My project")
SCRIPT = ROOT / "tools" / "rewrite_gd01_effects.py"
ns = runpy.run_path(str(SCRIPT), run_name="not_main")

TARGETS = {"GD01-024", "GD01-045", "GD01-095"}
CARDS_DIR = ns["CARDS_DIR"]
EFFECTS = ns["EFFECTS"]
BLOCKER = ns["BLOCKER"]
REPAIR = ns["REPAIR"]
replace_block = ns["replace_block"]

for p in sorted(CARDS_DIR.glob("GD01-*.asset")):
    text = p.read_text(encoding="utf-8")
    m = re.search(r"^  gcgOfficialId: (.+)$", text, re.M)
    if not m:
        continue
    gid = m.group(1).strip().strip('"')
    if gid not in TARGETS:
        continue
    repair_amount = REPAIR.get(gid, 0)
    is_repair = 1 if repair_amount > 0 else 0
    new_text = replace_block(text, EFFECTS[gid], BLOCKER.get(gid, 0), is_repair, repair_amount)
    new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
    p.write_bytes(new_text.encode("utf-8"))
    print("updated", gid, p.name)

print("done")
