# -*- coding: utf-8 -*-
"""ST01 未実装カードの効果を公式テキストに合わせて適用（実装済みはスキップ）。"""
from __future__ import annotations

import re
from pathlib import Path

import rewrite_st_effects as rs

CARDS_DIR = Path(r"d:\game\My project\Assets\Resources\Data\Cards")
LOG = Path(r"d:\game\gcg-card-scraper\logs\scrape_20260914_163532.log")

WBT = 33
ACADEMY = 23

# --- 公式テキスト（ログ / 旧スクレイプ） ---
TEXT = {
    "ST01-001": "<Repair 2>\n【During Pair】During your turn, all your Units get AP+1.",
    "ST01-003": "-",
    "ST01-005": "-",
    "ST01-006": "【When Paired】Choose 1 enemy Unit that is Lv.5 or lower. It gets AP-3 during this turn.",
    "ST01-007": "-",
    "ST01-008": "<Blocker>",
    "ST01-009": "<Blocker>\nThis Unit can't choose the enemy player as its attack target.",
    "ST01-010": "【Burst】Add this card to your hand.\n【When Paired】Choose 1 enemy Unit with 5 or less HP. Rest it.",
    "ST01-011": "【Burst】Add this card to your hand.\n【Attack】【Once per Turn】Choose 1 of your Resources. Set it as active.",
    "ST01-012": "【Main】Choose 1 rested enemy Unit. Deal 1 damage to it.",
    "ST01-013": "【Main】Choose 1 friendly Unit. It recovers 3 HP.",
    "ST01-014": "【Burst】Activate this card's 【Main】.\n【Main】/【Action】Choose 1 enemy Unit. It gets AP-3 during this turn.",
    "ST01-015": "【Burst】Deploy this card.\n【Deploy】Add 1 of your Shields to your hand.\n【Activate･Main】【Once per Turn】②：Deploy token by unit count.",
    "ST01-016": "【Burst】Deploy this card.\n【Deploy】Add 1 of your Shields to your hand.\n【Activate･Main】Rest this Base：All friendly Link Units get AP+1 during this turn.",
}

DEBUFF_AP3 = [
    rs.effect(type=3, value=3, target=2, selectionMode=1, duration=1, statTarget=0),
]

DEBUFF_AP3_LV5 = [
    rs.effect(
        type=3,
        value=3,
        target=2,
        selectionMode=1,
        duration=1,
        statTarget=0,
        targetUnitFilterStat=3,
        targetUnitStatCompareOp=3,
        targetUnitStatCompareValue=5,
    ),
]

REST_ENEMY_HP5 = [
    rs.effect(
        type=10,
        value=1,
        target=2,
        selectionMode=1,
        targetUnitFilterStat=1,
        targetUnitStatCompareOp=3,
        targetUnitStatCompareValue=5,
    ),
]

DAMAGE1_RESTED_ENEMY = [
    rs.effect(type=0, value=1, target=7, selectionMode=1),
]

RECOVER3_ALLY = [
    rs.effect(type=32, value=3, target=1, selectionMode=1),
]


def deploy_wbt_token(ap: int, hp: int):
    return rs.effect(
        type=22,
        value=1,
        target=5,
        selectionMode=-1,
        deployUnitSource=0,
        deployUnitAsRested=1,
        deployUnitOverrideAp=ap,
        deployUnitOverrideHp=hp,
        targetFeatureId=WBT,
    )


def field_unit_count_cond(threshold: int, op: int = 2, board_side: int = 0):
    """CompareFieldUnitCount on owner battle zone."""
    return rs.cond(
        boardSide=board_side,
        checkKind=13,
        turnCheck=-1,
        unitCountCompareOp=op,
        unitCountThreshold=threshold,
        minimumCount=0,
    )


ST01_EFFECTS: dict[str, list[str]] = {}
ST01_BLOCKER: dict[str, int] = {}
ST01_FLAGS: dict[str, dict] = {}

# ST01-001: During Pair + 自ターン 味方全体 AP+1（Repair は isRepair）
ST01_EFFECTS["ST01-001"] = [
    rs.timed(
        15,
        [rs.effect(type=2, value=1, target=3, selectionMode=0, duration=1)],
        conds=[
            rs.cond(checkKind=4, minimumCount=1),
            rs.cond(checkKind=-1, turnCheck=0),
        ],
    ),
]
ST01_FLAGS["ST01-001"] = {"isRepair": 1, "repairAmount": 2}

# ST01-003 / 005 / 007: 効果なし
for g in ("ST01-003", "ST01-005", "ST01-007"):
    ST01_EFFECTS[g] = []

# ST01-006
ST01_EFFECTS["ST01-006"] = [
    rs.timed(15, DEBUFF_AP3_LV5),
]

# ST01-008
ST01_EFFECTS["ST01-008"] = []
ST01_BLOCKER["ST01-008"] = 1

# ST01-009
ST01_EFFECTS["ST01-009"] = []
ST01_BLOCKER["ST01-009"] = 1
ST01_FLAGS["ST01-009"] = {"isNotDirectAttack": 1}

# ST01-010
ST01_EFFECTS["ST01-010"] = [
    rs.timed(5, effects_name="AddSelfToHand_OnBurst"),
    rs.timed(15, REST_ENEMY_HP5),
]

# ST01-011
ST01_EFFECTS["ST01-011"] = [
    rs.timed(5, effects_name="AddSelfToHand_OnBurst"),
    rs.timed(3, effects_name="ActivateResource1_Self_OnAttack", once_per_turn=1),
]

# ST01-012
ST01_EFFECTS["ST01-012"] = [
    rs.timed(12, DAMAGE1_RESTED_ENEMY),
]

# ST01-013
ST01_EFFECTS["ST01-013"] = [
    rs.timed(12, RECOVER3_ALLY),
]

# ST01-014
ST01_EFFECTS["ST01-014"] = [
    rs.timed(5, effects_name="ActivateSelfOnMain_OnBurst"),
    rs.timed(12, DEBUFF_AP3),
    rs.timed(8, DEBUFF_AP3),
]

# ST01-015
ST01_EFFECTS["ST01-015"] = [
    rs.timed(5, effects_name="DeployBase1_OnBurst"),
    rs.timed(6, effects_name="AddShield1_OnBaseDeployed"),
    rs.timed(
        12,
        [deploy_wbt_token(3, 3)],
        conds=[field_unit_count_cond(0, 2)],
        activation_cost=2,
        once_per_turn=1,
    ),
    rs.timed(
        12,
        [deploy_wbt_token(2, 2)],
        conds=[field_unit_count_cond(1, 2)],
        activation_cost=2,
        once_per_turn=1,
    ),
    rs.timed(
        12,
        [deploy_wbt_token(1, 1)],
        conds=[field_unit_count_cond(2, 0)],
        activation_cost=2,
        once_per_turn=1,
    ),
]

# ST01-016
ST01_EFFECTS["ST01-016"] = [
    rs.timed(5, effects_name="DeployBase1_OnBurst"),
    rs.timed(6, effects_name="AddShield1_OnBaseDeployed"),
    rs.timed(
        12,
        [
            rs.effect(type=10, value=1, target=0),
            rs.effect(type=2, value=1, target=3, selectionMode=0, duration=1),
        ],
    ),
]


def find_asset(gcg: str) -> Path | None:
    for p in CARDS_DIR.glob("*.asset"):
        t = p.read_text(encoding="utf-8", errors="replace")
        m = re.search(r"^  gcgOfficialId: (.+)$", t, re.M)
        if m and m.group(1).strip().strip('"') == gcg:
            return p
    return None


def is_already_implemented(gcg: str, text: str) -> bool:
    """実装済みとみなしてスキップする条件。"""
    p = find_asset(gcg)
    if not p:
        return False
    body = p.read_text(encoding="utf-8", errors="replace")

    if gcg == "ST01-002":
        return "timing: 15" in body and "type: 1" in body and "target: 5" in body
    if gcg == "ST01-004":
        return "timing: 0" in body and "type: 10" in body and "targetUnitFilterStat: 1" in body

    if text.strip() in ("-", "<Blocker>"):
        if gcg == "ST01-008":
            return "isBlocker: 1" in body
        if gcg == "ST01-009":
            return "isBlocker: 1" in body and "isNotDirectAttack: 1" in body
        return "timedEffects: []" in body or "timedEffects:\n" not in body

    expected = ST01_EFFECTS.get(gcg)
    if expected is None:
        return True

    # 簡易指紋：主要 timing / effectsName / 代表 type
    fingerprints = []
    for block in expected:
        if "timing: 5" in block and "AddSelfToHand" in block:
            fingerprints.append("AddSelfToHand_OnBurst")
        if "timing: 5" in block and "ActivateSelfOnMain" in block:
            fingerprints.append("ActivateSelfOnMain_OnBurst")
        if "timing: 5" in block and "DeployBase1" in block:
            fingerprints.append("DeployBase1_OnBurst")
        if "ActivateResource1_Self_OnAttack" in block:
            fingerprints.append("ActivateResource1_Self_OnAttack")
        if "target: 7" in block:
            fingerprints.append("rested_enemy_dmg")
        if "type: 32" in block and "target: 1" in block:
            fingerprints.append("recover_ally")
        if "targetUnitFilterStat: 3" in block and "type: 3" in block:
            fingerprints.append("debuff_lv5")
        if deploy_wbt_token(3, 3)[:80] in block or "deployUnitOverrideAp: 3" in block:
            fingerprints.append("white_base_token")
        if "type: 10" in block and "type: 2" in block and "target: 3" in block:
            fingerprints.append("earth_house_main")

    for fp in fingerprints:
        if fp == "AddSelfToHand_OnBurst" and "AddSelfToHand_OnBurst" not in body and "type: 27" in body:
            return False
        if fp == "AddSelfToHand_OnBurst" and "AddSelfToHand_OnBurst" not in body:
            return False
        if fp == "ActivateSelfOnMain_OnBurst" and "ActivateSelfOnMain_OnBurst" not in body:
            return False
        if fp == "ActivateResource1_Self_OnAttack" and "ActivateResource1_Self_OnAttack" not in body:
            return False
        if fp == "rested_enemy_dmg" and "target: 7" not in body:
            return False
        if fp == "recover_ally" and not re.search(r"type: 32[\s\S]*target: 1", body):
            return False
        if fp == "debuff_lv5" and "targetUnitFilterStat: 3" not in body:
            return False
        if fp == "white_base_token" and "deployUnitOverrideAp: 3" not in body:
            return False
        if fp == "earth_house_main" and not re.search(r"type: 10[\s\S]*type: 2", body):
            return False

    if gcg == "ST01-001":
        return "checkKind: 4" in body and "target: 3" in body and "isRepair: 1" in body
    if gcg == "ST01-014":
        return "ActivateSelfOnMain_OnBurst" in body and "timing: 12" in body
    if gcg == "ST01-006":
        return "targetUnitFilterStat: 3" in body
    if gcg == "ST01-010":
        return "AddSelfToHand_OnBurst" in body and "targetUnitFilterStat: 1" in body
    if gcg == "ST01-009":
        return "isBlocker: 1" in body and "isNotDirectAttack: 1" in body
    if gcg == "ST01-015":
        return (
            "deployUnitOverrideAp: 3" in body
            and "deployUnitOverrideAp: 2" in body
            and "deployUnitAsRested: 1" in body
            and body.count("activationCost: 2") >= 3
        )

    return "timedEffects: []" not in body and len(fingerprints) > 0


def apply_flags(text: str, flags: dict) -> str:
    for key, val in flags.items():
        text = re.sub(rf"  {key}: \d+", f"  {key}: {val}", text, count=1)
    return text


def main():
    updated = []
    skipped = []
    missing = []

    for gcg in sorted(ST01_EFFECTS.keys()):
        if is_already_implemented(gcg, TEXT.get(gcg, "")):
            skipped.append(gcg)
            continue
        p = find_asset(gcg)
        if not p:
            missing.append(gcg)
            continue
        raw = p.read_text(encoding="utf-8", errors="replace")
        new_text = rs.replace_block(raw, ST01_EFFECTS[gcg], ST01_BLOCKER.get(gcg, 0))
        if gcg in ST01_FLAGS:
            new_text = apply_flags(new_text, ST01_FLAGS[gcg])
        new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
        p.write_bytes(new_text.encode("utf-8"))
        updated.append(f"{gcg} -> {p.name}")

    print("updated:", len(updated))
    for u in updated:
        print(" ", u)
    print("skipped:", len(skipped), skipped)
    print("missing:", missing)


if __name__ == "__main__":
    main()
