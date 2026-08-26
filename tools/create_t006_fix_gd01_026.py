# -*- coding: utf-8 -*-
"""Create T-006 Char's Zaku II Unit Token and fix GD01-026 Destroyed-while-paired effect."""
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

TOKEN_ID = 1000536
TOKEN_GCG = "T-006"
IMAGE_LEAF = "T-006_Char's Zaku 2"
SRC_IMAGE = SCRAPER_IMAGES / "T-006Char's Zaku2.png"
GD01_026 = CARDS_DIR / "GD01-026 Char's Zaku Ⅱ.asset"


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
        return
    insert = (
        f"  - m_GUID: {guid}\n    m_Address: {address}\n    m_ReadOnly: 0\n"
        f"    m_SerializedLabels: []\n    FlaggedDuringContentUpdateRestriction: 0\n"
    )
    text = text.replace("  m_ReadOnly: 0\n  m_Settings:", f"{insert}  m_ReadOnly: 0\n  m_Settings:")
    write_crlf(ADDRESSABLE_GROUP, text)


def create_token():
    # duplicate check
    for p in CARDS_DIR.glob("*.asset"):
        t = p.read_text(encoding="utf-8", errors="replace")
        if re.search(r"^  gcgOfficialId: T-006\s*$", t, re.M):
            print(f"skip create: already exists {p.name}")
            m = re.search(r"^  id: (\d+)$", t, re.M)
            return int(m.group(1)) if m else TOKEN_ID
        if re.search(rf"^  id: {TOKEN_ID}\s*$", t, re.M):
            raise RuntimeError(f"numeric id {TOKEN_ID} already used by {p.name}")

    image_guid = ensure_image()
    append_addressable(image_guid, f"Data/Images/{IMAGE_LEAF}")

    asset_name = "T-006 Char's Zaku Ⅱ"
    asset_path = CARDS_DIR / f"{asset_name}.asset"
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
  m_Name: {asset_name}
  m_EditorClassIdentifier: 
  id: {TOKEN_ID}
  gcgOfficialId: {TOKEN_GCG}
  cardName: "Char's Zaku \\u2161"
  cost: 0
  level: 0
  power: 3
  hp: 1
  imageName: {{fileID: 0}}
  image: {{fileID: 0}}
  imageAddress: "Data/Images/{IMAGE_LEAF}"
  version: 3
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
    cardNumber: 6
"""
    write_crlf(asset_path, text)
    write_native_meta(asset_path.with_suffix(".asset.meta"), new_guid())
    print(f"created token id={TOKEN_ID} path={asset_path.name}")
    return TOKEN_ID


def fix_gd01_026(token_id: int):
    if not GD01_026.exists():
        raise FileNotFoundError(GD01_026)
    text = GD01_026.read_text(encoding="utf-8")
    # 【During Pair】【Destroyed】Deploy 1 rested Char's Zaku II token
    # timing OnDestroyed=9, MountedPilot checkKind=4, DeployUnit type=22
    new_timed = f"""  timedEffects:
  - timing: 9
    activationConditions:
    - boardSide: -1
      checkKind: 4
      turnCheck: -1
      feature: {{fileID: 0}}
      featureId: 0
      features: []
      featureIds: 
      minimumCount: 1
      levelAggregate: 0
      compareOp: 0
      compareValue: 0
      unitCountCompareOp: 0
      unitCountThreshold: 0
      pilotCardId: 0
      trashCardId: 0
      pilotLevelThreshold: 0
      activationStatTarget: -1
      observedCardType: 0
      destroyedByOwnerRelation: 0
      cardNameContains: 
    requireChainObservationContext: 0
    effectsName: 
    effects:
    - type: 22
      value: 1
      target: 5
      selectionMode: -1
      statTarget: 0
      duration: 0
      valueMode: 0
      valueCountBoardSide: 0
      valueCountKind: 0
      valueCountFeature: {{fileID: 0}}
      valueCountFeatureId: 0
      valueCountMinUnitLevel: 0
      valueScaleMaximum: 0
      shieldTokenCardId: 0
      valueCountExcludeSource: 0
      targetFeature: {{fileID: 0}}
      targetFeatureId: 0
      targetFeatures: []
      targetFeatureIds: 
      targetUnitFilterStat: -1
      targetUnitStatCompareOp: 3
      targetUnitStatCompareValue: 0
      compareTargetStatToSource: 0
      compareTargetStatToPriorChainPicked: 0
      abortRemainingChainOnSkip: 0
      requireChainObservationContext: 0
      effectActivationConditions: []
      filterTargetUnitLevel: 0
      filterByTargetCardType: 0
      targetCardType: 0
      deployUnitSource: 0
      deployCardId: {token_id}
      filterByDeployCardId: 0
      filterDeployCandidateByFeature: 0
      deployUnitTriggerOnPlayed: 0
      deployUnitAsRested: 1
      deployUnitPayCost: 0
      deployUnitOverrideAp: 0
      deployUnitOverrideHp: 0
      grantAttackFlagOnlyIfOff: 1
      revealDiscardedToOpponent: 0
      forbidSkipHandDiscard: 0
      revealDrawnToPlayer: 0
      filterTargetIsBlocker: 0
      selectMinCount: 0
      selectMaxCount: 0
      observedUnitTriggerKind: -1
      autoSelectLowestUnitStat: 0
      autoSelectHighestUnitStat: 0
      relaxTargetUnitStatFilterWhenTrashHasSourceCopies: 0
      trashRelaxFilterMinCopies: 2
      requireExactExileCount: 0
      targetCardNameContains: 
      targetCardNameExcludes: 
      targetPilotId: 0
      requireTargetLacksBreach: 0
      requireTargetHasNoPilot: 0
      resolveAfterDealtBattleDamage: 0
      optionalPlayerConfirm: 0
      opponentChoosesTarget: 0
      choiceBranches: []
      choicePromptJa: 
      choicePromptEn: 
    activationCost: 0
    oncePerTurn: 0
    observedUnitTriggerKind: -1
"""
    m = re.search(r"  timedEffects:.*?\n  features:", text, re.S)
    if not m:
        raise RuntimeError("timedEffects block not found on GD01-026")
    text = text[: m.start()] + new_timed + "  features:" + text[m.end() :]
    write_crlf(GD01_026, text)
    print(f"fixed GD01-026 -> DeployUnit token id={token_id} rested on Destroyed while Paired")


def main():
    token_id = create_token()
    fix_gd01_026(token_id)


if __name__ == "__main__":
    main()
