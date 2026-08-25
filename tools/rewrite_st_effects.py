# -*- coding: utf-8 -*-
"""スクレイプ済み ST カードの timedEffects を公式テキスト解釈で書き直す。"""
from pathlib import Path
import re

CARDS_DIR = Path(r"d:\game\My project\Assets\Resources\Data\Cards")

COND_TMPL = """    - boardSide: {boardSide}
      checkKind: {checkKind}
      turnCheck: {turnCheck}
      feature: {{fileID: 0}}
      featureId: {featureId}
      features: []
      featureIds: 
      minimumCount: {minimumCount}
      levelAggregate: {levelAggregate}
      compareOp: {compareOp}
      compareValue: {compareValue}
      unitCountCompareOp: {unitCountCompareOp}
      unitCountThreshold: {unitCountThreshold}
      pilotCardId: 0
      trashCardId: 0
      pilotLevelThreshold: 0
      activationStatTarget: {activationStatTarget}
      observedCardType: 0
      destroyedByOwnerRelation: 0
      cardNameContains: """

EFF_TMPL = """    - type: {type}
      value: {value}
      target: {target}
      selectionMode: {selectionMode}
      statTarget: {statTarget}
      duration: {duration}
      valueMode: {valueMode}
      valueCountBoardSide: {valueCountBoardSide}
      valueCountKind: {valueCountKind}
      valueCountFeature: {{fileID: 0}}
      valueCountFeatureId: {valueCountFeatureId}
      valueCountMinUnitLevel: 0
      valueScaleMaximum: 0
      shieldTokenCardId: 0
      valueCountExcludeSource: 0
      targetFeature: {{fileID: 0}}
      targetFeatureId: {targetFeatureId}
      targetFeatures: []
      targetFeatureIds: 
      targetUnitFilterStat: {targetUnitFilterStat}
      targetUnitStatCompareOp: {targetUnitStatCompareOp}
      targetUnitStatCompareValue: {targetUnitStatCompareValue}
      compareTargetStatToSource: 0
      compareTargetStatToPriorChainPicked: 0
      abortRemainingChainOnSkip: 0
      requireChainObservationContext: {requireChainObservationContext}
{econds}      filterTargetUnitLevel: 0
      filterByTargetCardType: {filterByTargetCardType}
      targetCardType: {targetCardType}
      deployUnitSource: {deployUnitSource}
      deployCardId: 0
      filterByDeployCardId: 0
      filterDeployCandidateByFeature: 0
      deployUnitTriggerOnPlayed: {deployUnitTriggerOnPlayed}
      deployUnitAsRested: 0
      deployUnitPayCost: 0
      deployUnitOverrideAp: 0
      deployUnitOverrideHp: 0
      grantAttackFlagOnlyIfOff: 1
      revealDiscardedToOpponent: 0
      forbidSkipHandDiscard: 0
      revealDrawnToPlayer: 0
      filterTargetIsBlocker: 0
      selectMinCount: {selectMinCount}
      selectMaxCount: {selectMaxCount}
      observedUnitTriggerKind: -1
      autoSelectLowestUnitStat: 0
      autoSelectHighestUnitStat: {autoSelectHighestUnitStat}
      relaxTargetUnitStatFilterWhenTrashHasSourceCopies: 0
      trashRelaxFilterMinCopies: 2
      requireExactExileCount: 0
      targetCardNameContains: {targetCardNameContains}
      targetCardNameExcludes: 
      targetPilotId: 0
      requireTargetLacksBreach: 0
      requireTargetHasNoPilot: 0
      resolveAfterDealtBattleDamage: 0
      optionalPlayerConfirm: 0
      opponentChoosesTarget: 0
      choiceBranches: []
      choicePromptJa: 
      choicePromptEn: """


def cond(**kw):
    d = dict(
        boardSide=-1,
        checkKind=-1,
        turnCheck=-1,
        featureId=0,
        minimumCount=1,
        levelAggregate=0,
        compareOp=0,
        compareValue=0,
        unitCountCompareOp=0,
        unitCountThreshold=0,
        activationStatTarget=-1,
    )
    d.update(kw)
    return COND_TMPL.format(**d)


def econd_block(conds):
    if not conds:
        return "      effectActivationConditions: []\n"
    return "      effectActivationConditions:\n" + "\n".join(
        "      " + line if not line.startswith("    -") else "    " + line
        for line in "\n".join(conds).splitlines()
    ) + "\n"


def effect(**kw):
    d = dict(
        type=0,
        value=0,
        target=0,
        selectionMode=-1,
        statTarget=0,
        duration=0,
        valueMode=0,
        valueCountBoardSide=0,
        valueCountKind=0,
        valueCountFeatureId=0,
        targetFeatureId=0,
        targetUnitFilterStat=-1,
        targetUnitStatCompareOp=3,
        targetUnitStatCompareValue=0,
        requireChainObservationContext=0,
        filterByTargetCardType=0,
        targetCardType=0,
        deployUnitSource=0,
        deployUnitTriggerOnPlayed=0,
        selectMinCount=0,
        selectMaxCount=0,
        autoSelectHighestUnitStat=0,
        targetCardNameContains="",
        econds="      effectActivationConditions: []\n",
    )
    extra_conds = kw.pop("effectActivationConditions", None)
    d.update(kw)
    if extra_conds:
        # effectActivationConditions 配下は G-fred と同じく
        # 「      - boardSide」＋続き行は 8 スペース
        lines = ["      effectActivationConditions:"]
        for c in extra_conds:
            for line in c.splitlines():
                if line.startswith("    -"):
                    lines.append("      " + line[4:])
                elif line.startswith("      "):
                    lines.append("        " + line[6:])
                else:
                    lines.append("        " + line.strip())
        d["econds"] = "\n".join(lines) + "\n"
    return EFF_TMPL.format(**d)


def timed(timing, effects=None, effects_name="", conds=None, activation_cost=0, once_per_turn=0, require_obs=0):
    lines = [f"  - timing: {timing}"]
    if conds:
        lines.append("    activationConditions:")
        lines.extend(conds)
    else:
        lines.append("    activationConditions: []")
    lines.append(f"    requireChainObservationContext: {require_obs}")
    lines.append(f"    effectsName: {effects_name}")
    if effects:
        lines.append("    effects:")
        lines.extend(effects)
    else:
        lines.append("    effects: []")
    lines.append(f"    activationCost: {activation_cost}")
    lines.append(f"    oncePerTurn: {once_per_turn}")
    lines.append("    observedUnitTriggerKind: -1")
    return "\n".join(lines)


# Feature IDs
CB = 25
CLAN = 7
WBT = 33
PURPLE = 6

EFFECTS = {}
BLOCKER = {}

# ST01-002 When Paired White Base Team Pilot -> Draw 1
EFFECTS["ST01-002"] = [
    timed(15, [effect(type=1, value=1, target=5)], conds=[cond(checkKind=4, featureId=WBT, minimumCount=1)])
]
# ST01-003 none
EFFECTS["ST01-003"] = []
# ST01-004 Deploy Rest enemy HP<=2
EFFECTS["ST01-004"] = [
    timed(0, [effect(type=10, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)])
]
# ST02-001 Breach5 + AttackActive Lv<=4
EFFECTS["ST02-001"] = [
    timed(0, effects_name="Breach5"),
    timed(0, effects_name="AttackActiveEnemyUnit_Lv4OrLess_Permanent"),
]
# ST02-002 Deploy EX Resource 1
EFFECTS["ST02-002"] = [
    timed(0, [effect(type=33, value=1, target=5)])
]
# ST02-003 During Pair（パイロット搭乗中）, own turn, destroy by battle damage -> 1 dmg all enemy Lv<=3
EFFECTS["ST02-003"] = [
    timed(
        16,
        [effect(type=0, value=1, target=4, selectionMode=0, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3)],
        conds=[
            cond(checkKind=4),  # MountedPilot（特徴なし＝任意パイロット搭乗＝Pair）
            cond(checkKind=24),  # DestroyedByBattleDamage
            cond(checkKind=-1, turnCheck=0),  # OwnerTurn
        ],
    )
]
EFFECTS["ST02-004"] = []
EFFECTS["ST02-005"] = []
EFFECTS["ST03-003"] = []
# ST03-004 Support 2
EFFECTS["ST03-004"] = [
    timed(12, effects_name="Support2_RestSelf_BuffAllyOtherAp2_OnMain")
]
EFFECTS["ST03-005"] = []
# ST04-002 Deploy Draw 1 then Discard 1
EFFECTS["ST04-002"] = [
    timed(0, [
        effect(type=1, value=1, target=5),
        effect(type=24, value=1, target=5, selectionMode=1),
    ])
]
EFFECTS["ST04-003"] = []
EFFECTS["ST04-004"] = []
BLOCKER["ST04-004"] = 1
EFFECTS["ST04-005"] = []
# ST05-001 Deploy damage other ally + AP+1 same target; Suppress while damaged
EFFECTS["ST05-001"] = [
    timed(0, [
        effect(type=0, value=1, target=8, selectionMode=1),
        effect(type=2, value=1, target=8, selectionMode=4, duration=1),
    ]),
    timed(4, effects_name="Suppress2_OnShieldAttack", conds=[cond(checkKind=13)]),
]
# ST05-002 While damaged AP+2
EFFECTS["ST05-002"] = [
    timed(0, effects_name="BuffSelfAp2_WhileDamaged"),
]
# ST05-003 Activate Main: Rest this Unit : damage 1 ally + AP+1
EFFECTS["ST05-003"] = [
    timed(12, [
        effect(type=10, value=1, target=0),
        effect(type=0, value=1, target=1, selectionMode=1),
        effect(type=2, value=1, target=1, selectionMode=4, duration=1),
    ])
]
# ST05-005 Destroyed Rest enemy AP<=4
EFFECTS["ST05-005"] = [
    timed(9, [effect(type=10, value=1, target=2, selectionMode=1, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=4)])
]
# ST06-001 When Linked if another Clan: First Strike this turn
EFFECTS["ST06-001"] = [
    timed(18, [effect(type=47, value=1, target=0, duration=1)], conds=[cond(boardSide=0, checkKind=0, featureId=CLAN, minimumCount=2)])
]
# ST06-002 Deploy if another Clan: 1 dmg enemy
EFFECTS["ST06-002"] = [
    timed(0, [effect(type=0, value=1, target=2, selectionMode=1)], conds=[cond(boardSide=0, checkKind=0, featureId=CLAN, minimumCount=2)])
]
# ST06-003 Support 1
EFFECTS["ST06-003"] = [
    timed(12, effects_name="Support1_RestSelf_BuffAllyOtherAp1_OnMain")
]
EFFECTS["ST06-004"] = []
# ST06-005 Breach1 + Attack choose 1-2 Clan AP+2
EFFECTS["ST06-005"] = [
    timed(0, effects_name="Breach1"),
    timed(3, [effect(type=2, value=2, target=3, selectionMode=5, duration=1, targetFeatureId=CLAN, selectMinCount=1, selectMaxCount=2)]),
]
# ST07-001 Turn end: 7+ CB in trash -> ActivateResource 1
# When Paired: mill 2, if CB milled draw 1
EFFECTS["ST07-001"] = [
    timed(2, [effect(type=43, value=1, target=5)], conds=[cond(boardSide=4, checkKind=15, featureId=CB, minimumCount=7)]),
    timed(15, [
        effect(type=19, value=2, target=5),
        effect(
            type=1,
            value=1,
            target=5,
            requireChainObservationContext=1,
            effectActivationConditions=[cond(checkKind=7, featureId=CB, minimumCount=1)],
        ),
    ], require_obs=1),
]
EFFECTS["ST07-002"] = []
EFFECTS["ST07-003"] = []
# ST07-004 Blocker while CB Pilot in play
EFFECTS["ST07-004"] = [
    timed(11, [], conds=[cond(boardSide=0, checkKind=4, featureId=CB, minimumCount=1)])
]
BLOCKER["ST07-004"] = 1
# ST07-005 destroy by battle dmg recover 2; During Link AP+2
EFFECTS["ST07-005"] = [
    timed(16, [effect(type=32, value=2, target=0)], conds=[cond(checkKind=24), cond(checkKind=-1, turnCheck=0)]),
    timed(18, effects_name="BuffSelfAp2_Permanent_OnLink"),
]
# ST08-001 Hand: no own Lv6+ -> cost/lv -1 per enemy unit; When Paired dmg 3 highest Lv
EFFECTS["ST08-001"] = [
    timed(
        13,
        effects_name="ScaleSelfCostLevelByEnemyUnits",
        conds=[
            cond(
                boardSide=0,
                checkKind=2,
                levelAggregate=3,
                compareValue=6,
                minimumCount=0,
                unitCountCompareOp=2,
                unitCountThreshold=0,
            )
        ],
    ),
    timed(15, [effect(type=0, value=3, target=2, selectionMode=1, autoSelectHighestUnitStat=1, targetUnitFilterStat=3)]),
]
# ST08-002 Deploy 1 dmg enemy
EFFECTS["ST08-002"] = [
    timed(0, [effect(type=0, value=1, target=2, selectionMode=1)])
]
EFFECTS["ST08-003"] = []
# ST08-004 Attack if attacking enemy unit: 1 dmg choose enemy
EFFECTS["ST08-004"] = [
    timed(3, [effect(type=0, value=1, target=2, selectionMode=1)], conds=[cond(checkKind=31)])  # SourceAttackingEnemyUnit
]
EFFECTS["ST08-005"] = []
# ST09-001 Impulse existing named
EFFECTS["ST09-001"] = [
    timed(12, effects_name="ReturnSelfToDeckBottom_ThenDeployTrashImpulseGundamLv4Plus_OnMain", activation_cost=2)
]
# ST09-003 Breach3 + When Linked 5+ purple trash: 2 dmg all AP<=5
EFFECTS["ST09-003"] = [
    timed(0, effects_name="Breach3"),
    timed(
        18,
        effects_name="Damage2_AllUnits_Ap5OrLess_OnLink",
        conds=[cond(boardSide=4, checkKind=28, compareValue=PURPLE, minimumCount=5)],
    ),
]
# ST09-004 Blocker + Suppress while friendly Base
EFFECTS["ST09-004"] = [
    timed(4, effects_name="Suppress2_OnShieldAttack", conds=[cond(checkKind=29)])
]
BLOCKER["ST09-004"] = 1


def replace_block(text, new_timed, is_blocker):
    # replace timedEffects through observedUnitTriggerKind of last block, until "  features:"
    m = re.search(r"  timedEffects:.*?\n  features:", text, re.S)
    if not m:
        raise SystemExit("timedEffects block not found")
    if new_timed:
        timed_yaml = "  timedEffects:\n" + "\n".join(new_timed) + "\n"
    else:
        timed_yaml = "  timedEffects: []\n"
    text = text[: m.start()] + timed_yaml + "  features:" + text[m.end() :]
    text = re.sub(r"  isBlocker: \d+", f"  isBlocker: {is_blocker}", text, count=1)
    return text


def main():
    n = 0
    for p in sorted(CARDS_DIR.glob("ST*.asset")):
        text = p.read_text(encoding="utf-8")
        m = re.search(r"^  gcgOfficialId: (.+)$", text, re.M)
        if not m:
            continue
        gid = m.group(1).strip().strip('"')
        if gid not in EFFECTS:
            continue
        new_text = replace_block(text, EFFECTS[gid], BLOCKER.get(gid, 0))
        new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
        p.write_bytes(new_text.encode("utf-8"))
        n += 1
        print("updated", gid, p.name)
    print("done", n)


if __name__ == "__main__":
    main()
