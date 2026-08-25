import json
import re
import shutil
import uuid
from pathlib import Path


ROOT = Path(r"d:\game\My project")
SCRAPER_ROOT = Path(r"d:\game\gcg-card-scraper")
PARSED_PATH = SCRAPER_ROOT / "parsed_entries.json"
CARDS_DIR = ROOT / "Assets" / "Resources" / "Data" / "Cards"
IMAGES_DIR = ROOT / "Assets" / "Resources_moved" / "Data" / "Images"
PILOT_DIR = ROOT / "Assets" / "Resources" / "Data" / "PilotIds"
FEATURE_DIR = ROOT / "Assets" / "Resources" / "Data" / "Features"
PILOT_MASTER = ROOT / "Assets" / "Resources" / "Data" / "Json" / "pilot_master.json"
FEATURE_MASTER = ROOT / "Assets" / "Resources" / "Data" / "Json" / "feature_master.json"
ADDRESSABLE_GROUP = ROOT / "Assets" / "AddressableAssetsData" / "AssetGroups" / "Default Local Group.asset"

CARDDATA_SCRIPT_GUID = "fb320e6489da1ee42a0daff0b0f579e0"
PILOT_SCRIPT_GUID = "a7c4e91f2b8d4e0a9f3c5d6e7a1b2c3d"
FEATURE_SCRIPT_GUID = "c515c583aa780ff4591d3a073bd30f74"
TEXTURE_META_TEMPLATE = IMAGES_DIR / "67_Guncannon.png.meta"

SOURCE_TITLES = {
    "Mobile Suit Gundam": 1,
    "Mobile Suit Gundam Wing": 7,
    "Mobile Suit Gundam Unicorn": 14,
    "Mobile Suit Gundam SEED": 11,
    "Mobile Suit Gundam IRON-BLOODED ORPHANS": 16,
    "Mobile Suit Gundam GQuuuuuuX": 19,
    "Mobile Suit Gundam 00": 13,
    "Mobile Suit Gundam: Hathaway's Flash": 17,
    "Mobile Suit Gundam SEED DESTINY": 12,
}

STARTER_SET = {
    "ST01": 1,
    "ST02": 2,
    "ST03": 3,
    "ST04": 4,
    "ST05": 5,
    "ST06": 6,
    "ST07": 7,
    "ST08": 8,
    "ST09": 9,
}

COLOR_MAP = {
    "Red": 0,
    "Green": 1,
    "Blue": 2,
    "Yellow": 3,
    "Colorless": 4,
    "White": 5,
    "Purple": 6,
}

BATTLE_ZONE_MAP = {
    "Space": 1,
    "Earth": 2,
    "Space Earth": 3,
}

FEATURE_SEED = {
    "Earth Federation": {"id": 2, "key": "Earth_Federation", "display": "地球連邦"},
    "Earth Alliance": {"id": 4, "key": "Earth_Alliance", "display": "地球連合"},
    "Neo Zeon": {"id": 6, "key": "Neo_Zeon", "display": "ネオジオン"},
    "Operation Meteor": {"id": 9, "key": "Operation_Meteor", "display": "オペレーション・メテオ"},
    "Zaft": {"id": 3, "key": "Zaft", "display": "ザフト"},
    "ZAFT": {"id": 3, "key": "Zaft", "display": "ザフト"},
    "Orb": {"id": 18, "key": "Orb", "display": "オーブ"},
    "Clan": {"id": 7, "key": "Clan", "display": "クラン"},
    "CB": {"id": 25, "key": "CB", "display": "CB"},
    "GN Drive": {"id": 26, "key": "GN_Drive", "display": "GN Drive"},
    "Zeon": {"id": 5, "key": "Zeon", "display": "ジオン"},
    "Tekkadan": {"id": 28, "key": "Tekkadan", "display": "鉄華団"},
    "Gundam Frame": {"id": 29, "key": "Gundam_Frame", "display": "ガンダム・フレーム"},
    "Minerva Squad": {"id": 31, "key": "Minerva_Squad", "display": "ミネルバ隊"},
    "White Base Team": {"id": 33, "key": "White_Base_Team", "display": "ホワイトベース隊"},
    "Maganac Corps": {"id": 34, "key": "Maganac_Corps", "display": "マグアナック隊"},
    "Mafty": {"id": 35, "key": "Mafty", "display": "マフティー"},
}

PILOT_SEED = {
    "Amuro Ray": {"id": 1, "key": "Amuro_Ray", "display": "アムロ・レイ"},
    "Kira Yamato": {"id": 3, "key": "Kira_Yamato", "display": "キラ・ヤマト"},
    "Athrun Zala": {"id": 10, "key": "Athrun_Zala", "display": "アスラン・ザラ"},
    "Mu La Flaga": {"id": 6, "key": "Mu_La_Flaga", "display": "ムウ・ラ・フラガ"},
    "Mikazuki Augus": {"id": 9, "key": "Mikazuki_Augus", "display": "三日月・オーガス"},
    "Akihiro Altland": {"id": 8, "key": "Akihiro_Altland", "display": "昭弘・アルトランド"},
    "Setsuna F. Seiei": {"id": 7, "key": "Setsuna_F_Seiei", "display": "刹那・F・セイエイ"},
    "Shinn Asuka": {"id": 4, "key": "Shin_Asuka", "display": "シン・アスカ"},
    "Kai Shiden": {"id": 13, "key": "Kai_Shiden", "display": "カイ・シデン"},
    "Hayato Kobayashi": {"id": 14, "key": "Hayato_Kobayashi", "display": "ハヤト・コバヤシ"},
    "Heero Yuy": {"id": 15, "key": "Heero_Yuy", "display": "ヒイロ・ユイ"},
    "Trowa Barton": {"id": 16, "key": "Trowa_Barton", "display": "トロワ・バートン"},
    "Quatre Raberba Winner": {"id": 17, "key": "Quatre_Raberba_Winner", "display": "カトル・ラバーバ・ウィナー"},
    "Amate Yuzuriha (Machu)": {"id": 18, "key": "Amate_Yuzuriha_Machu", "display": "アマテ・ユズリハ（マチュ）"},
    "Gaia": {"id": 19, "key": "Gaia", "display": "ガイア"},
    "Shuji Itō": {"id": 20, "key": "Shuji_Ito", "display": "シュウジ・イトウ"},
    "Tieria Erde": {"id": 21, "key": "Tieria_Erde", "display": "ティエリア・アーデ"},
    "Lockon Stratos": {"id": 22, "key": "Lockon_Stratos", "display": "ロックオン・ストラトス"},
    "Hathaway Noa": {"id": 23, "key": "Hathaway_Noa", "display": "ハサウェイ・ノア"},
}


def new_guid() -> str:
    return uuid.uuid4().hex


def yaml_quote(text: str) -> str:
    if text is None:
        return ""
    return json.dumps(text, ensure_ascii=True)


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_text(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    # Unity YAML は Windows では CRLF。LF のみだと Parser Failure になる。
    normalized = text.replace("\r\n", "\n").replace("\n", "\r\n")
    path.write_bytes(normalized.encode("utf-8"))


def write_meta(path: Path, guid: str, importer: str):
    if importer == "NativeFormatImporter":
        text = f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
        write_text(path, text)
        return

    template = TEXTURE_META_TEMPLATE.read_text(encoding="utf-8")
    text = re.sub(r"^guid: .*$", f"guid: {guid}", template, flags=re.M)
    write_text(path, text)


def parse_card_assets():
    results = []
    for path in CARDS_DIR.glob("*.asset"):
        text = path.read_text(encoding="utf-8", errors="replace")
        id_match = re.search(r"^  id: (\d+)$", text, re.M)
        gcg_match = re.search(r"^  gcgOfficialId: ?(.*)$", text, re.M)
        name_match = re.search(r"^  cardName: ?(?:\"([^\"]+)\"|(.*))$", text, re.M)
        if not id_match:
            continue
        results.append(
            {
                "path": path,
                "id": int(id_match.group(1)),
                "gcg": (gcg_match.group(1).strip() if gcg_match else ""),
                "name": ((name_match.group(1) or name_match.group(2) or "").strip() if name_match else ""),
            }
        )
    return results


def load_guid_map(directory: Path):
    mapping = {}
    for meta in directory.glob("*.asset.meta"):
        guid_match = re.search(r"^guid: ([0-9a-f]+)$", meta.read_text(encoding="utf-8"), re.M)
        if guid_match:
            key = meta.name.replace(".asset.meta", "")
            mapping[key] = guid_match.group(1)
    return mapping


def ensure_master_asset(asset_dir: Path, name: str, seed: dict, script_guid: str):
    asset_path = asset_dir / f"{name}.asset"
    meta_path = asset_path.with_suffix(".asset.meta")
    if not asset_path.exists():
        write_text(
            asset_path,
            f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}
  m_Name: {name}
  m_EditorClassIdentifier: 
  id: {seed["id"]}
  {"featureKey" if asset_dir == FEATURE_DIR else "pilotKey"}: {seed["key"]}
  displayName: {yaml_quote(seed["display"])}
  description: 
""",
        )
        write_meta(meta_path, new_guid(), "NativeFormatImporter")


def sync_feature_master():
    data = read_json(FEATURE_MASTER)
    existing = {item["id"]: item for item in data["features"]}
    for feature_name, seed in FEATURE_SEED.items():
        if seed["id"] not in existing:
            data["features"].append(
                {
                    "id": seed["id"],
                    "featureKey": seed["key"],
                    "displayName": seed["display"],
                    "description": "",
                }
            )
        ensure_master_asset(FEATURE_DIR, f"Feature_{seed['id']}_{seed['key']}", seed, FEATURE_SCRIPT_GUID)

    data["features"] = sorted(data["features"], key=lambda x: x["id"])
    write_text(FEATURE_MASTER, json.dumps(data, ensure_ascii=False, indent=4) + "\n")


def sync_pilot_master():
    data = read_json(PILOT_MASTER)
    existing = {item["id"]: item for item in data["pilots"]}
    for _, seed in PILOT_SEED.items():
        if seed["id"] not in existing:
            data["pilots"].append(
                {
                    "id": seed["id"],
                    "pilotKey": seed["key"],
                    "displayName": seed["display"],
                    "description": "",
                }
            )
        ensure_master_asset(PILOT_DIR, f"PilotId_{seed['id']}_{seed['key']}", seed, PILOT_SCRIPT_GUID)

    data["pilots"] = sorted(data["pilots"], key=lambda x: x["id"])
    write_text(PILOT_MASTER, json.dumps(data, ensure_ascii=False, indent=4) + "\n")


def extract_traits(text: str):
    return re.findall(r"\(([^)]+)\)", text or "")


def parse_link(text: str):
    if not text or text == "-":
        return {"pilot_ids": [], "trait_features": []}
    if text.startswith("[") and text.endswith("]"):
        pilot_name = text[1:-1]
        return {"pilot_ids": [pilot_name], "trait_features": []}
    trait_names = extract_traits(text)
    return {"pilot_ids": [], "trait_features": trait_names}


def feature_entry(guid: str) -> str:
    return f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"


def effect(type_id: int, value: int = 0, target: int = 0, selection_mode: int = -1, duration: int = 0, **extra):
    payload = {
        "type": type_id,
        "value": value,
        "target": target,
        "selectionMode": selection_mode,
        "statTarget": 0,
        "duration": duration,
    }
    payload.update(extra)
    return payload


def cond(check_kind: int, board_side: int = -1, **extra):
    payload = {
        "boardSide": board_side,
        "checkKind": check_kind,
        "turnCheck": -1,
        "featureId": 0,
        "minimumCount": 1,
        "compareOp": 0,
        "compareValue": 0,
        "unitCountCompareOp": 0,
        "unitCountThreshold": 0,
        "pilotCardId": 0,
        "trashCardId": 0,
        "pilotLevelThreshold": 0,
        "activationStatTarget": -1,
        "observedCardType": 0,
        "destroyedByOwnerRelation": 0,
        "cardNameContains": "",
    }
    payload.update(extra)
    return payload


def timed(timing: int, effects=None, effects_name="", activation_conditions=None, activation_cost=0, once_per_turn=0):
    return {
        "timing": timing,
        "activationConditions": activation_conditions or [],
        "requireChainObservationContext": 0,
        "effectsName": effects_name,
        "effects": effects or [],
        "activationCost": activation_cost,
        "oncePerTurn": once_per_turn,
        "observedUnitTriggerKind": -1,
    }


def effects_for_card(entry):
    card_id = entry["ID"]
    txt = entry["効果"]
    blocks = []
    if txt == "-":
        return blocks, False, False

    is_blocker = "<Blocker>" in txt
    is_deploy_turn_attack = False

    if card_id == "ST01-002":
        blocks.append(timed(18, [effect(1, 1, 5)]))
    elif card_id == "ST01-004":
        blocks.append(timed(0, [effect(10, 1, 2, 1, 0, targetUnitFilterStat=1, targetUnitStatCompareOp=3, targetUnitStatCompareValue=2)]))
    elif card_id == "ST02-001":
        blocks.append(timed(0, effects_name="Breach5"))
        blocks.append(timed(0, [effect(12, 0, 0, -1, 0, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=4)]))
    elif card_id == "ST02-002":
        blocks.append(timed(0, [effect(33, 1, 5)]))
    elif card_id == "ST02-003":
        blocks.append(timed(16, [effect(0, 1, 4, 0, 0, targetUnitFilterStat=3, targetUnitStatCompareOp=3, targetUnitStatCompareValue=3)], activation_conditions=[cond(13), cond(24)]))
    elif card_id == "ST03-004":
        blocks.append(timed(12, [effect(2, 2, 8, 1, 1)], activation_cost=1))
    elif card_id == "ST04-002":
        blocks.append(timed(0, [effect(1, 1, 5), effect(24, 1, 5, 1)]))
    elif card_id == "ST05-001":
        blocks.append(timed(0, [effect(0, 1, 8, 1), effect(2, 1, 8, 1, 1)]))
        blocks.append(timed(11, [effect(5, 2, 6)], activation_conditions=[cond(11)]))
    elif card_id == "ST05-002":
        blocks.append(timed(3, [effect(2, 2, 0, -1, 0)], activation_conditions=[cond(11)]))
    elif card_id == "ST05-003":
        blocks.append(timed(12, [effect(0, 1, 1, 1), effect(2, 1, 1, 1, 1)], activation_cost=1))
    elif card_id == "ST05-005":
        blocks.append(timed(9, [effect(10, 1, 2, 1, 0, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=4)]))
    elif card_id == "ST06-001":
        blocks.append(timed(18, [effect(41, 1, 0, -1, 1)], activation_conditions=[cond(0, 0, featureId=7, minimumCount=2)]))
    elif card_id == "ST06-002":
        blocks.append(timed(0, [effect(0, 1, 2, 1)], activation_conditions=[cond(0, 0, featureId=7, minimumCount=2)]))
    elif card_id == "ST06-003":
        blocks.append(timed(12, [effect(2, 1, 8, 1, 1)], activation_cost=1))
    elif card_id == "ST06-005":
        blocks.append(timed(0, [effect(31, 1)]))
        blocks.append(timed(3, [effect(2, 2, 3, 1, 1)],))
    elif card_id == "ST07-001":
        blocks.append(timed(2, [effect(40, 1, 5)], activation_conditions=[cond(12, 4, featureId=25, minimumCount=7)]))
        blocks.append(timed(18, [effect(19, 2, 5)],))
    elif card_id == "ST07-004":
        is_blocker = True
        blocks.append(timed(11, [], activation_conditions=[cond(0, 0, featureId=25, minimumCount=1)]))
    elif card_id == "ST07-005":
        blocks.append(timed(16, [effect(32, 2, 0)], activation_conditions=[cond(24)]))
        blocks.append(timed(18, [effect(2, 2, 0, -1, 0)]))
    elif card_id == "ST08-001":
        blocks.append(timed(18, [effect(0, 3, 2, 1)], activation_conditions=[]))
    elif card_id == "ST08-002":
        blocks.append(timed(0, [effect(0, 1, 2, 1)]))
    elif card_id == "ST08-004":
        blocks.append(timed(3, [effect(0, 1, 2, 1)], activation_conditions=[]))
    elif card_id == "ST09-001":
        blocks.append(timed(12, effects_name="ReturnSelfToDeckBottom_ThenDeployTrashImpulseGundamLv4Plus_OnMain", activation_cost=2))
    elif card_id == "ST09-003":
        blocks.append(timed(0, effects_name="Breach3"))
        blocks.append(timed(18, [effect(0, 2, 4, 0, 0, targetUnitFilterStat=0, targetUnitStatCompareOp=3, targetUnitStatCompareValue=5)], activation_conditions=[cond(12, 4, minimumCount=5, featureId=0)]))
    elif card_id == "ST09-004":
        is_blocker = True
        blocks.append(timed(11, [effect(5, 2, 6)]))

    return blocks, is_blocker, is_deploy_turn_attack


def render_activation_condition(c):
    lines = [
        f"    - boardSide: {c['boardSide']}",
        f"      checkKind: {c['checkKind']}",
        f"      turnCheck: {c['turnCheck']}",
        "      feature: {fileID: 0}",
        f"      featureId: {c.get('featureId', 0)}",
        "      features: []",
        "      featureIds: ",
        f"      minimumCount: {c.get('minimumCount', 1)}",
        "      levelAggregate: 0",
        f"      compareOp: {c.get('compareOp', 0)}",
        f"      compareValue: {c.get('compareValue', 0)}",
        f"      unitCountCompareOp: {c.get('unitCountCompareOp', 0)}",
        f"      unitCountThreshold: {c.get('unitCountThreshold', 0)}",
        f"      pilotCardId: {c.get('pilotCardId', 0)}",
        f"      trashCardId: {c.get('trashCardId', 0)}",
        f"      pilotLevelThreshold: {c.get('pilotLevelThreshold', 0)}",
        f"      activationStatTarget: {c.get('activationStatTarget', -1)}",
        f"      observedCardType: {c.get('observedCardType', 0)}",
        f"      destroyedByOwnerRelation: {c.get('destroyedByOwnerRelation', 0)}",
        f"      cardNameContains: {c.get('cardNameContains', '')}",
    ]
    return "\n".join(lines)


def render_effect(e):
    lines = [
        f"    - type: {e['type']}",
        f"      value: {e.get('value', 0)}",
        f"      target: {e.get('target', 0)}",
        f"      selectionMode: {e.get('selectionMode', -1)}",
        "      statTarget: 0",
        f"      duration: {e.get('duration', 0)}",
    ]
    for key in [
        "targetUnitFilterStat",
        "targetUnitStatCompareOp",
        "targetUnitStatCompareValue",
        "compareTargetStatToSource",
        "requireTargetHasNoPilot",
        "resolveAfterDealtBattleDamage",
    ]:
        if key in e:
            lines.append(f"      {key}: {e[key]}")
    return "\n".join(lines)


def render_timed_block(block):
    lines = [f"  - timing: {block['timing']}"]
    if block["activationConditions"]:
        lines.append("    activationConditions:")
        lines.extend(render_activation_condition(c) for c in block["activationConditions"])
    else:
        lines.append("    activationConditions: []")
    lines.append("    requireChainObservationContext: 0")
    lines.append(f"    effectsName: {block['effectsName']}")
    if block["effects"]:
        lines.append("    effects:")
        lines.extend(render_effect(e) for e in block["effects"])
    else:
        lines.append("    effects: []")
    lines.append(f"    activationCost: {block['activationCost']}")
    lines.append(f"    oncePerTurn: {block['oncePerTurn']}")
    lines.append("    observedUnitTriggerKind: -1")
    return "\n".join(lines)


def image_leaf(card_id: str, name: str) -> str:
    safe = re.sub(r"[\\\\/:*?\"<>|]", "", name).strip()
    return f"{card_id}_{safe}"


def ensure_image(card_id: str, name: str):
    src = SCRAPER_ROOT / "images" / f"{card_id}.png"
    leaf = image_leaf(card_id, name)
    dst = IMAGES_DIR / f"{leaf}.png"
    if not dst.exists() and src.exists():
        shutil.copyfile(src, dst)
    meta = dst.with_suffix(".png.meta")
    if not meta.exists():
        write_meta(meta, new_guid(), "TextureImporter")
    guid = re.search(r"^guid: ([0-9a-f]+)$", meta.read_text(encoding="utf-8"), re.M).group(1)
    return leaf, guid


def append_addressable(guid: str, address: str):
    text = ADDRESSABLE_GROUP.read_text(encoding="utf-8")
    if guid in text or f"m_Address: {address}" in text:
        return
    insert = f"  - m_GUID: {guid}\n    m_Address: {address}\n    m_ReadOnly: 0\n    m_SerializedLabels: []\n    FlaggedDuringContentUpdateRestriction: 0\n"
    text = text.replace("  m_ReadOnly: 0\n  m_Settings:", f"{insert}  m_ReadOnly: 0\n  m_Settings:")
    write_text(ADDRESSABLE_GROUP, text)


def build_card_asset(entry, next_id, feature_guids, pilot_guids):
    set_code, card_no = entry["ID"].split("-")
    set_num = int(set_code[2:])
    card_num = int(card_no)
    traits = [name for name in extract_traits(entry["特徴"]) if name in FEATURE_SEED]
    if entry["タイプ"] == "UNIT" and "Mobile Suit" in FEATURE_SEED and "Mobile Suit" not in traits:
        pass
    feature_lines = [feature_entry(feature_guids[f"Feature_{FEATURE_SEED[name]['id']}_{FEATURE_SEED[name]['key']}"]) for name in traits]
    link_info = parse_link(entry["リンク"])
    link_lines = []
    if link_info["pilot_ids"] or link_info["trait_features"]:
        link_lines.append("  - pilotCardId: 0")
        if link_info["pilot_ids"]:
            link_lines.append("    linkPilotIds:")
            for pilot_name in link_info["pilot_ids"]:
                seed = PILOT_SEED.get(pilot_name)
                if seed:
                    guid = pilot_guids[f"PilotId_{seed['id']}_{seed['key']}"]
                    link_lines.append(f"    - {{fileID: 11400000, guid: {guid}, type: 2}}")
            link_lines.append("    linkPilotIdIds: ")
            link_lines.append("    pilotFeatures: []")
            link_lines.append("    pilotFeatureIds: ")
        else:
            link_lines.append("    linkPilotIds: []")
            link_lines.append("    linkPilotIdIds: ")
            link_lines.append("    pilotFeatures:")
            for feature_name in link_info["trait_features"]:
                seed = FEATURE_SEED.get(feature_name)
                if seed:
                    guid = feature_guids[f"Feature_{seed['id']}_{seed['key']}"]
                    link_lines.append(f"    - {{fileID: 11400000, guid: {guid}, type: 2}}")
            link_lines.append("    pilotFeatureIds: ")
    else:
        link_lines = []

    timed_blocks, is_blocker, is_deploy_turn_attack = effects_for_card(entry)
    image_name, image_guid = ensure_image(entry["ID"], entry["カード名"])
    append_addressable(image_guid, f"Data/Images/{image_name}")

    text = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {CARDDATA_SCRIPT_GUID}, type: 3}}
  m_Name: {entry["ID"]} {entry["カード名"]}
  m_EditorClassIdentifier: 
  id: {next_id}
  gcgOfficialId: {entry["ID"]}
  cardName: {yaml_quote(entry["カード名"])}
  cost: {0 if entry["COST"] == "-" else int(entry["COST"])}
  level: {0 if entry["Lv."] == "-" else int(entry["Lv."])}
  power: {0 if entry["AP"] == "-" else int(entry["AP"])}
  hp: {0 if entry["HP"] == "-" else int(entry["HP"])}
  imageName: {{fileID: 0}}
  image: {{fileID: 0}}
  imageAddress: {yaml_quote(f"Data/Images/{image_name}")}
  version: {STARTER_SET[set_code]}
  sourceType: 2
  productLine: 2
  boosterSet: 0
  starterSet: {STARTER_SET[set_code]}
  eternalBoosterSet: 0
  sourceTitle: {SOURCE_TITLES[entry["出典タイトル"]]}
  filterType: 0
  color: {COLOR_MAP[entry["色"]]}
  type: 0
  battleZones: {BATTLE_ZONE_MAP.get(entry["地形"], 0)}
  attackFlg: 0
"""
    if timed_blocks:
        text += "  timedEffects:\n"
        text += "\n".join(render_timed_block(block) for block in timed_blocks) + "\n"
    else:
        text += "  timedEffects: []\n"
    if feature_lines:
        text += "  features:\n"
        text += "\n".join(feature_lines) + "\n"
    else:
        text += "  features: []\n"
    text += "  pilotIds: []\n"
    if link_lines:
        text += "  link:\n" + "\n".join(link_lines) + "\n"
    else:
        text += "  link: []\n"
    text += f"""  pilotMountOnPilotMountedSource: 0
  pilotMountOnPilotMountedOrder: 1
  isBlocker: {1 if is_blocker else 0}
  isDeployTurnAttack: {1 if is_deploy_turn_attack else 0}
  isNotDirectAttack: 0
  isShieldToken: 0
  isRepair: 0
  repairAmount: 0
  notUsedOnline: 0
  cannotMountPilot: 0
  gcgId:
    setKind: 1
    setNumber: {set_num}
    cardNumber: {card_num}
"""
    return text


def main():
    sync_feature_master()
    sync_pilot_master()
    feature_guids = load_guid_map(FEATURE_DIR)
    pilot_guids = load_guid_map(PILOT_DIR)
    entries = read_json(PARSED_PATH)
    existing = parse_card_assets()
    existing_gcg = {item["gcg"] for item in existing if item["gcg"]}
    next_id = max(item["id"] for item in existing) + 1

    created = 0
    for entry in entries:
        if entry["ID"] in existing_gcg:
            continue
        file_name = f"{entry['ID']} {entry['カード名']}.asset"
        asset_path = CARDS_DIR / file_name
        meta_path = asset_path.with_suffix(".asset.meta")
        if asset_path.exists():
            continue
        write_text(asset_path, build_card_asset(entry, next_id, feature_guids, pilot_guids))
        write_meta(meta_path, new_guid(), "NativeFormatImporter")
        created += 1
        next_id += 1

    print(f"created_cards={created}")


if __name__ == "__main__":
    main()
