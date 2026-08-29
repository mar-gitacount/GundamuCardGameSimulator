# -*- coding: utf-8 -*-
"""GD01 ブースター（001-130）をスクレイプログから CardData .asset へ取り込む。"""
import json
import re
import shutil
import uuid
from pathlib import Path


ROOT = Path(r"d:\game\My project")
SCRAPER_ROOT = Path(r"d:\game\gcg-card-scraper")
LOG_PATH = SCRAPER_ROOT / "logs" / "scrape_20260825_162506.log"
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

START_CARD_ID = 1000038

SKIP_GD01 = {
    "GD01-006", "GD01-008", "GD01-015", "GD01-016", "GD01-020", "GD01-023",
    "GD01-025", "GD01-030", "GD01-049", "GD01-054", "GD01-065", "GD01-066", "GD01-068",
    "GD01-073", "GD01-086", "GD01-090", "GD01-100", "GD01-118",
}

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
    "Mobile Suit Gundam the Witch from Mercury": 18,
}

TYPE_MAP = {
    "UNIT": 0,
    "PILOT": 1,
    "COMMAND": 2,
    "BASE": 3,
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
    "Triple Ship Alliance": {"id": 8, "key": "Triple_Ship_Alliance", "display": "三隻同盟"},
    "Newtype": {"id": 19, "key": "Newtype", "display": "ニュータイプ"},
    "Academy": {"id": 23, "key": "Academy", "display": "学園"},
    "Coordinator": {"id": 32, "key": "Coordinator", "display": "コーディネイター"},
    "Warship": {"id": 30, "key": "Warship", "display": "戦艦"},
    "OZ": {"id": 36, "key": "OZ", "display": "OZ"},
    "G Team": {"id": 37, "key": "G_Team", "display": "G Team"},
    "Civilian": {"id": 38, "key": "Civilian", "display": "Civilian"},
    "Cyber-Newtype": {"id": 39, "key": "Cyber_Newtype", "display": "サイバーニュータイプ"},
    "Stronghold": {"id": 40, "key": "Stronghold", "display": "Stronghold"},
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
    "Char Aznable": {"id": 2, "key": "Char_Aznable", "display": "シャア・アズナブル"},
    "Suletta Mercury": {"id": 5, "key": "Suletta_Mercury", "display": "スレッタ・マーキュリー"},
    "Marida Cruz": {"id": 24, "key": "Marida_Cruz", "display": "マリダ・クルス"},
    "Lucrezia Noin": {"id": 25, "key": "Lucrezia_Noin", "display": "ルクレツィア・ノイン"},
    "Banagher Links": {"id": 26, "key": "Banagher_Links", "display": "バナージ・リンクス"},
    "Dozle Zabi": {"id": 27, "key": "Dozle_Zabi", "display": "ドズル・ザビ"},
    "Chang Wufei": {"id": 28, "key": "Chang_Wufei", "display": "張五飛"},
    "M'Quve": {"id": 29, "key": "M_Quve", "display": "マ・クベ"},
    "Yzak Jule": {"id": 30, "key": "Yzak_Jule", "display": "イザーク・ジュール"},
    "Dearka Elthman": {"id": 31, "key": "Dearka_Elthman", "display": "ディアッカ・エルスマン"},
    "Cagalli Yula Athha": {"id": 32, "key": "Cagalli_Yula_Athha", "display": "カガリ・ユラ・アスハ"},
    "Chuatury Panlunch": {"id": 33, "key": "Chuatury_Panlunch", "display": "チュアチュリー・パンランチ"},
    "Guel Jeturk": {"id": 34, "key": "Guel_Jeturk", "display": "グエル・ジェットーク"},
    "Elan Ceres (Enhanced Person Number 4)": {"id": 35, "key": "Elan_Ceres", "display": "エラン・ケレス"},
    "Elan Ceres": {"id": 35, "key": "Elan_Ceres", "display": "エラン・ケレス"},
    "Sayla Mass": {"id": 36, "key": "Sayla_Mass", "display": "セイラ・マス"},
    "Riddhe Marcenas": {"id": 37, "key": "Riddhe_Marcenas", "display": "リディ・マーカナス"},
    "Daguza Mackle": {"id": 38, "key": "Daguza_Mackle", "display": "ダグザ・マッコイ"},
    "Rasid Kurama": {"id": 39, "key": "Rasid_Kurama", "display": "ラシード・クラマ"},
    "Loni Garvey": {"id": 40, "key": "Loni_Garvey", "display": "ロニ・ガーベイ"},
    "Andrew Waldfeld": {"id": 41, "key": "Andrew_Waldfeld", "display": "アンドリュー・バルトフェルド"},
    "Yonem Kirks": {"id": 42, "key": "Yonem_Kirks", "display": "ヨネム・カークス"},
    "Shaddiq Zenelli": {"id": 43, "key": "Shaddiq_Zenelli", "display": "シャディク・ゼネリ"},
    "Nicol Amarfi": {"id": 12, "key": "Nicol_Amalfi", "display": "ニコル・アマルフィ"},
    "Nicol Amalfi": {"id": 12, "key": "Nicol_Amalfi", "display": "ニコル・アマルフィ"},
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


def parse_stat(raw: str) -> int:
    if not raw or raw.strip() == "-":
        return 0
    raw = raw.strip()
    plus = re.match(r"^\+(\d+)$", raw)
    if plus:
        return int(plus.group(1))
    return int(raw)


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
                "gcg": (gcg_match.group(1).strip().strip('"') if gcg_match else ""),
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
    seen_ids = set()
    for _, seed in FEATURE_SEED.items():
        if seed["id"] in seen_ids:
            continue
        seen_ids.add(seed["id"])
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
    seen_ids = set()
    for _, seed in PILOT_SEED.items():
        if seed["id"] in seen_ids:
            continue
        seen_ids.add(seed["id"])
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
    trait_names = extract_traits(text.replace(" / ", " "))
    return {"pilot_ids": [], "trait_features": trait_names}


def feature_entry(guid: str) -> str:
    return f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"


def parse_log_entries():
    log_text = LOG_PATH.read_text(encoding="utf-8")
    skip_ids = set(SKIP_GD01)
    skip_ids.update(re.findall(r"\[skip\] (GD01-\d{3})", log_text))

    entries = []
    blocks = re.split(r"={60,}", log_text)
    for block in blocks:
        m = re.search(r"^ID: (GD01-\d{3})\s*$", block, re.M)
        if not m:
            continue
        card_id = m.group(1)
        card_num = int(card_id.split("-")[1])
        if card_num < 1 or card_num > 130:
            continue
        if card_id in skip_ids:
            continue

        def field(name):
            fm = re.search(rf"^{re.escape(name)}: (.*)$", block, re.M)
            return fm.group(1).strip() if fm else ""

        eff_m = re.search(
            r"^効果:\s*\n(.*?)(?=^地形:|^特徴:|^リンク:|^AP:|^COST:|^出典タイトル:|^入手情報:|^画像:|\Z)",
            block,
            re.M | re.S,
        )
        effect = eff_m.group(1).strip() if eff_m else "-"

        entries.append(
            {
                "ID": card_id,
                "カード名": field("カード名"),
                "Lv.": field("Lv."),
                "COST": field("COST"),
                "色": field("色"),
                "タイプ": field("タイプ"),
                "効果": effect,
                "地形": field("地形"),
                "特徴": field("特徴"),
                "リンク": field("リンク"),
                "AP": field("AP"),
                "HP": field("HP"),
                "出典タイトル": field("出典タイトル"),
            }
        )
    return entries, sorted(skip_ids)


def image_leaf(card_id: str, name: str) -> str:
    # Addressables はアドレスに [ ] を許可しない
    safe = re.sub(r'[\\/:*?"<>|\[\]{}]', "", name).strip()
    safe = re.sub(r"\s+", " ", safe)
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
    insert = (
        f"  - m_GUID: {guid}\n    m_Address: {address}\n    m_ReadOnly: 0\n"
        f"    m_SerializedLabels: []\n    FlaggedDuringContentUpdateRestriction: 0\n"
    )
    text = text.replace("  m_ReadOnly: 0\n  m_Settings:", f"{insert}  m_ReadOnly: 0\n  m_Settings:")
    write_text(ADDRESSABLE_GROUP, text)


def build_card_asset(entry, next_id, feature_guids, pilot_guids):
    _, card_no = entry["ID"].split("-")
    card_num = int(card_no)
    card_type = TYPE_MAP.get(entry["タイプ"], 0)
    traits = [name for name in extract_traits(entry["特徴"]) if name in FEATURE_SEED]
    feature_lines = [
        feature_entry(feature_guids[f"Feature_{FEATURE_SEED[name]['id']}_{FEATURE_SEED[name]['key']}"])
        for name in traits
    ]

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

    image_name, image_guid = ensure_image(entry["ID"], entry["カード名"])
    append_addressable(image_guid, f"Data/Images/{image_name}")

    source_title = SOURCE_TITLES.get(entry["出典タイトル"])
    if source_title is None:
        raise KeyError(f"未知の出典タイトル: {entry['出典タイトル']} ({entry['ID']})")

    battle_zone = BATTLE_ZONE_MAP.get(entry["地形"], 0) if card_type == 0 else 0

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
  cost: {parse_stat(entry["COST"])}
  level: {parse_stat(entry["Lv."])}
  power: {parse_stat(entry["AP"])}
  hp: {parse_stat(entry["HP"])}
  imageName: {{fileID: 0}}
  image: {{fileID: 0}}
  imageAddress: {yaml_quote(f"Data/Images/{image_name}")}
  version: 1
  sourceType: 1
  productLine: 1
  boosterSet: 1
  starterSet: 0
  eternalBoosterSet: 0
  sourceTitle: {source_title}
  filterType: 0
  color: {COLOR_MAP[entry["色"]]}
  type: {card_type}
  battleZones: {battle_zone}
  attackFlg: 0
"""
    text += "  timedEffects: []\n"
    if feature_lines:
        text += "  features:\n" + "\n".join(feature_lines) + "\n"
    else:
        text += "  features: []\n"
    text += "  pilotIds: []\n"
    if link_lines:
        text += "  link:\n" + "\n".join(link_lines) + "\n"
    else:
        text += "  link: []\n"
    text += f"""  pilotMountOnPilotMountedSource: 0
  pilotMountOnPilotMountedOrder: 1
  isBlocker: 0
  isDeployTurnAttack: 0
  isNotDirectAttack: 0
  isShieldToken: 0
  isRepair: 0
  repairAmount: 0
  notUsedOnline: 0
  cannotMountPilot: 0
  gcgId:
    setKind: 2
    setNumber: 1
    cardNumber: {card_num}
"""
    return text


def main():
    sync_feature_master()
    sync_pilot_master()
    feature_guids = load_guid_map(FEATURE_DIR)
    pilot_guids = load_guid_map(PILOT_DIR)

    entries, skipped = parse_log_entries()
    existing = parse_card_assets()
    existing_gcg = {item["gcg"] for item in existing if item["gcg"]}
    next_id = max((item["id"] for item in existing), default=START_CARD_ID - 1) + 1
    if next_id < START_CARD_ID:
        next_id = START_CARD_ID

    created_ids = []
    created_gcgs = []
    errors = []

    for entry in entries:
        gcg = entry["ID"]
        if gcg in existing_gcg:
            continue
        file_name = f"{entry['ID']} {entry['カード名']}.asset"
        asset_path = CARDS_DIR / file_name
        if asset_path.exists():
            continue
        try:
            write_text(asset_path, build_card_asset(entry, next_id, feature_guids, pilot_guids))
            write_meta(asset_path.with_suffix(".asset.meta"), new_guid(), "NativeFormatImporter")
            created_ids.append(next_id)
            created_gcgs.append(gcg)
            existing_gcg.add(gcg)
            next_id += 1
        except Exception as exc:
            errors.append(f"{gcg}: {exc}")

    print(f"created_count={len(created_ids)}")
    print(f"created_ids={created_ids}")
    print(f"created_gcg={created_gcgs}")
    print(f"skipped={skipped}")
    if errors:
        print("errors:")
        for err in errors:
            print(f"  {err}")


if __name__ == "__main__":
    main()
