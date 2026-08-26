# -*- coding: utf-8 -*-
"""GD01-118 / GD05-111 / GD01-050 / GD01-048 効果修正。"""
from pathlib import Path
import re
import runpy

ROOT = Path(r"d:\game\My project")
CARDS = ROOT / "Assets" / "Resources" / "Data" / "Cards"

ns = runpy.run_path(str(ROOT / "tools" / "rewrite_gd01_effects.py"), run_name="not_main")
replace_block = ns["replace_block"]
EFFECTS = ns["EFFECTS"]
BLOCKER = ns["BLOCKER"]
REPAIR = ns["REPAIR"]
timed = ns["timed"]
effect = ns["effect"]
cond = ns["cond"]
ZEON = ns["ZEON"]
NEO = ns["NEO"]


def write_crlf(path: Path, text: str):
    text = text.replace("\r\n", "\n").replace("\n", "\r\n")
    path.write_bytes(text.encode("utf-8"))


def rewrite(gid: str, effects, blocker=0, repair=0):
    path = next(CARDS.glob(f"{gid}*.asset"))
    text = path.read_text(encoding="utf-8")
    text = replace_block(text, effects, blocker, 1 if repair else 0, repair)
    write_crlf(path, text)
    print("updated", path.name)


def fix_gd05_111():
    # ファイル名に zero-width space が入っている場合あり
    paths = list(CARDS.glob("GD05-111*.asset"))
    if not paths:
        raise SystemExit("GD05-111 not found")
    path = paths[0]
    text = path.read_text(encoding="utf-8")
    effects = [
        timed(
            12,
            [
                effect(
                    type=24,
                    value=1,
                    target=5,
                    selectionMode=0,
                    forbidSkipHandDiscard=1,
                    abortRemainingChainOnSkip=1,
                ),
                effect(type=1, value=2, target=5),
            ],
        )
    ]
    text = replace_block(text, effects, 0, 0, 0)
    write_crlf(path, text)
    print("updated", path.name)


def patch_target_feature_ids(text: str, ids_yaml: str) -> str:
    """最初の空 targetFeatureIds: を置換（AddToHandFromLooked 用）。"""
    return text.replace(
        "      targetFeatureIds: \n",
        f"      targetFeatureIds:\n{ids_yaml}",
        1,
    )


def main():
    # GD01-118: Draw2 → Discard1 (Skip不可)。発動元自己捨てはコード側で除外。
    EFFECTS["GD01-118"] = [
        timed(
            12,
            [
                effect(type=1, value=2, target=5),
                effect(
                    type=24,
                    value=1,
                    target=5,
                    selectionMode=0,
                    forbidSkipHandDiscard=1,
                ),
            ],
        )
    ]
    rewrite("GD01-118", EFFECTS["GD01-118"])

    # GD01-050: AP>=5 かつ敵ユニット攻撃中
    EFFECTS["GD01-050"] = [
        timed(
            3,
            [effect(type=0, value=2, target=2, selectionMode=1)],
            conds=[
                cond(checkKind=5, activationStatTarget=0, compareOp=4, compareValue=5),
                cond(checkKind=31),  # SourceAttackingEnemyUnit
            ],
        )
    ]
    rewrite("GD01-050", EFFECTS["GD01-050"])

    # GD01-048: Support1 + Look1 + OnLook(Zeon/NeoZeon Unit may add + reveal) + bottom
    EFFECTS["GD01-048"] = [
        timed(12, effects_name="Support1_RestSelf_BuffAllyOtherAp1_OnMain"),
        timed(0, [effect(type=13, value=1, target=5)]),  # Look
        timed(
            17,
            [
                effect(
                    type=14,  # AddToHandFromLooked
                    value=1,
                    target=5,
                    selectionMode=1,
                    filterByTargetCardType=1,
                    targetCardType=0,  # Unit
                    revealDiscardedToOpponent=1,
                    targetFeatureId=ZEON,  # あとで NEO も追加
                ),
                effect(type=16, value=1, target=5),  # ShuffleLookedRemainderToDeckBottom
            ],
        ),
    ]
    path48 = next(CARDS.glob("GD01-048*.asset"))
    text48 = path48.read_text(encoding="utf-8")
    text48 = replace_block(
        text48,
        EFFECTS["GD01-048"],
        BLOCKER.get("GD01-048", 0),
        0,
        0,
    )
    # Zeon(5) + Neo_Zeon(6)
    text48 = patch_target_feature_ids(
        text48,
        "      - 5\n      - 6\n",
    )
    write_crlf(path48, text48)
    print("updated", path48.name)

    fix_gd05_111()

    # rewrite 定義も同期
    rewrite_path = ROOT / "tools" / "rewrite_gd01_effects.py"
    src = rewrite_path.read_text(encoding="utf-8")

    src = re.sub(
        r'EFFECTS\["GD01-048"\] = \[[^\]]*\]',
        'EFFECTS["GD01-048"] = [\n'
        '    timed(12, effects_name="Support1_RestSelf_BuffAllyOtherAp1_OnMain"),\n'
        "    timed(0, [effect(type=13, value=1, target=5)]),\n"
        "    timed(17, [\n"
        "        effect(type=14, value=1, target=5, selectionMode=1, filterByTargetCardType=1,\n"
        "               targetCardType=0, revealDiscardedToOpponent=1, targetFeatureId=ZEON),\n"
        "        effect(type=16, value=1, target=5),\n"
        "    ]),\n"
        "]",
        src,
        count=1,
        flags=re.S,
    )
    src = re.sub(
        r'EFFECTS\["GD01-050"\] = \[[^\]]*\]',
        'EFFECTS["GD01-050"] = [\n'
        "    timed(3, [effect(type=0, value=2, target=2, selectionMode=1)],\n"
        "           conds=[cond(checkKind=5, activationStatTarget=0, compareOp=4, compareValue=5),\n"
        "                  cond(checkKind=31)]),\n"
        "]",
        src,
        count=1,
        flags=re.S,
    )
    # GD01-118 を SKIP から外し定義追加
    if '"GD01-118"' in src and 'EFFECTS["GD01-118"]' not in src:
        src = src.replace(', "GD01-118"', "")
        src = src.replace(
            'EFFECTS["GD01-117"]',
            'EFFECTS["GD01-118"] = [\n'
            "    timed(12, [\n"
            "        effect(type=1, value=2, target=5),\n"
            "        effect(type=24, value=1, target=5, selectionMode=0, forbidSkipHandDiscard=1),\n"
            "    ]),\n"
            "]\n"
            'EFFECTS["GD01-117"]',
        )
        # 117 may not exist - append before ALL loop instead
        if 'EFFECTS["GD01-118"]' not in src:
            src = src.replace(
                "for gid in ALL:",
                'EFFECTS["GD01-118"] = [\n'
                "    timed(12, [\n"
                "        effect(type=1, value=2, target=5),\n"
                "        effect(type=24, value=1, target=5, selectionMode=0, forbidSkipHandDiscard=1),\n"
                "    ]),\n"
                "]\n"
                "for gid in ALL:",
            )

    rewrite_path.write_text(src, encoding="utf-8")
    print("synced rewrite_gd01_effects.py")


if __name__ == "__main__":
    main()
