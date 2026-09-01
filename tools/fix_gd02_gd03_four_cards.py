# -*- coding: utf-8 -*-
"""GD02-006 / GD02-021 / GD03-001 / GD03-002 の timedEffects を正しい定義に更新する。"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).parent))
from rewrite_gd01_effects import cond, effect, timed, replace_block

CARDS_DIR = Path(r"d:\game\My project\Assets\Resources\Data\Cards")

BATTLE_DMG_IMMUNITY = 52
DISCARD = 24
ADD_EX = 33
DRAW = 1
REST = 10

MOUNTED_PILOT = 4
OBSERVED_FEATURE = 7
OBSERVED_TYPE = 8
DESTROYING_OWNER_ALLY = 22
DESTROYED_BY_EFFECT = 42
OWNER_TOTAL_LEVEL = 41
SOURCE_MOUNT_HAS_REPAIR = 36

OWNER_TURN = 0
GE = 0
LE = 3

EF = 2
GREEN = 1
UNIT_TYPE = 0

EFFECTS = {
    "GD02-006": [
        timed(
            11,
            [effect(type=BATTLE_DMG_IMMUNITY, value=2, target=0, statTarget=3)],
            conds=[cond(turnCheck=OWNER_TURN)],
        ),
    ],
    "GD02-021": [
        timed(
            0,
            [
                effect(
                    type=DISCARD,
                    value=1,
                    target=5,
                    selectionMode=1,
                    abortRemainingChainOnSkip=1,
                    filterByTargetCardType=1,
                    targetCardType=UNIT_TYPE,
                    filterTargetUnitColor=1,
                    filterTargetUnitColorValue=GREEN,
                    targetFeatureId=EF,
                ),
                effect(
                    type=ADD_EX,
                    value=1,
                    target=5,
                    requireChainObservationContext=1,
                    effectActivationConditions=[
                        cond(checkKind=OBSERVED_FEATURE, featureId=EF, minimumCount=1),
                        cond(checkKind=OBSERVED_TYPE, observedCardType=UNIT_TYPE, minimumCount=1),
                    ],
                ),
                effect(
                    type=DRAW,
                    value=1,
                    target=5,
                    effectActivationConditions=[
                        cond(checkKind=OWNER_TOTAL_LEVEL, compareOp=GE, compareValue=7),
                    ],
                ),
            ],
        ),
    ],
    "GD03-001": [
        timed(15, effects_name="Damage1_RestEnemyUnit_SelectSingle"),
        timed(
            16,
            [effect(type=DRAW, value=1, target=5)],
            conds=[
                cond(checkKind=DESTROYING_OWNER_ALLY),
                cond(checkKind=DESTROYED_BY_EFFECT),
            ],
        ),
    ],
    "GD03-002": [
        timed(
            26,
            [
                effect(
                    type=REST,
                    value=1,
                    target=2,
                    selectionMode=1,
                    targetUnitFilterStat=3,
                    targetUnitStatCompareOp=LE,
                    compareTargetStatToMountHostUnit=1,
                    effectActivationConditions=[
                        cond(checkKind=SOURCE_MOUNT_HAS_REPAIR),
                    ],
                )
            ],
            conds=[cond(checkKind=MOUNTED_PILOT, minimumCount=1)],
        ),
    ],
}

BLOCKER = {"GD02-006": 1}
REPAIR = {"GD03-001": 2, "GD03-002": 3}


def patch_compare_mount_host(text: str) -> str:
    needle = "      compareTargetStatToSource: 0\n      compareTargetStatToPriorChainPicked: 0"
    replacement = (
        "      compareTargetStatToSource: 0\n"
        "      compareTargetStatToMountHostUnit: 1\n"
        "      compareTargetStatToPriorChainPicked: 0"
    )
    if "compareTargetStatToMountHostUnit" not in text:
        text = text.replace(needle, replacement, 1)
    return text


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
        if gid == "GD03-002":
            new_text = patch_compare_mount_host(new_text)
        new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
        path.write_bytes(new_text.encode("utf-8"))
        print("updated", gid, path.name)


if __name__ == "__main__":
    main()
