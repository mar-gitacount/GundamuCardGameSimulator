# -*- coding: utf-8 -*-
"""ST カードの imageAddress 修正と効果の再適用。"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(r"d:\game\My project")
TOOLS = ROOT / "tools"
CARDS = ROOT / "Assets" / "Resources" / "Data" / "Cards"
LOG = Path(r"d:\game\gcg-card-scraper\logs\scrape_20260914_163532.log")

sys.path.insert(0, str(TOOLS))
import rewrite_gd02_05_effects as rewriter
import import_scraped_st_20260914 as stimp


def load_effects():
    log_text = LOG.read_text(encoding="utf-8")
    result = {}
    blocks = re.split(r"(?=^ID: ST\d{2}-\d{3}$)", log_text, flags=re.M)
    for block in blocks:
        m = re.match(r"ID: (ST(?:0[1-9]|1[0-4])-\d{3})\s*$", block, re.M)
        if not m:
            continue
        if re.search(r"^結果: NG", block, re.M):
            continue
        em = re.search(r"^効果:\s*\n(.*?)(?=^地形:)", block, re.M | re.S)
        result[m.group(1)] = (em.group(1).strip() if em else "-")
    return result


def patch_parser():
    rewriter.FEATURE_IDS.update(
        {
            "G Generation": 71,
            "Attack": 72,
            "Durability": 73,
            "Support": 74,
            "Innovade": 75,
            "Marine": 76,
            "Long-Range Weapon": 77,
            "Academy": 23,
            "Stronghold": 40,
            "Civilian": 38,
            "GN Drive": 26,
            "Trinity": 62,
            "Super Soldier": 60,
            "Phantom Pain": 54,
            "Militia": 24,
            "Tekkadan": 28,
            "Alaya-Vijnana": 45,
            "Jupitris": 49,
            "Old UNE": 53,
            "Preventer": 55,
            "SRA": 57,
            "MF": 12,
            "Shuffle Alliance": 14,
            "Mafty": 35,
            "Operation Meteor": 9,
            "Triple Ship Alliance": 8,
            "Minerva Squad": 31,
            "Coordinator": 32,
        }
    )


def fix_image_address(text: str) -> str:
    m = re.search(r'^  imageAddress: "(.*)"\s*$', text, re.M)
    if not m:
        return text
    raw = m.group(1)
    # JSON unicode エスケープを実文字へ
    try:
        decoded = json.loads(f'"{raw}"')
    except Exception:
        decoded = raw.encode("utf-8").decode("unicode_escape") if "\\u" in raw else raw
    new = json.dumps(decoded, ensure_ascii=False)
    return re.sub(r'^  imageAddress: ".*"\s*$', f"  imageAddress: {new}", text, count=1, flags=re.M)


def main():
    patch_parser()
    effects = load_effects()
    fixed_img = 0
    updated = 0
    still_empty = []
    dash_empty = []

    for p in sorted(CARDS.glob("ST*.asset")):
        text = p.read_text(encoding="utf-8")
        g = re.search(r"^  gcgOfficialId: (.+)$", text, re.M)
        if not g:
            continue
        gcg = g.group(1).strip().strip('"')
        if not re.match(r"ST(0[1-9]|1[0-4])-\d{3}$", gcg):
            continue

        new_text = fix_image_address(text)
        if new_text != text:
            fixed_img += 1
            text = new_text

        eff = effects.get(gcg)
        if eff is None:
            # ログに無い既存カードはそのまま
            if text != p.read_text(encoding="utf-8"):
                stimp.write_text(p, text)
            continue

        # 効果が "-" のみなら空のままで可
        if eff.strip() in ("-", ""):
            if "timedEffects: []" in text:
                dash_empty.append(gcg)
            if text != p.read_text(encoding="utf-8"):
                stimp.write_text(p, text)
            continue

        tm = re.search(r"^  type: (\d+)$", text, re.M)
        card_type = int(tm.group(1)) if tm else 0
        blocks, is_blocker, repair_amount = rewriter.build_from_text(eff, card_type)
        is_repair = 1 if repair_amount else 0
        text = rewriter.replace_block(text, blocks, is_blocker, is_repair, repair_amount)
        if re.search(r"can attack during the turn it is deployed|\[Deployed Unit\]", eff, re.I):
            text = re.sub(r"  isDeployTurnAttack: \d+", "  isDeployTurnAttack: 1", text, count=1)
        stimp.write_text(p, text)
        updated += 1
        if not blocks:
            still_empty.append(gcg)

    print(f"fixed_imageAddress={fixed_img}")
    print(f"effects_rewritten={updated}")
    print(f"still_empty_with_text={len(still_empty)} {still_empty[:30]}")
    print(f"dash_effect_empty={len(dash_empty)}")


if __name__ == "__main__":
    main()
