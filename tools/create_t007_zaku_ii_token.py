# -*- coding: utf-8 -*-
"""Create T-007 Zaku II Unit Token and register Addressables image."""
import re
import shutil
import uuid
from pathlib import Path

ROOT = Path(r"d:\game\My project")
SCRAPER_IMAGES = Path(r"D:\game\gcg-card-scraper\images")
CARDS_DIR = ROOT / "Assets" / "Resources" / "Data" / "Cards"
IMAGES_DIR = ROOT / "Assets" / "Resources_moved" / "Data" / "Images"
ADDRESSABLE_GROUP = ROOT / "Assets" / "AddressableAssetsData" / "AssetGroups" / "Default Local Group.asset"
TEXTURE_META_TEMPLATE = IMAGES_DIR / "63_Tallgeese.png.meta"

CARDDATA_SCRIPT_GUID = "fb320e6489da1ee42a0daff0b0f579e0"
ZEON_FEATURE_GUID = "e0793995189d51249b8e6616badc53d3"

TOKEN_ID = 1000537
TOKEN_GCG = "T-007"
IMAGE_LEAF = "T-007_Zaku 2"
SRC_IMAGE = SCRAPER_IMAGES / "T-007.png"


def new_guid() -> str:
    return uuid.uuid4().hex


def write_crlf(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    normalized = text.replace("\r\n", "\n").replace("\n", "\r\n")
    path.write_bytes(normalized.encode("utf-8"))


def write_native_meta(path: Path, guid: str):
    write_crlf(
        path,
        f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""",
    )


def ensure_image() -> str:
    if not SRC_IMAGE.exists():
        raise FileNotFoundError(SRC_IMAGE)
    dst = IMAGES_DIR / f"{IMAGE_LEAF}.png"
    meta = dst.with_suffix(".png.meta")
    if not dst.exists():
        shutil.copyfile(SRC_IMAGE, dst)
    if not meta.exists():
        template = TEXTURE_META_TEMPLATE.read_text(encoding="utf-8")
        text = re.sub(r"^guid: .*$", f"guid: {new_guid()}", template, flags=re.M)
        write_crlf(meta, text)
    return re.search(r"^guid: ([0-9a-f]+)$", meta.read_text(encoding="utf-8"), re.M).group(1)


def append_addressable(guid: str, address: str):
    text = ADDRESSABLE_GROUP.read_text(encoding="utf-8")
    if guid in text or f"m_Address: {address}" in text:
        print("addressable already present")
        return
    insert = (
        f"  - m_GUID: {guid}\n    m_Address: {address}\n    m_ReadOnly: 0\n"
        f"    m_SerializedLabels: []\n    FlaggedDuringContentUpdateRestriction: 0\n"
    )
    text = text.replace("  m_ReadOnly: 0\n  m_Settings:", f"{insert}  m_ReadOnly: 0\n  m_Settings:")
    write_crlf(ADDRESSABLE_GROUP, text)
    print("addressable added", address)


def create_token() -> int:
    for p in CARDS_DIR.glob("*.asset"):
        t = p.read_text(encoding="utf-8", errors="replace")
        if re.search(r"^  gcgOfficialId: T-007\s*$", t, re.M):
            print(f"skip create: already exists {p.name}")
            m = re.search(r"^  id: (\d+)$", t, re.M)
            return int(m.group(1)) if m else TOKEN_ID
        if re.search(rf"^  id: {TOKEN_ID}\s*$", t, re.M):
            raise RuntimeError(f"numeric id {TOKEN_ID} already used by {p.name}")

    image_guid = ensure_image()
    address = f"Data/Images/{IMAGE_LEAF}"
    append_addressable(image_guid, address)

    asset_name = "T-007 Zaku Ⅱ"
    asset_path = CARDS_DIR / f"{asset_name}.asset"
    asset_guid = new_guid()
    write_crlf(
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
  m_Script: {{fileID: 11500000, guid: {CARDDATA_SCRIPT_GUID}, type: 3}}
  m_Name: {asset_name}
  m_EditorClassIdentifier: 
  id: {TOKEN_ID}
  gcgOfficialId: {TOKEN_GCG}
  cardName: "Zaku \u2161"
  cost: 0
  level: 0
  power: 1
  hp: 1
  imageName: {{fileID: 0}}
  image: {{fileID: 0}}
  imageAddress: "{address}"
  version: 1
  sourceType: 2
  productLine: 2
  boosterSet: 0
  starterSet: 3
  eternalBoosterSet: 0
  sourceTitle: 1
  filterType: 0
  color: 4
  type: 5
  battleZones: 0
  attackFlg: 0
  timedEffects: []
  features:
  - {{fileID: 11400000, guid: {ZEON_FEATURE_GUID}, type: 2}}
  pilotIds: []
  link: []
  pilotMountOnPilotMountedSource: 0
  pilotMountOnPilotMountedOrder: 0
  isBlocker: 0
  isDeployTurnAttack: 0
  isNotDirectAttack: 0
  isShieldToken: 0
  isRepair: 0
  repairAmount: 0
  notUsedOnline: 0
  cannotMountPilot: 0
  gcgId:
    setKind: 4
    setNumber: 0
    cardNumber: 7
""",
    )
    write_native_meta(asset_path.with_suffix(".asset.meta"), asset_guid)
    print("created", asset_path.name, "id", TOKEN_ID, "guid", asset_guid)
    return TOKEN_ID


if __name__ == "__main__":
    create_token()
