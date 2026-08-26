# -*- coding: utf-8 -*-
"""GD01-032/045 link 確認、GD01-092/094 pilotIds、GD01-094/104 効果更新。"""
from pathlib import Path
import re
import runpy

ROOT = Path(r"d:\game\My project")
CARDS = ROOT / "Assets" / "Resources" / "Data" / "Cards"
M_QUVE = "ed2a9f851af34988be90203afdca546f"
YZAK = "5546ef47ee5745e0b59273d318373a12"

ns = runpy.run_path(str(ROOT / "tools" / "rewrite_gd01_effects.py"), run_name="not_main")
replace_block = ns["replace_block"]
EFFECTS = ns["EFFECTS"]
BLOCKER = ns["BLOCKER"]
REPAIR = ns["REPAIR"]


def write_crlf(path: Path, text: str):
    text = text.replace("\r\n", "\n").replace("\n", "\r\n")
    path.write_bytes(text.encode("utf-8"))


def set_pilot_ids(path: Path, guid: str):
    text = path.read_text(encoding="utf-8")
    if guid in text and re.search(r"pilotIds:\n  - \{fileID:", text):
        print(path.name, "pilotIds already set")
        return
    new_text, n = re.subn(
        r"  pilotIds: \[\]\n",
        f"  pilotIds:\n  - {{fileID: 11400000, guid: {guid}, type: 2}}\n",
        text,
        count=1,
    )
    if n != 1:
        raise SystemExit(f"failed pilotIds on {path.name}")
    write_crlf(path, new_text)
    print("updated pilotIds", path.name)


def rewrite_effects(gid: str):
    path = next(CARDS.glob(f"{gid}*.asset"))
    text = path.read_text(encoding="utf-8")
    repair_amount = REPAIR.get(gid, 0)
    text = replace_block(
        text,
        EFFECTS[gid],
        BLOCKER.get(gid, 0),
        1 if repair_amount else 0,
        repair_amount,
    )
    write_crlf(path, text)
    print("rewrote effects", path.name)


def main():
    for gid, guid, name in [
        ("GD01-032", M_QUVE, "M_Quve"),
        ("GD01-045", YZAK, "Yzak"),
    ]:
        path = next(CARDS.glob(f"{gid}*.asset"))
        text = path.read_text(encoding="utf-8")
        print(f"{gid} link {name}:", "OK" if guid in text else "MISSING")

    set_pilot_ids(next(CARDS.glob("GD01-092*.asset")), M_QUVE)
    set_pilot_ids(next(CARDS.glob("GD01-094*.asset")), YZAK)
    rewrite_effects("GD01-094")
    rewrite_effects("GD01-104")


if __name__ == "__main__":
    main()
