# -*- coding: utf-8 -*-
"""GD03-003 / GD03-021 / GD03-033 の timedEffects・フラグを更新する。"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).parent))
from rewrite_gd01_effects import cond, effect, timed, replace_block

CARDS_DIR = Path(r"d:\game\My project\Assets\Resources\Data\Cards")

BUFF = 2
DAMAGE = 0
GRANT_ATTACK_ACTIVE = 12

MOUNTED_PILOT = 4
OWNER_TURN = 0

OM = 9
G_TEAM = 37
ZAFT = 3

ON_PILOT_MOUNTED = 15
ON_ATTACK = 3
ON_PLAYED = 0

ALLY_ALL = 3
ENEMY_UNIT = 2
ALLY_UNIT = 1

MULTIPLY = 1
SOURCE_AP_PER_EVERY = 3
UNTIL_EOT = 1

EFFECTS = {
    "GD03-021": [
        timed(
            ON_PLAYED,
            [
                effect(
                    type=GRANT_ATTACK_ACTIVE,
                    value=1,
                    target=ALLY_UNIT,
                    selectionMode=1,
                    duration=UNTIL_EOT,
                    targetFeatureIds="0900000025000000",
                )
            ],
        ),
    ],
    "GD03-033": [
        timed(
            ON_PILOT_MOUNTED,
            [
                effect(type=BUFF, value=2, target=ALLY_ALL, targetFeatureId=ZAFT),
            ],
            conds=[cond(checkKind=MOUNTED_PILOT, featureId=ZAFT, turnCheck=OWNER_TURN)],
        ),
        timed(
            ON_ATTACK,
            [
                effect(
                    type=DAMAGE,
                    value=1,
                    target=ENEMY_UNIT,
                    selectionMode=1,
                    valueMode=MULTIPLY,
                    valueCountKind=SOURCE_AP_PER_EVERY,
                    valueCountMinUnitLevel=4,
                )
            ],
        ),
    ],
}

BLOCKER = {"GD03-003": 1}
REPAIR = {"GD03-003": 1}


def patch_target_feature_ids(text: str) -> str:
    return text.replace(
        "      targetFeatureIds: 0900000025000000",
        "      targetFeatureIds: 0900000025000000",
    )


def main():
    for gid, blocks in EFFECTS.items():
        matches = list(CARDS_DIR.glob(f"{gid}*.asset"))
        if not matches:
            print("missing", gid)
            continue
        path = matches[0]
        text = path.read_text(encoding="utf-8")
        repair_amount = REPAIR.get(gid, 0)
        is_repair = 1 if repair_amount > 0 else 0
        is_blocker = BLOCKER.get(gid, 0)
        new_text = replace_block(text, blocks, is_blocker, is_repair, repair_amount)
        new_text = patch_target_feature_ids(new_text)
        new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
        path.write_bytes(new_text.encode("utf-8"))
        print("updated", gid, path.name)


if __name__ == "__main__":
    main()
