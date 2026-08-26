# -*- coding: utf-8 -*-
"""GD01-046 効果更新 + GD01-095 に Dearka_Elthman pilotIds を付与。"""
import re
import runpy
from pathlib import Path

ROOT = Path(r"d:\game\My project")
CARDS = ROOT / "Assets" / "Resources" / "Data" / "Cards"
DEARKA_PILOT_GUID = "8a086fc678e7457ebb4ce544d5e21452"

ns = runpy.run_path(str(ROOT / "tools" / "rewrite_gd01_effects.py"), run_name="not_main")
replace_block = ns["replace_block"]
EFFECTS = ns["EFFECTS"]
BLOCKER = ns["BLOCKER"]
REPAIR = ns["REPAIR"]


def write_crlf(path: Path, text: str):
    text = text.replace("\r\n", "\n").replace("\n", "\r\n")
    path.write_bytes(text.encode("utf-8"))


def fix_046():
    path = next(CARDS.glob("GD01-046*.asset"))
    text = path.read_text(encoding="utf-8")
    repair_amount = REPAIR.get("GD01-046", 0)
    text = replace_block(text, EFFECTS["GD01-046"], BLOCKER.get("GD01-046", 0), 1 if repair_amount else 0, repair_amount)
    # link が Dearka であること確認（既にあれば維持）
    if DEARKA_PILOT_GUID not in text:
        text = re.sub(
            r"(  link:\n  - pilotCardId: 0\n    linkPilotIds:\n)(?:    - \{fileID: 11400000, guid: [0-9a-f]+, type: 2\}\n)?",
            rf"\1    - {{fileID: 11400000, guid: {DEARKA_PILOT_GUID}, type: 2}}\n",
            text,
            count=1,
        )
    write_crlf(path, text)
    print("updated", path.name)


def fix_095_pilot_ids():
    path = next(CARDS.glob("GD01-095*.asset"))
    text = path.read_text(encoding="utf-8")
    if DEARKA_PILOT_GUID in text and re.search(r"pilotIds:\n  - \{fileID:", text):
        print("GD01-095 pilotIds already set")
        return
    text = re.sub(
        r"  pilotIds: \[\]\n",
        f"  pilotIds:\n  - {{fileID: 11400000, guid: {DEARKA_PILOT_GUID}, type: 2}}\n",
        text,
        count=1,
    )
    write_crlf(path, text)
    print("updated GD01-095 pilotIds -> Dearka_Elthman")


if __name__ == "__main__":
    fix_046()
    fix_095_pilot_ids()
