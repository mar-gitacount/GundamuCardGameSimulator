# -*- coding: utf-8 -*-
"""スクレイプ済み GD01 カードの timedEffects / isBlocker / isRepair を書き直す。"""
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
      compareTargetStatToSource: {compareTargetStatToSource}
      compareTargetStatToPriorChainPicked: 0
      abortRemainingChainOnSkip: {abortRemainingChainOnSkip}
      requireChainObservationContext: {requireChainObservationContext}
{econds}      filterTargetUnitLevel: 0
      filterByTargetCardType: {filterByTargetCardType}
      targetCardType: {targetCardType}
      deployUnitSource: {deployUnitSource}
      deployCardId: {deployCardId}
      filterByDeployCardId: 0
      filterDeployCandidateByFeature: 0
      deployUnitTriggerOnPlayed: {deployUnitTriggerOnPlayed}
      deployUnitAsRested: {deployUnitAsRested}
      deployUnitPayCost: 0
      deployUnitOverrideAp: 0
      deployUnitOverrideHp: 0
      grantAttackFlagOnlyIfOff: 1
      revealDiscardedToOpponent: {revealDiscardedToOpponent}
      forbidSkipHandDiscard: {forbidSkipHandDiscard}
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
        compareTargetStatToSource=0,
        requireChainObservationContext=0,
        abortRemainingChainOnSkip=0,
        revealDiscardedToOpponent=0,
        forbidSkipHandDiscard=0,
        filterByTargetCardType=0,
        targetCardType=0,
        deployUnitSource=0,
        deployCardId=0,
        deployUnitTriggerOnPlayed=0,
        deployUnitAsRested=0,
        selectMinCount=0,
        selectMaxCount=0,
        autoSelectHighestUnitStat=0,
        targetCardNameContains="",
        econds="      effectActivationConditions: []\n",
    )
    extra_conds = kw.pop("effectActivationConditions", None)
    d.update(kw)
    if extra_conds:
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


def main_action(blocks):
    """【Main】/【Action】共通効果を Main(12) と Action(8) 両方に複製。"""
    out = []
    for b in blocks:
        out.append(b)
        out.append(b.replace("  - timing: 12", "  - timing: 8", 1))
    return out


# Feature IDs
EF = 2
ZAFT = 3
EA = 4
ZEON = 5
NEO = 6
TSA = 8
OM = 9
NEWTYPE = 19
ACAD = 23
WBT = 33
MC = 34
OZ = 36
GTEAM = 37
CYBER = 39
COORD = 32

EFFECTS = {}
BLOCKER = {}
REPAIR = {}

SKIP = {
    "GD01-002", "GD01-006", "GD01-008", "GD01-015", "GD01-016", "GD01-020", "GD01-023",
    "GD01-025", "GD01-030", "GD01-049", "GD01-054", "GD01-065", "GD01-066", "GD01-068",
    "GD01-073", "GD01-086", "GD01-090", "GD01-100", "GD01-118",
}

ALL = [f"GD01-{i:03d}" for i in range(1, 131) if f"GD01-{i:03d}" not in SKIP]


# --- card definitions ---
EFFECTS["GD01-001"] = [
    timed(0, effects_name="SyncTurnEndRepairFromCalamityWarUnitTokens"),
    timed(15, [effect(type=1, value=1, target=5)], conds=[cond(boardSide=0, checkKind=1, minimumCount=3)]),
]
EFFECTS["GD01-003"] = [
    timed(3, [
        effect(type=20, value=12, target=5, selectionMode=1),
        effect(type=35, value=1, target=0),
        effect(type=46, value=1, target=0, duration=1),
    ], conds=[cond(checkKind=25)]),
]
EFFECTS["GD01-004"] = [
    timed(15, [effect(type=10, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)]),
]
REPAIR["GD01-004"] = 1
EFFECTS["GD01-005"] = [
    timed(18, [
        effect(type=25, value=1, target=0),
        effect(type=24, value=1, target=5, selectionMode=1),
    ], conds=[cond(checkKind=25)]),
]
EFFECTS["GD01-007"] = [
    timed(9, [effect(type=1, value=1, target=5)], conds=[cond(boardSide=0, checkKind=0, featureId=OZ, minimumCount=2)]),
]
EFFECTS["GD01-009"] = [
    timed(0, [effect(type=13, value=1, target=1, selectionMode=1, targetFeatureId=WBT)], effects_name="GrantSelfHighMobility_UntilEndOfTurn"),
]
EFFECTS["GD01-010"] = [
    timed(15, [effect(type=10, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3)]),
]
for gid in ["GD01-011", "GD01-013", "GD01-018", "GD01-021", "GD01-022", "GD01-031", "GD01-035", "GD01-036", "GD01-037", "GD01-040", "GD01-051", "GD01-057", "GD01-060", "GD01-062", "GD01-064", "GD01-077", "GD01-079", "GD01-083", "GD01-084", "GD01-085"]:
    EFFECTS[gid] = []
EFFECTS["GD01-012"] = EFFECTS["GD01-010"]
EFFECTS["GD01-014"] = [
    timed(18, [effect(type=32, value=1, target=4, selectionMode=1)], activation_cost=0, once_per_turn=1),
]
REPAIR["GD01-017"] = 1
EFFECTS["GD01-017"] = []
BLOCKER["GD01-019"] = 1
EFFECTS["GD01-019"] = [
    timed(11, [], conds=[cond(boardSide=1, checkKind=1, minimumCount=4)]),
]
EFFECTS["GD01-024"] = [
    timed(0, effects_name="HighMobility"),
    # 【Deploy】敵味方問わず Lv.5以下の全ユニットに3ダメージ
    timed(
        0,
        [
            effect(type=0, value=3, target=3, selectionMode=-1, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5),
            effect(type=0, value=3, target=4, selectionMode=-1, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5),
        ],
    ),
]
# 【During Pair】【Destroyed】レストで Char's Zaku Ⅱ トークン(T-006 / id:1000536)を配備
EFFECTS["GD01-026"] = [
    timed(
        9,
        [effect(type=22, value=1, target=5, deployCardId=1000536, deployUnitAsRested=1)],
        conds=[cond(checkKind=4, minimumCount=1)],
    ),
]
EFFECTS["GD01-027"] = [
    timed(0, [effect(type=31, value=4, target=0)]),
    timed(0, [effect(type=0, value=4, target=4, selectionMode=0, filterTargetIsBlocker=1)], conds=[cond(boardSide=4, checkKind=15, featureId=ZEON, minimumCount=10)]),
]
EFFECTS["GD01-028"] = [
    timed(0, [effect(type=8, value=1, target=5, selectionMode=1, targetFeatureId=MC, filterByTargetCardType=1, targetCardType=0, deployUnitSource=1)]),
]
EFFECTS["GD01-029"] = [
    timed(0, [effect(type=31, value=4, target=0)]),
    timed(3, [effect(type=9, value=1, target=2, selectionMode=1, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3, filterTargetIsBlocker=1)]),
]
EFFECTS["GD01-032"] = [
    timed(15, [effect(type=9, value=1, target=2, selectionMode=1, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2, filterTargetIsBlocker=1)], conds=[cond(checkKind=4, featureId=ZEON, minimumCount=1)]),
]
REPAIR["GD01-033"] = 1
EFFECTS["GD01-033"] = []
EFFECTS["GD01-034"] = [
    timed(18, effects_name="Breach3"),
]
EFFECTS["GD01-038"] = [
    timed(0, [effect(type=0, value=1, target=2, selectionMode=0)], conds=[cond(boardSide=1, checkKind=1, minimumCount=5)]),
]
EFFECTS["GD01-039"] = [
    timed(0, effects_name="LookTop1_PlayDeck"),
]
EFFECTS["GD01-041"] = [
    timed(0, effects_name="Breach3"),
]
EFFECTS["GD01-042"] = [
    timed(0, effects_name="AttackActiveEnemyUnit_Lv4OrLess_Permanent"),
]
EFFECTS["GD01-043"] = [
    timed(0, [effect(type=12, value=1, target=1, selectionMode=1, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=4, duration=1)]),
]
EFFECTS["GD01-044"] = [
    timed(15, [effect(type=0, value=1, target=2, selectionMode=5, selectMinCount=1, selectMaxCount=2)], conds=[cond(checkKind=4, featureId=CYBER, minimumCount=1)]),
]
# 【When Paired】山札上3枚を見る。その中の Lv.4以下 (ZAFT) ユニットを1体配備してもよい。残りはランダムで山札下へ
EFFECTS["GD01-045"] = [
    timed(15, effects_name="LookTop3_SelfDeck"),
    timed(
        17,
        [
            effect(
                type=51,
                value=1,
                target=5,
                selectionMode=-1,
                targetFeatureId=ZAFT,
                filterByTargetCardType=1,
                targetCardType=0,
                targetUnitFilterStat=3,
                targetUnitStatCompareOp=3,
                targetUnitStatCompareValue=4,
                deployUnitTriggerOnPlayed=1,
            )
        ],
    ),
    timed(17, effects_name="ShuffleLookedRemainderToDeckBottom"),
]
EFFECTS["GD01-046"] = [
    # 《援護3》は何度でも可。アクティブ化のみ EffectData.oncePerTurn（named JSON）
    timed(
        12,
        effects_name="Support3_RestSelf_BuffAllyOtherAp3_ActivateSelfIfCoordinatorBuffedZaft_OnMain",
        once_per_turn=0,
    ),
]
EFFECTS["GD01-047"] = [
    timed(3, [effect(type=0, value=3, target=2, selectionMode=1)], conds=[cond(boardSide=0, checkKind=1, minimumCount=3)]),
]
EFFECTS["GD01-048"] = [
    timed(12, effects_name="Support1_RestSelf_BuffAllyOtherAp1_OnMain"),
    timed(0, effects_name="LookTop1_SelfDeck"),
]
EFFECTS["GD01-050"] = [
    timed(3, [effect(type=0, value=2, target=2, selectionMode=1)], conds=[cond(checkKind=5, activationStatTarget=0, compareOp=4, compareValue=5)]),
]
EFFECTS["GD01-052"] = [
    timed(0, [effect(type=0, value=1, target=2, selectionMode=1)]),
]
EFFECTS["GD01-053"] = [
    timed(12, [effect(type=0, value=1, target=2, selectionMode=1, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)], once_per_turn=1),
]
EFFECTS["GD01-055"] = [
    timed(12, effects_name="Support2_RestSelf_BuffAllyOtherAp2_OnMain"),
]
EFFECTS["GD01-056"] = [
    timed(9, [effect(type=0, value=1, target=2, selectionMode=1, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5)]),
]
EFFECTS["GD01-058"] = [
    timed(8, [effect(type=2, value=1, target=4, selectionMode=1, targetUnitFilterStat=3, targetUnitStatCompareOp=4, targetUnitStatCompareValue=4, duration=1)], once_per_turn=1, activation_cost=1),
]
EFFECTS["GD01-059"] = [
    timed(3, [effect(type=2, value=2, target=0, duration=1)], conds=[cond(checkKind=30)]),
]
EFFECTS["GD01-061"] = [
    timed(12, effects_name="Support1_RestSelf_BuffAllyOtherAp1_OnMain"),
]
EFFECTS["GD01-063"] = [
    timed(3, [effect(type=46, value=1, target=0, duration=1)], conds=[cond(checkKind=-1, turnCheck=0), cond(checkKind=5, activationStatTarget=3, compareOp=3, compareValue=2)]),
]
EFFECTS["GD01-067"] = [
    timed(15, [effect(type=26, value=1, target=5, selectionMode=1, filterByTargetCardType=1, targetCardType=2, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5)]),
]
EFFECTS["GD01-069"] = [
    timed(12, [effect(type=35, value=1, target=1, selectionMode=1, filterTargetIsBlocker=1)], once_per_turn=1, activation_cost=1),
]
EFFECTS["GD01-070"] = [
    timed(13, effects_name="ScaleSelfCostLevelByOwnerOrbUnits"),
]
EFFECTS["GD01-071"] = [
    timed(18, [effect(type=3, value=2, target=2, selectionMode=1, duration=1)], conds=[cond(checkKind=25)]),
]
BLOCKER["GD01-072"] = 1
EFFECTS["GD01-072"] = []
BLOCKER["GD01-081"] = 1
EFFECTS["GD01-081"] = [
    timed(11, [effect(type=2, value=1, target=0), effect(type=41, value=1, target=0)], conds=[cond(boardSide=0, checkKind=0, featureId=TSA, minimumCount=2)]),
]
EFFECTS["GD01-074"] = [
    timed(3, [effect(type=1, value=1, target=5), effect(type=24, value=1, target=5, selectionMode=1)]),
]
EFFECTS["GD01-075"] = [
    timed(0, [effect(type=7, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=1)]),
]
EFFECTS["GD01-076"] = [
    timed(11, [effect(type=2, value=1, target=0), effect(type=4, value=1, target=0)], conds=[cond(boardSide=4, checkKind=28, compareValue=2, minimumCount=4)]),
]
EFFECTS["GD01-078"] = [
    timed(0, [effect(type=3, value=1, target=2, selectionMode=1, duration=1)]),
]
EFFECTS["GD01-080"] = [
    timed(9, [effect(type=7, value=1, target=2, selectionMode=1, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)]),
]
EFFECTS["GD01-082"] = [
    timed(18, [effect(type=3, value=1, target=2, selectionMode=1, duration=1)], once_per_turn=1, activation_cost=2),
]

# Pilots
for gid in ["GD01-087", "GD01-088", "GD01-089", "GD01-091", "GD01-092", "GD01-093", "GD01-094", "GD01-095", "GD01-096", "GD01-097", "GD01-098"]:
    EFFECTS[gid] = [timed(5, effects_name="AddSelfToHand_OnBurst")]
EFFECTS["GD01-088"].append(timed(18, effects_name="Draw1_OnLink"))
EFFECTS["GD01-089"].append(timed(11, [effect(type=2, value=1, target=0)], conds=[cond(checkKind=13)]))
EFFECTS["GD01-091"].append(timed(11, effects_name="SelfEffectDamageImmunity_OnPlayed"))
EFFECTS["GD01-092"].append(timed(11, effects_name="Breach1"))
# 【During Link】【Attack】Choose 1 enemy Unit whose Lv. <= this Unit. Deal 1 damage.
# SourceUnitIsLinked=17, OnAttack=3, Level filter + compareTargetStatToSource
EFFECTS["GD01-093"].append(
    timed(
        3,
        [
            effect(
                type=0,
                value=1,
                target=2,
                selectionMode=1,
                targetUnitFilterStat=3,
                targetUnitStatCompareOp=3,
                compareTargetStatToSource=1,
            )
        ],
        conds=[cond(checkKind=17)],
    )
)
EFFECTS["GD01-094"].append(
    timed(
        16,
        [effect(type=1, value=1, target=5)],
        once_per_turn=1,
        # DestroyedByBattleDamage=24, SourceAttackingEnemyUnit=31, DestroyedUnitIsLinked=32
        conds=[cond(checkKind=24), cond(checkKind=31), cond(checkKind=32)],
    )
)
# 【When Linked】Discard 1. If you do, draw 1.（捨て札は Skip 不可・強制）
EFFECTS["GD01-095"].append(
    timed(
        18,
        [
            effect(
                type=24,
                value=1,
                target=5,
                selectionMode=0,
                abortRemainingChainOnSkip=1,
                forbidSkipHandDiscard=1,
            ),
            effect(type=1, value=1, target=5),
        ],
    )
)
BLOCKER["GD01-096"] = 1
EFFECTS["GD01-096"].append(timed(11, [], conds=[cond(checkKind=13)]))
EFFECTS["GD01-097"].append(timed(12, [effect(type=35, value=1, target=0)], once_per_turn=1))
EFFECTS["GD01-098"].append(timed(8, [effect(type=32, value=1, target=0)], once_per_turn=1))

# Commands
EFFECTS["GD01-099"] = [
    timed(5, [effect(type=10, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5)]),
] + main_action([
    timed(12, [effect(type=10, value=1, target=2, selectionMode=5, selectMinCount=1, selectMaxCount=2, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3)]),
])
EFFECTS["GD01-101"] = main_action([timed(12, [effect(type=32, value=3, target=1, selectionMode=1)])])
EFFECTS["GD01-102"] = [timed(12, [effect(type=32, value=2, target=3, selectionMode=0, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=4)])]
EFFECTS["GD01-103"] = main_action([timed(12, [effect(type=10, value=1, target=1, selectionMode=1, targetFeatureId=EF), effect(type=10, value=1, target=2, selectionMode=1)])])
EFFECTS["GD01-104"] = [
    timed(5, effects_name="Burst_Draw1"),
    timed(12, [effect(type=0, value=2, target=2, selectionMode=1)]),
]
EFFECTS["GD01-105"] = [
    timed(5, effects_name="AddSelfToHand_OnBurst"),
    timed(12, [effect(type=2, value=2, target=3, selectionMode=0, duration=1)]),
]
EFFECTS["GD01-106"] = [timed(12, effects_name="DeployUnitToken1_OnPlayed")]
EFFECTS["GD01-107"] = [
    timed(5, effects_name="AddExResource1_Self"),
    timed(12, [effect(type=42, value=1, target=5)]),
]
EFFECTS["GD01-108"] = [timed(12, [effect(type=0, value=2, target=4, selectionMode=0, filterTargetIsBlocker=1)])]
EFFECTS["GD01-109"] = [timed(12, effects_name="LookTop5_SelfDeck")]
EFFECTS["GD01-110"] = main_action([timed(12, [effect(type=12, value=1, target=4, selectionMode=1, targetUnitFilterStat=3, targetUnitStatCompareOp=4, targetUnitStatCompareValue=4, duration=1)])])
EFFECTS["GD01-111"] = [
    timed(5, [effect(type=0, value=2, target=2, selectionMode=1)]),
] + main_action([timed(12, [effect(type=0, value=3, target=2, selectionMode=1)], conds=[cond(checkKind=33)])])
EFFECTS["GD01-112"] = [timed(12, [effect(type=10, value=1, target=1, selectionMode=5, selectMinCount=2, selectMaxCount=2), effect(type=0, value=3, target=2, selectionMode=1)])]
EFFECTS["GD01-113"] = main_action([timed(12, [effect(type=2, value=3, target=1, selectionMode=1, duration=1, targetFeatureId=ZAFT)])])
EFFECTS["GD01-114"] = [timed(8, [effect(type=2, value=1, target=3, selectionMode=5, selectMinCount=2, selectMaxCount=2, duration=1)])]
EFFECTS["GD01-115"] = main_action([timed(12, [effect(type=0, value=1, target=2, selectionMode=1)])])
EFFECTS["GD01-116"] = main_action([timed(12, [effect(type=0, value=2, target=2, selectionMode=1, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)])])
EFFECTS["GD01-117"] = [
    timed(5, effects_name="ActivateSelfOnMain_OnBurst"),
] + main_action([timed(12, [effect(type=7, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5)])])
EFFECTS["GD01-119"] = main_action([timed(12, [effect(type=3, value=2, target=2, selectionMode=1, duration=1, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=4)])])
EFFECTS["GD01-120"] = [
    timed(5, [effect(type=3, value=3, target=2, selectionMode=1, duration=1)]),
    timed(8, [effect(type=2, value=3, target=1, selectionMode=1, duration=1, filterTargetIsBlocker=1)]),
]
EFFECTS["GD01-121"] = [
    timed(5, effects_name="ActivateSelfOnMain_OnBurst"),
    timed(12, [effect(type=35, value=1, target=4, selectionMode=1, filterTargetIsBlocker=1)]),
]
EFFECTS["GD01-122"] = [timed(12, [effect(type=7, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)])]

# Bases
for gid in ["GD01-123", "GD01-124", "GD01-125", "GD01-126", "GD01-127", "GD01-128", "GD01-129", "GD01-130"]:
    EFFECTS[gid] = [
        timed(5, effects_name="DeployBase1_OnBurst"),
        timed(6, effects_name="AddShield1_OnBaseDeployed"),
    ]
EFFECTS["GD01-123"][1] = timed(6, [
    effect(type=6, value=1, target=5),
    effect(type=10, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3),
])
EFFECTS["GD01-124"].append(timed(12, [effect(type=32, value=1, target=1, selectionMode=1)]))
EFFECTS["GD01-125"][1] = timed(6, [effect(type=6, value=1, target=5)])
EFFECTS["GD01-127"].append(timed(8, [effect(type=36, value=3, target=1, selectionMode=1, duration=1, targetFeatureId=ZAFT)], activation_cost=0))
EFFECTS["GD01-129"][1] = timed(6, [
    effect(type=6, value=1, target=5),
    effect(type=7, value=1, target=2, selectionMode=1, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3),
])
EFFECTS["GD01-130"].append(timed(12, [effect(type=3, value=1, target=2, selectionMode=1, duration=1)], conds=[cond(boardSide=0, checkKind=0, featureId=ACAD, minimumCount=1)]))

for gid in ALL:
    EFFECTS.setdefault(gid, [])



def replace_block(text, new_timed, is_blocker, is_repair=0, repair_amount=0):
    m = re.search(r"  timedEffects:.*?\n\s+features:", text, re.S)
    if not m:
        raise SystemExit("timedEffects block not found")
    if new_timed:
        timed_yaml = "  timedEffects:\n" + "\n".join(new_timed) + "\n"
    else:
        timed_yaml = "  timedEffects: []\n"
    text = text[: m.start()] + timed_yaml + "  features:" + text[m.end() :]
    text = re.sub(r"  isBlocker: \d+", f"  isBlocker: {is_blocker}", text, count=1)
    text = re.sub(r"  isRepair: \d+", f"  isRepair: {is_repair}", text, count=1)
    text = re.sub(r"  repairAmount: \d+", f"  repairAmount: {repair_amount}", text, count=1)
    return text


def main():
    n = 0
    for p in sorted(CARDS_DIR.glob("GD01-*.asset")):
        text = p.read_text(encoding="utf-8")
        m = re.search(r"^  gcgOfficialId: (.+)$", text, re.M)
        if not m:
            continue
        gid = m.group(1).strip().strip('"')
        if gid not in EFFECTS:
            continue
        repair_amount = REPAIR.get(gid, 0)
        is_repair = 1 if repair_amount > 0 else 0
        new_text = replace_block(text, EFFECTS[gid], BLOCKER.get(gid, 0), is_repair, repair_amount)
        new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
        p.write_bytes(new_text.encode("utf-8"))
        n += 1
        print("updated", gid, p.name)
    print("done", n)


if __name__ == "__main__":
    main()
