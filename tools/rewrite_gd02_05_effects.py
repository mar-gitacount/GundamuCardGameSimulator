# -*- coding: utf-8 -*-
"""スクレイプ済み GD02–GD05 カードの timedEffects / isBlocker / isRepair を書き直す。

英語効果テキストをパターン解釈し、既存 EffectType / CheckKind / named effects を優先する。
"""
from __future__ import annotations

import re
from pathlib import Path

CARDS_DIR = Path(r"d:\game\My project\Assets\Resources\Data\Cards")
LOG_PATH = Path(r"D:\game\gcg-card-scraper\logs\scrape_20260825_162506.log")

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
      filterTargetIsBlocker: {filterTargetIsBlocker}
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

# Feature IDs（よく使うもの）
FEATURE_IDS = {
    "Titans": 21,
    "AEUG": 41,
    "Zeon": 5,
    "Neo Zeon": 6,
    "ZAFT": 3,
    "Zaft": 3,
    "Earth Federation": 2,
    "Earth Alliance": 4,
    "Cyber-Newtype": 39,
    "Newtype": 19,
    "OZ": 36,
    "CB": 25,
    "White Base Team": 33,
    "G Team": 37,
    "Orb": 18,
    "Clan": 7,
    "Vulture": 66,
    "X-Rounder": 67,
    "White Fang": 42,
    "Gjallarhorn": 43,
    "Special Move": 11,
    "Academy": 23,
    "Coordinator": 32,
    "Warship": 30,
    "Stronghold": 40,
    "Operation Meteor": 9,
    "Tekkadan": 28,
    "Gundam Frame": 29,
    "Triple Ship Alliance": 8,
    "Minerva Squad": 31,
    "Maganac Corps": 34,
    "Mafty": 35,
    "Preventer": 55,
    "Phantom Pain": 54,
    "Vagan": 65,
    "Zanscare": 68,
    "League Militaire": 16,
    "Victory Type": 17,
    "Gundam Fighter": 13,
    "Shuffle Alliance": 14,
    "Moonrace": 51,
    "Asuno Family": 69,
    "AGE System": 44,
    "Alaya-Vijnana": 45,
    "Biological CPU": 46,
    "Trinity": 62,
    "UE": 63,
    "UN": 64,
    "SRA": 57,
    "Satyricon": 58,
    "Side 6": 59,
    "Super Soldier": 60,
    "Karaba": 50,
    "New UNE": 52,
    "Old UNE": 53,
    "Quiet Zero": 56,
    "Dawn of Fold": 48,
    "Cyclops Team": 47,
    "Jupitris": 49,
    "Machu": 70,
    "Civilian": 38,
    "Militia": 24,
    "Teiwaz": 27,
    "Shrike Team": 22,
    "GN Drive": 26,
    "Superpower Bloc": 61,
}


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
        filterByTargetCardType=0,
        targetCardType=0,
        deployUnitSource=0,
        deployUnitTriggerOnPlayed=0,
        filterTargetIsBlocker=0,
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
    out = []
    for b in blocks:
        out.append(b)
        out.append(b.replace("  - timing: 12", "  - timing: 8", 1))
    return out


def load_effect_texts():
    log_text = LOG_PATH.read_text(encoding="utf-8")
    result = {}
    blocks = re.split(r"(?=^ID: GD0[2-5]-\d{3}$)", log_text, flags=re.M)
    for block in blocks:
        m = re.match(r"ID: (GD0[2-5]-\d{3})\s*$", block, re.M)
        if not m:
            continue
        if re.search(r"^結果: NG", block, re.M):
            continue
        em = re.search(r"^効果:\n(.*?)(?=^地形:)", block, re.M | re.S)
        result[m.group(1)] = (em.group(1).strip() if em else "-")
    return result


def strip_keyword_explanations(text: str) -> str:
    """<Keyword N> (explanation...) の説明括弧を除去。"""
    text = re.sub(
        r"<(Blocker|Repair|Breach|Support|High-Maneuver|First Strike|Suppression)[^>]*>\s*\([^)]*\)",
        lambda m: m.group(0).split("(")[0].strip(),
        text,
    )
    return text


def parse_keywords(text: str):
    """カード先頭の常駐キーワードのみ抽出（本文中の gains <X> は除外）。"""
    # 行頭または単独行のキーワード
    head = []
    for line in text.splitlines():
        s = line.strip()
        if re.match(r"^<(Blocker|Repair|Breach|Support|High-Maneuver|First Strike|Suppression)\b", s, re.I):
            head.append(s)
        elif re.match(r"^\[Suppression\]", s, re.I):
            head.append(s)
        else:
            break
    head_text = "\n".join(head)
    blocker = bool(re.search(r"<Blocker>", head_text, re.I))
    repair = 0
    m = re.search(r"<Repair\s*(\d+)?\s*>", head_text, re.I)
    if m:
        repair = int(m.group(1) or 1)
    breach = None
    m = re.search(r"<Breach\s*(\d+)\s*>", head_text, re.I)
    if m:
        breach = int(m.group(1))
    support = None
    m = re.search(r"<Support\s*(\d+)\s*>", head_text, re.I)
    if m:
        support = int(m.group(1))
    high_maneuver = bool(re.search(r"<High-Maneuver>", head_text, re.I))
    first_strike = bool(re.search(r"<First Strike>", head_text, re.I))
    suppression = bool(re.search(r"[<\[]Suppression[>\]]", head_text, re.I))
    return {
        "blocker": blocker,
        "repair": repair,
        "breach": breach,
        "support": support,
        "high_maneuver": high_maneuver,
        "first_strike": first_strike,
        "suppression": suppression,
    }


def split_clauses(text: str):
    """【Timing】マーカーで効果文を分割。連続タグは同一条項にまとめる。"""
    text = strip_keyword_explanations(text)
    pattern = re.compile(r"(【[^】]+】)")
    parts = pattern.split(text)
    clauses = []
    i = 0
    if parts and not parts[0].startswith("【"):
        preamble = parts[0].strip()
        if preamble and preamble not in ("-",):
            clauses.append({"tags": [], "body": preamble})
        i = 1
    while i < len(parts):
        tags = []
        while i < len(parts) and parts[i].startswith("【"):
            tags.append(parts[i][1:-1].strip())
            i += 1
        body = ""
        if i < len(parts) and not parts[i].startswith("【"):
            body = parts[i].strip()
            i += 1
        # タグ間の空文字で分断された連続【…】を吸収
        while not body and i < len(parts):
            while i < len(parts) and parts[i].startswith("【"):
                tags.append(parts[i][1:-1].strip())
                i += 1
            if i < len(parts) and not parts[i].startswith("【"):
                body = parts[i].strip()
                i += 1
            else:
                break
        if tags or body:
            clauses.append({"tags": tags, "body": body})
    return clauses


def timing_from_tags(tags):
    """タグから主タイミングと修飾条件を決定。"""
    once = any("Once per Turn" in t for t in tags)
    conds = []
    timing = None
    activation_cost = 0
    is_activate = False
    pair_feature = None
    pair_level = None
    pair_color = None

    for t in tags:
        if t.startswith("During Link") or t == "During Link":
            conds.append(cond(checkKind=17))  # SourceUnitIsLinked
        elif t.startswith("During Pair"):
            # 【During Pair】(Feature) Pilot / Lv.N / Color
            fm = re.search(r"\(([^)]+)\)", t)
            if fm and fm.group(1) in FEATURE_IDS:
                pair_feature = FEATURE_IDS[fm.group(1)]
                conds.append(cond(checkKind=4, featureId=pair_feature, minimumCount=1))
            else:
                lm = re.search(r"Lv\.?\s*(\d+)\s*or Lower", t, re.I)
                if lm:
                    pair_level = int(lm.group(1))
                    conds.append(cond(checkKind=4, compareOp=3, compareValue=pair_level, minimumCount=1))
                else:
                    for color_name, color_id in [("Red", 0), ("Green", 1), ("Blue", 2), ("Yellow", 3), ("White", 5), ("Purple", 6)]:
                        if color_name in t:
                            pair_color = color_id
                            break
                    conds.append(cond(checkKind=4, minimumCount=1))  # MountedPilot
        elif t.startswith("When Paired"):
            timing = 15
            fm = re.search(r"\(([^)]+)\)", t)
            if fm and fm.group(1) in FEATURE_IDS:
                conds.append(cond(checkKind=4, featureId=FEATURE_IDS[fm.group(1)], minimumCount=1))
            elif "Purple" in t:
                pass  # 色条件は厳密実装困難 → 搭乗のみ
        elif t.startswith("When Linked"):
            timing = 18
        elif t == "Deploy" or t.startswith("Deploy"):
            timing = 0
        elif t == "Burst" or t.startswith("Burst"):
            timing = 5
        elif t == "Attack" or t.startswith("Attack"):
            timing = 3
        elif t == "Destroyed" or t.startswith("Destroyed"):
            timing = 9
        elif t in ("Main",) or t.startswith("Main"):
            timing = 12
        elif t in ("Action",) or t.startswith("Action"):
            timing = 8
        elif "Activate" in t and "Main" in t:
            timing = 12
            is_activate = True
        elif "Activate" in t and "Action" in t:
            timing = 8
            is_activate = True
        elif t == "Pilot" or t.startswith("Pilot"):
            # パイロット常時テキスト等 — OnPilotMounted 寄りの常時は Deploy(0) 扱いせず別途
            pass

    # During Link + Attack → OnAttack + linked（タグが揃っている場合の保険）
    if any(t == "Attack" or t.startswith("Attack") for t in tags):
        timing = 3
    if any(t.startswith("When Paired") for t in tags):
        timing = 15
    if any(t.startswith("When Linked") for t in tags):
        timing = 18

    # During Link 単独で timing 未定 → パッシブは OnPlayed(0)+linked
    if timing is None:
        if any(t.startswith("During Pair") for t in tags) and any(t == "Destroyed" or t.startswith("Destroyed") for t in tags):
            timing = 9
        elif any(t.startswith("During Pair") for t in tags) and any(t == "Attack" or t.startswith("Attack") for t in tags):
            timing = 3
        elif any(t.startswith("During Link") for t in tags):
            timing = 0
        elif any(t.startswith("During Pair") for t in tags):
            timing = 0

    return {
        "timing": timing,
        "conds": conds,
        "once": once,
        "activation_cost": activation_cost,
        "is_activate": is_activate,
        "pair_feature": pair_feature,
        "pair_level": pair_level,
        "pair_color": pair_color,
    }


def parse_stat_filter(text: str):
    """with N or less HP/AP/Lv. / Lv.N or lower 等。"""
    m = re.search(r"(\d+)\s*or less\s*HP", text, re.I)
    if m:
        return dict(targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=int(m.group(1)))
    m = re.search(r"(\d+)\s*or less\s*AP", text, re.I)
    if m:
        return dict(targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=int(m.group(1)))
    m = re.search(r"(?:Lv\.?\s*|Level\s*)(\d+)\s*or (?:less|lower)", text, re.I)
    if m:
        return dict(targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=int(m.group(1)))
    m = re.search(r"(?:Lv\.?\s*|Level\s*)(\d+)\s*or (?:more|higher|greater)", text, re.I)
    if m:
        return dict(targetUnitFilterStat=3, targetUnitStatCompareOp=0, targetUnitStatCompareValue=int(m.group(1)))
    m = re.search(r"(\d+)\s*or more\s*HP", text, re.I)
    if m:
        return dict(targetUnitFilterStat=1, targetUnitStatCompareOp=0, targetUnitStatCompareValue=int(m.group(1)))
    m = re.search(r"(\d+)\s*or more\s*AP", text, re.I)
    if m:
        return dict(targetUnitFilterStat=0, targetUnitStatCompareOp=0, targetUnitStatCompareValue=int(m.group(1)))
    return {}


def feature_in_text(text: str):
    for name, fid in FEATURE_IDS.items():
        if f"({name})" in text:
            return fid, name
    return 0, None


def parse_body_effects(body: str, meta: dict):
    """本文から Effect リストを生成（ベストエフォート）。"""
    effects = []
    body_l = body.strip()
    if not body_l:
        return effects

    filt = parse_stat_filter(body_l)
    feat_id, _ = feature_in_text(body_l)
    once = meta.get("once", False)

    # Draw N
    m = re.search(r"(?:Draw|draw)\s+(\d+)", body_l)
    if m and "discard" not in body_l.lower()[: m.start() + 10]:
        effects.append(effect(type=1, value=int(m.group(1)), target=5))

    # Deal N damage
    m = re.search(r"(?:Deal|deal)\s+(\d+)\s+damage", body_l)
    if m:
        dmg = int(m.group(1))
        target = 2
        sel = 1
        kw = dict(type=0, value=dmg, target=target, selectionMode=sel, **filt)
        if re.search(r"all\s+enemy\s+Units?", body_l, re.I):
            kw["target"] = 4
            kw["selectionMode"] = 0
        elif re.search(r"enemy\s+Player|to\s+(?:the\s+)?opponent", body_l, re.I):
            kw["target"] = 6
            kw["selectionMode"] = -1
        elif re.search(r"this\s+Unit", body_l, re.I) and "enemy" not in body_l.lower():
            kw["target"] = 0
            kw["selectionMode"] = -1
        # Lv <= this Unit
        if re.search(r"(?:Lv\.?|Level).{0,20}(?:less than or equal to|<=|or less than).{0,20}this Unit", body_l, re.I) or re.search(
            r"whose Lv\.?\s*(?:is\s*)?(?:less than or equal to|=<|≤)\s*this Unit", body_l, re.I
        ) or re.search(r"Lv\.?\s*<?=\s*this Unit", body_l, re.I) or re.search(
            r"enemy Unit whose Lv\..{0,30}this Unit", body_l, re.I
        ):
            kw["targetUnitFilterStat"] = 3
            kw["targetUnitStatCompareOp"] = 3
            kw["compareTargetStatToSource"] = 1
            kw.pop("targetUnitStatCompareValue", None)
            kw["targetUnitStatCompareValue"] = 0
        effects.append(effect(**kw))

    # Choose 1 enemy Unit ... Rest / Destroy / Bounce / return to hand
    if re.search(r"\bRest\b", body_l) and re.search(r"enemy Unit", body_l, re.I):
        tgt = 7 if re.search(r"rested enemy", body_l, re.I) else 2
        effects.append(effect(type=10, value=1, target=tgt, selectionMode=1, **filt))
    if re.search(r"\bDestroy\b", body_l) and re.search(r"enemy Unit", body_l, re.I):
        effects.append(effect(type=18, value=1, target=2, selectionMode=1, **filt))
    if re.search(r"return .{0,40}to (?:its owner's )?hand|Return .{0,40}to your hand|bounce", body_l, re.I):
        if re.search(r"enemy", body_l, re.I):
            effects.append(effect(type=9, value=1, target=2, selectionMode=1, **filt))
        elif re.search(r"this Unit|paired", body_l, re.I):
            effects.append(effect(type=9, value=1, target=0))

    # AP+/- / HP recover
    m = re.search(r"(?:gets?|gain[s]?|receive[s]?)\s*\+?(\d+)\s*AP|AP\s*\+(\d+)", body_l, re.I)
    if m:
        val = int(m.group(1) or m.group(2))
        tgt = 0
        sel = -1
        if re.search(r"Choose 1 (?:other )?friendly|Choose 1 of your|another of your", body_l, re.I):
            tgt = 8 if "other" in body_l.lower() or "another" in body_l.lower() else 1
            sel = 1
        elif re.search(r"all (?:of )?your Units|all friendly", body_l, re.I):
            tgt = 3
            sel = 0
        dur = 1 if re.search(r"during this turn|this turn", body_l, re.I) else 0
        kw = dict(type=2, value=val, target=tgt, selectionMode=sel, duration=dur, statTarget=0)
        if feat_id:
            kw["targetFeatureId"] = feat_id
        effects.append(effect(**kw))

    m = re.search(r"recovers?\s+(\d+)\s*HP|recover\s+(\d+)\s*HP", body_l, re.I)
    if m:
        val = int(m.group(1) or m.group(2))
        effects.append(effect(type=32, value=val, target=0))

    # set as active / Activate
    if re.search(r"set this Unit as active|Activate this Unit", body_l, re.I):
        effects.append(effect(type=25, value=1, target=0))

    # EX Resource
    m = re.search(r"(?:place|add|gain)\s+(\d+)\s+EX Resource", body_l, re.I)
    if m:
        effects.append(effect(type=33, value=int(m.group(1)), target=5))

    # Look at top N / top card
    m = re.search(r"[Ll]ook at (?:the )?top (\d+)", body_l)
    if not m and re.search(r"[Ll]ook at (?:the )?top card", body_l):
        class _M:
            def group(self, _n):
                return "1"

        m = _M()
    if m:
        n = int(m.group(1))
        named = {1: "LookTop1_SelfDeck", 2: "LookTop2_SelfDeck", 3: "LookTop3_SelfDeck", 4: "LookTop4_SelfDeck", 5: "LookTop5_SelfDeck"}
        if n in named:
            return [("named", named[n])]
        effects.append(effect(type=13, value=n, target=5))

    # Discard
    m = re.search(r"[Dd]iscard\s+(\d+)", body_l)
    if m and "If you do" not in body_l[:40]:
        effects.append(effect(type=24, value=int(m.group(1)), target=5, selectionMode=1))

    # Add shield / Deploy shield
    if re.search(r"add .{0,20}[Ss]hield|Deploy .{0,10}[Ss]hield", body_l):
        m = re.search(r"(\d+)\s+[Ss]hield", body_l)
        n = int(m.group(1)) if m else 1
        if re.search(r"to (?:your )?hand|AddShield", body_l, re.I) or re.search(r"add .{0,10}Shield", body_l, re.I):
            effects.append(effect(type=6, value=n, target=5))
        else:
            effects.append(effect(type=7, value=n, target=5))

    # Breach grant
    m = re.search(r"[Gg]ains?\s*<Breach\s*(\d+)>", body_l)
    if m:
        effects.append(effect(type=48, value=int(m.group(1)), target=0))

    # High-Maneuver / High Mobility grant（条件付き付与）
    if re.search(r"[Gg]ains?\s*<High-Maneuver>|gains?\s*High[- ]Maneuver", body_l, re.I):
        effects.append(effect(type=11, value=1, target=0))

    # First Strike grant
    if re.search(r"[Gg]ains?\s*<First Strike>|gains?\s*First Strike", body_l, re.I):
        effects.append(effect(type=47, value=1, target=0))

    # Rest enemy (won't activate next start ≈ Rest for now)
    if re.search(r"won't be set as active|will not be set as active", body_l, re.I):
        if re.search(r"enemy Unit", body_l, re.I):
            effects.append(effect(type=10, value=1, target=7, selectionMode=1, **filt))  # RestEnemyUnit

    # Attack active enemy
    if re.search(r"may attack active enemy|can attack active|Attack Active", body_l, re.I):
        kw = dict(type=12, value=1, target=0, **filt)
        effects.append(effect(**kw))

    # Add self to hand (burst)
    if re.search(r"add this card to (?:your )?hand", body_l, re.I):
        effects.append(effect(type=27, value=1, target=5))

    # Deploy this card as Base
    if re.search(r"Deploy this (?:card|Base)", body_l, re.I) and "Base" in body_l:
        return [("named", "DeployBase1_OnBurst")]

    return effects


def build_from_text(text: str, card_type: int):
    """効果テキスト全体から timedEffects / blocker / repair を構築。"""
    if not text or text.strip() in ("-", ""):
        return [], 0, 0

    kw = parse_keywords(text)
    blocks = []
    is_blocker = 1 if kw["blocker"] else 0
    repair_amount = kw["repair"]

    # キーワード常駐効果
    if kw["breach"] is not None:
        name = {1: "Breach1", 3: "Breach3", 5: "Breach5"}.get(kw["breach"])
        if name:
            blocks.append(timed(0, effects_name=name))
        else:
            blocks.append(timed(0, [effect(type=31, value=kw["breach"], target=0)]))
    if kw["high_maneuver"]:
        blocks.append(timed(0, effects_name="HighMobility"))
    if kw["first_strike"]:
        blocks.append(timed(0, [effect(type=47, value=1, target=0)]))
    if kw["support"] is not None:
        name = {
            1: "Support1_RestSelf_BuffAllyOtherAp1_OnMain",
            2: "Support2_RestSelf_BuffAllyOtherAp2_OnMain",
        }.get(kw["support"])
        if name:
            blocks.append(timed(12, effects_name=name))
        else:
            blocks.append(
                timed(
                    12,
                    [
                        effect(type=10, value=1, target=0),
                        effect(type=2, value=kw["support"], target=8, selectionMode=1, duration=1),
                    ],
                )
            )
    if kw["suppression"]:
        blocks.append(timed(4, effects_name="Suppress2_OnShieldAttack"))

    # Base 共通 Burst / Deployed
    if card_type == 3:
        # Burst deploy base + add shield — 個別本文で上書き可
        has_burst = "【Burst】" in text
        if has_burst and not any("DeployBase" in (b or "") for b in blocks):
            pass  # 条項側で処理

    clauses = split_clauses(text)
    for clause in clauses:
        tags = clause["tags"]
        body = clause["body"]
        # キーワードのみの条項はスキップ
        body_clean = re.sub(r"<[^>]+>", "", body).strip()
        if not body_clean and not tags:
            continue
        if not body_clean and tags and all(
            any(k in t for k in ("Blocker", "Repair", "Breach", "Support", "High-Maneuver", "First Strike", "Suppression"))
            for t in tags
        ):
            continue

        meta = timing_from_tags(tags)
        # While another friendly (Feature) Link Unit → OwnerHasLinkedUnitWithFeature
        wm = re.search(r"While another friendly \(([^)]+)\) Link Unit", body, re.I)
        if wm and wm.group(1) in FEATURE_IDS:
            meta["conds"] = meta["conds"] + [
                cond(checkKind=27, featureId=FEATURE_IDS[wm.group(1)], minimumCount=1)
            ]
        timing = meta["timing"]

        # Pilot 常時テキスト（【Pilot】[Name]）はスキップ寄り
        if tags == ["Pilot"] or (len(tags) == 1 and tags[0].startswith("Pilot")):
            continue

        # 本文がキーワード説明のみ
        if re.fullmatch(r"<[^>]+>\s*", body.strip()):
            continue

        parsed = parse_body_effects(body, meta)

        # named effect ショートカット
        if parsed and isinstance(parsed[0], tuple) and parsed[0][0] == "named":
            t = meta["timing"] if meta["timing"] is not None else 0
            blocks.append(
                timed(
                    t,
                    effects_name=parsed[0][1],
                    conds=meta["conds"] or None,
                    once_per_turn=1 if meta["once"] else 0,
                    activation_cost=meta["activation_cost"],
                )
            )
            continue

        effects_list = [e for e in parsed if isinstance(e, str)]
        timing = meta["timing"]

        # Burst: AddSelfToHand 定番
        if timing == 5 and not effects_list:
            if re.search(r"add this card to (?:your )?hand", body, re.I) or not body_clean:
                blocks.append(timed(5, effects_name="AddSelfToHand_OnBurst"))
                continue
            if re.search(r"Draw\s+1", body, re.I):
                blocks.append(timed(5, effects_name="Burst_Draw1"))
                continue
            if card_type == 3 or re.search(r"Deploy this", body, re.I):
                blocks.append(timed(5, effects_name="DeployBase1_OnBurst"))
                continue

        if timing == 5 and effects_list:
            blocks.append(
                timed(5, effects_list, conds=meta["conds"] or None, once_per_turn=1 if meta["once"] else 0)
            )
            continue

        # Base OnBaseDeployed AddShield
        if card_type == 3 and timing == 0 and re.search(r"[Ss]hield", body):
            blocks.append(timed(6, effects_name="AddShield1_OnBaseDeployed"))
            continue

        if timing is None or (
            timing == 0
            and meta["conds"]
            and re.search(
                r"when (?:one of )?your|when this Unit|destroys an enemy",
                body,
                re.I,
            )
        ):
            # パッシブ文 / During Link 中の誘発イベント
            if re.search(r"destroys an enemy shield area card", body, re.I):
                timing = 21  # OnOpponentShieldAreaCardDestroyed
            elif re.search(r"destroys an enemy Unit with battle damage", body, re.I):
                timing = 16  # OnEnemyUnitDestroyed
                if not any("checkKind: 24" in c for c in meta["conds"]):
                    meta["conds"] = meta["conds"] + [cond(checkKind=24)]
            elif timing is None and re.search(r"During your turn", body, re.I) and effects_list:
                timing = 0
                meta["conds"] = meta["conds"] + [cond(checkKind=-1, turnCheck=0)]
            elif timing is None and effects_list:
                timing = 0
            elif timing is None:
                continue

        # 効果なしの空ブロックは作らない（条件付きブロッカー等は別途）
        if not effects_list and not (
            is_blocker and timing == 11
        ):
            # Burst 定番名付きは上で処理済み
            continue

        # Main+Action 両方
        if timing == 12 and ("Main" in tags and "Action" in tags):
            b = timed(
                12,
                effects_list,
                conds=meta["conds"] or None,
                once_per_turn=1 if meta["once"] else 0,
                activation_cost=meta["activation_cost"],
            )
            blocks.extend(main_action([b]))
            continue

        blocks.append(
            timed(
                timing,
                effects_list if effects_list else None,
                conds=meta["conds"] or None,
                once_per_turn=1 if meta["once"] else 0,
                activation_cost=meta["activation_cost"],
            )
        )

    # Base デフォルト Burst/Deployed（本文に Burst があるが未生成）
    if card_type == 3:
        has_burst_block = any("timing: 5" in b for b in blocks)
        has_deployed = any("timing: 6" in b for b in blocks)
        if "【Burst】" in text and not has_burst_block:
            blocks.insert(0, timed(5, effects_name="DeployBase1_OnBurst"))
        if not has_deployed:
            blocks.append(timed(6, effects_name="AddShield1_OnBaseDeployed"))

    # Pilot デフォルト Burst AddSelfToHand
    if card_type == 1:
        has_burst = any("timing: 5" in b for b in blocks)
        if "【Burst】" in text and not has_burst:
            blocks.insert(0, timed(5, effects_name="AddSelfToHand_OnBurst"))
        elif not has_burst and "【Burst】" not in text:
            # パイロットは大抵 Burst 持ち — テキストに無くても付与しない
            pass

    return blocks, is_blocker, repair_amount


def replace_block(text, new_timed, is_blocker, is_repair=0, repair_amount=0):
    m = re.search(r"  timedEffects:.*?\n  features:", text, re.S)
    if not m:
        raise RuntimeError("timedEffects block not found")
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
    effect_texts = load_effect_texts()
    updated = 0
    empty_effects = 0
    errors = []
    samples = []
    by_set = {2: 0, 3: 0, 4: 0, 5: 0}

    for p in sorted(CARDS_DIR.glob("GD0[2-5]-*.asset")):
        text = p.read_text(encoding="utf-8")
        m = re.search(r"^  gcgOfficialId: (.+)$", text, re.M)
        if not m:
            continue
        gid = m.group(1).strip().strip('"')
        if gid not in effect_texts:
            continue
        tm = re.search(r"^  type: (\d+)$", text, re.M)
        card_type = int(tm.group(1)) if tm else 0
        try:
            blocks, is_blocker, repair_amount = build_from_text(effect_texts[gid], card_type)
            is_repair = 1 if repair_amount > 0 else 0
            new_text = replace_block(text, blocks, is_blocker, is_repair, repair_amount)
            new_text = new_text.replace("\r\n", "\n").replace("\n", "\r\n")
            p.write_bytes(new_text.encode("utf-8"))
            updated += 1
            set_num = int(gid[3])
            by_set[set_num] = by_set.get(set_num, 0) + 1
            if not blocks:
                empty_effects += 1
            elif len(samples) < 8 and any("compareTargetStatToSource: 1" in b or "checkKind: 17" in b for b in blocks):
                samples.append((gid, effect_texts[gid][:120], blocks[0][:200]))
        except Exception as exc:
            errors.append(f"{gid}: {exc}")

    print(f"updated_count={updated}")
    print(f"updated_by_set={by_set}")
    print(f"empty_timedEffects={empty_effects}")
    print(f"error_count={len(errors)}")
    if errors:
        for e in errors[:20]:
            print("  ERR", e)
    print("samples:")
    for gid, src, blk in samples:
        print("---", gid)
        print("SRC:", src.replace("\n", " / "))
        print("BLK:", blk.replace("\n", " | "))


if __name__ == "__main__":
    main()
