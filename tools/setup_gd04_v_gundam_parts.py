# -*- coding: utf-8 -*-
"""T-021 Parts token + GD04-003/006/011/081 card data and scraper images."""
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
LM_FEATURE_GUID = "b2c3d4e5f60718293a4b5c6d7e8f9012"
VICTORY_TYPE_GUID = "c3d4e5f60718293a4b5c6d7e8f9012a3"
USO_PILOT_GUID = "805cabaeb17b472ea8f18786a0c86f5e"
NEWTYPE_GUID = "f708192a3b4c5d6e7f8012a3b4c5d6e7"

PARTS_TOKEN_ID = 1000543
VDASH_ID = 1000544

ACTIVATION_COND_TAIL = """      levelAggregate: 0
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
      cardNameContains: """

EFFECT_TAIL = """      compareTargetStatToSource: 0
      compareTargetStatToPriorChainPicked: 0
      abortRemainingChainOnSkip: {abort}
      requireChainObservationContext: 0
      effectActivationConditions: []
      filterTargetUnitLevel: 0
      filterByTargetCardType: 0
      targetCardType: 0
      deployUnitSource: 0
      deployCardId: {deploy_card_id}
      filterByDeployCardId: 0
      filterDeployCandidateByFeature: 0
      deployUnitTriggerOnPlayed: 0
      deployUnitAsRested: {deploy_rested}
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
      choicePromptEn: """


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


def effect_block(
    effect_type: int,
    value: int,
    target: int,
    selection_mode: int = -1,
    target_feature_id: int = 0,
    target_unit_filter_stat: int = -1,
    target_unit_stat_compare_op: int = 3,
    target_unit_stat_compare_value: int = 0,
    abort: int = 0,
    deploy_card_id: int = 0,
    deploy_rested: int = 0,
) -> str:
    return f"""    - type: {effect_type}
      value: {value}
      target: {target}
      selectionMode: {selection_mode}
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
      targetFeatureId: {target_feature_id}
      targetFeatures: []
      targetFeatureIds: 
      targetUnitFilterStat: {target_unit_filter_stat}
      targetUnitStatCompareOp: {target_unit_stat_compare_op}
      targetUnitStatCompareValue: {target_unit_stat_compare_value}
{EFFECT_TAIL.format(abort=abort, deploy_card_id=deploy_card_id, deploy_rested=deploy_rested)}"""


def activation_cond(board_side: int, check_kind: int, feature_id: int = 0, minimum_count: int = 1) -> str:
    return f"""    - boardSide: {board_side}
      checkKind: {check_kind}
      turnCheck: -1
      feature: {{fileID: 0}}
      featureId: {feature_id}
      features: []
      featureIds: 
      minimumCount: {minimum_count}
{ACTIVATION_COND_TAIL}"""


def timed_block(
    timing: int,
    effects: str,
    activation_conditions: str = "",
    once_per_turn: int = 0,
    effects_name: str = "",
) -> str:
    ac = activation_conditions if activation_conditions else "    activationConditions: []"
    if activation_conditions and not activation_conditions.startswith("    activationConditions:"):
        ac = "    activationConditions:\n" + activation_conditions
    if effects:
        effects_yaml = f"    effects:\n{effects}"
    else:
        effects_yaml = "    effects: []"
    return f"""  - timing: {timing}
{ac}
    requireChainObservationContext: 0
    effectsName: {effects_name}
{effects_yaml}
    activationCost: 0
    oncePerTurn: {once_per_turn}
    observedUnitTriggerKind: -1
"""


def ensure_image(scraper_name: str, image_leaf: str) -> str:
    src = SCRAPER_IMAGES / scraper_name
    if not src.exists():
        raise FileNotFoundError(src)
    dst = IMAGES_DIR / f"{image_leaf}.png"
    meta = dst.with_suffix(".png.meta")
    shutil.copyfile(src, dst)
    if not meta.exists():
        template = TEXTURE_META_TEMPLATE.read_text(encoding="utf-8")
        text = re.sub(r"^guid: .*$", f"guid: {new_guid()}", template, flags=re.M)
        write_crlf(meta, text)
    guid = re.search(r"^guid: ([0-9a-f]+)$", meta.read_text(encoding="utf-8"), re.M).group(1)
    address = f"Data/Images/{image_leaf}"
    append_addressable(guid, address)
    print("image", image_leaf, "from", scraper_name)
    return address


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


def replace_timed_effects(text: str, timed_yaml: str) -> str:
    m = re.search(r"  timedEffects:.*?\n  features:", text, re.S)
    if not m:
        raise RuntimeError("timedEffects block not found")
    return text[: m.start()] + f"  timedEffects:\n{timed_yaml}  features:" + text[m.end() :]


def create_parts_token() -> int:
    asset_name = "T-021 Parts"
    asset_path = CARDS_DIR / f"{asset_name}.asset"
    if asset_path.exists():
        print("token exists", asset_path.name)
        return PARTS_TOKEN_ID

    address = ensure_image("T-021.png", "T-021_Parts")
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
  id: {PARTS_TOKEN_ID}
  gcgOfficialId: T-021
  cardName: Parts
  cost: 0
  level: 0
  power: 1
  hp: 1
  imageName: {{fileID: 0}}
  image: {{fileID: 0}}
  imageAddress: "{address}"
  version: 4
  sourceType: 2
  productLine: 2
  boosterSet: 4
  starterSet: 0
  eternalBoosterSet: 0
  sourceTitle: 5
  filterType: 0
  color: 5
  type: 5
  battleZones: 0
  attackFlg: 0
  timedEffects: []
  features:
  - {{fileID: 11400000, guid: {LM_FEATURE_GUID}, type: 2}}
  pilotIds: []
  link: []
  pilotMountOnPilotMountedSource: 0
  pilotMountOnPilotMountedOrder: 0
  isBlocker: 0
  isDeployTurnAttack: 0
  isNotDirectAttack: 1
  isShieldToken: 0
  isRepair: 0
  repairAmount: 0
  notUsedOnline: 0
  cannotMountPilot: 0
  gcgId:
    setKind: 4
    setNumber: 0
    cardNumber: 21
""",
    )
    write_native_meta(asset_path.with_suffix(".asset.meta"), asset_guid)
    print("created", asset_path.name)
    return PARTS_TOKEN_ID


def fix_gd04_003():
    path = CARDS_DIR / "GD04-003 Victory Gundam.asset"
    text = path.read_text(encoding="utf-8")
    ensure_image("GD04-003.png", "GD04-003_Victory Gundam")
    timed = timed_block(
        3,
        effect_block(1, 1, 5),
        activation_conditions=activation_cond(0, 0, feature_id=16, minimum_count=3),
    )
    text = replace_timed_effects(text, timed)
    write_crlf(path, text)
    print("updated", path.name)


def fix_gd04_011(token_id: int):
    path = CARDS_DIR / "GD04-011 Victory Gundam.asset"
    text = path.read_text(encoding="utf-8")
    ensure_image("GD04-011.png", "GD04-011_Victory Gundam")
    timed = timed_block(
        9,
        effect_block(22, 1, 5, deploy_card_id=token_id, deploy_rested=0),
        activation_conditions=activation_cond(0, 0, feature_id=16, minimum_count=2),
    )
    text = replace_timed_effects(text, timed)
    write_crlf(path, text)
    print("updated", path.name)


def fix_gd04_081(token_id: int):
    path = CARDS_DIR / "GD04-081 Üso Ewin.asset"
    text = path.read_text(encoding="utf-8")
    ensure_image("GD04-081.png", "GD04-081_Üso Ewin")
    burst = timed_block(
        5,
        effect_block(27, 1, 5),
    )
    paired = timed_block(
        15,
        effect_block(22, 1, 5, deploy_card_id=token_id, deploy_rested=0),
        activation_conditions=activation_cond(-1, 23, feature_id=16, minimum_count=1),
    )
    text = replace_timed_effects(text, burst + paired)
    write_crlf(path, text)
    print("updated", path.name)


def create_gd04_006():
    path = CARDS_DIR / "GD04-006 V-Dash Gundam.asset"
    if path.exists():
        print("exists", path.name)
        return
    address = ensure_image("GD04-006.png", "GD04-006_V-Dash Gundam")
    breach = timed_block(0, "", effects_name="Breach3")
    on_main = timed_block(
        12,
        effect_block(10, 1, 5, selection_mode=1, target_feature_id=16, abort=1)
        + effect_block(
            10,
            1,
            2,
            selection_mode=1,
            target_unit_filter_stat=1,
            target_unit_stat_compare_op=3,
            target_unit_stat_compare_value=4,
        ),
        once_per_turn=1,
    )
    asset_guid = new_guid()
    write_crlf(
        path,
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
  m_Name: GD04-006 V-Dash Gundam
  m_EditorClassIdentifier: 
  id: {VDASH_ID}
  gcgOfficialId: GD04-006
  cardName: "V-Dash Gundam"
  cost: 4
  level: 6
  power: 4
  hp: 5
  imageName: {{fileID: 0}}
  image: {{fileID: 0}}
  imageAddress: "{address}"
  version: 4
  sourceType: 1
  productLine: 1
  boosterSet: 4
  starterSet: 0
  eternalBoosterSet: 0
  sourceTitle: 5
  filterType: 0
  color: 2
  type: 0
  battleZones: 3
  attackFlg: 0
  timedEffects:
{breach}{on_main}  features:
  - {{fileID: 11400000, guid: {LM_FEATURE_GUID}, type: 2}}
  - {{fileID: 11400000, guid: {VICTORY_TYPE_GUID}, type: 2}}
  pilotIds: []
  link:
  - pilotCardId: 0
    linkPilotIds:
    - {{fileID: 11400000, guid: {USO_PILOT_GUID}, type: 2}}
    linkPilotIdIds: 
    pilotFeatures: []
    pilotFeatureIds: 
  pilotMountOnPilotMountedSource: 0
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
    setNumber: 4
    cardNumber: 6
""",
    )
    write_native_meta(path.with_suffix(".asset.meta"), asset_guid)
    print("created", path.name)


def fix_aegis_gcg_id():
    path = CARDS_DIR / "36Aegis Gundam.asset"
    text = path.read_text(encoding="utf-8")
    text = re.sub(r"^  gcgOfficialId: GD04-006\s*$", "  gcgOfficialId: ", text, flags=re.M)
    text = re.sub(
        r"  gcgId:\n    setKind: 2\n    setNumber: 4\n    cardNumber: 6",
        "  gcgId:\n    setKind: 0\n    setNumber: 0\n    cardNumber: 36",
        text,
    )
    write_crlf(path, text)
    print("cleared wrong GD04-006 id on", path.name)


def main():
    token_id = create_parts_token()
    fix_gd04_003()
    create_gd04_006()
    fix_gd04_011(token_id)
    fix_gd04_081(token_id)
    fix_aegis_gcg_id()


if __name__ == "__main__":
    main()
