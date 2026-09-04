using System;
using UnityEngine;

/// <summary>
/// P2P で送るオンライン対戦アクションの payload。
/// </summary>
[Serializable]
public class OnlineBattleActionPayload
{
    public string action;
    public int cardId;
    public int instanceId;
    public int requestId;
    public int attackerInstanceId;
    public int defenderInstanceId;
    public int blockerInstanceId;
    public int attackerHp;
    public int defenderHp;
    public int defenderShieldAfter;
    public int defenderExBaseAfter;
    public bool directAttackWin;
    public bool blockCombat;
    /// <summary>
    /// UnitAttack: true のとき受信側で攻撃宣言レスト／攻撃権消費を行わない（エフェクトバトル等）。
    /// </summary>
    public bool skipAttackDeclarationRest;
    /// <summary>
    /// ShieldAttack / UnitAttack: 防御領域スナップショット付きなら true（効果ダメージ・突破の領域同期）。
    /// JsonUtility が 0 を省略しても識別できるよう明示フラグを使う。
    /// </summary>
    public bool includeDefenderAreaSnapshot;
    public bool shieldBreakSimultaneousReveal;
    public int[] brokenShieldCardIds;
    public string attackKind;
    public string onActionContext;
    public int actingZoneSide;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
    /// <summary>HandDeckState: 手札枚数。</summary>
    public int handCount = -1;
    /// <summary>HandDeckState / ZoneSync: 山札残数。</summary>
    public int deckRemainCount = -1;
    /// <summary>DeployUnit / MountPilot 等: 支払後リソースを同梱したとき true。</summary>
    public bool includeResourceSnapshot;
    /// <summary>DeployBase 同期：配備ベースの現在 HP。</summary>
    public int baseHpAfter;
    /// <summary>
    /// ShieldAttack / 防御領域同期：配備ベースの現在 HP。
    /// -1=変更なし、0=破壊済み、1 以上=現在 HP。
    /// </summary>
    public int defenderDeployedBaseHpAfter;
    /// <summary>DeployBase / DeployShield 同期：シールドゾーンのカード ID 列（先頭＝外側）。</summary>
    public int[] shieldZoneCardIds;
    /// <summary>OnActionCommandUsed 用。効果対象ユニットの cardId。-1 は未指定。</summary>
    public int targetCardId = -1;
    /// <summary>OnActionCommandUsed 用。使用時点のコスト／レベル（UI 表示用）。</summary>
    public int cardCost;
    public int cardLevel;
    /// <summary>OnAction パス種別。0=Pass（Confirm/Cancel）、1=ActionEnd。</summary>
    public int actionStepPassKind;
    /// <summary>送信側視点で Player ゾーンが ActionEnd 済み（1/0）。受信側はミラーして適用。</summary>
    public int sessionPlayerActionEnded;
    /// <summary>送信側視点で Enemy ゾーンが ActionEnd 済み（1/0）。受信側はミラーして適用。</summary>
    public int sessionEnemyActionEnded;
    /// <summary>ターン終了アクションステップが両者完了したら 1。受信側は EndTurn 欠落でも次ターン開始。</summary>
    public int turnEndCommit;
    /// <summary>アクションステップセッション ID（0=レガシー／非セッション）。</summary>
    public int actionStepSessionId;
    /// <summary>DeployUnit 同期：送信側視点の配備先バトルゾーン（0=Player, 1=Enemy）。未指定時は Player。</summary>
    public int deployTargetZoneOwnerSide;
    /// <summary>
    /// DeployUnit 専用の拡張（null なら JsonUtility が省略 → EOS ~1170B 超過を防ぐ）。
    /// </summary>
    public OnlineDeployUnitExtras deployUnitExtras;
    /// <summary>
    /// ShieldBreakComplete 専用。バーストで配備したユニット（null なら省略）。
    /// </summary>
    public OnlineBurstDeployedUnitsSnapshot burstDeployedUnits;

    public const string DeployUnit = "DeployUnit";
    public const string DeployBase = "DeployBase";
    public const string DeployShield = "DeployShield";
    public const string ShieldAttack = "ShieldAttack";
    public const string UnitAttack = "UnitAttack";
    public const string AttackDeclare = "AttackDeclare";
    public const string BlockResponse = "BlockResponse";
    public const string MountPilot = "MountPilot";
    public const string OnActionBegin = "OnActionBegin";
    public const string OnActionEnd = "OnActionEnd";
    public const string OnActionCommandUsed = "OnActionCommandUsed";
    public const string CommandPlayRevealComplete = "CommandPlayRevealComplete";
    public const string ShieldBreakComplete = "ShieldBreakComplete";
    public const string AttackKindShield = "Shield";
    public const string AttackKindUnitVsUnit = "UnitVsUnit";

    public const string HandDiscardReveal = "HandDiscardReveal";
    public const string HandDiscardRevealComplete = "HandDiscardRevealComplete";
    public const string DiscardThinkWait = "DiscardThinkWait";
    public const string DiscardThinkComplete = "DiscardThinkComplete";
    public const string ResourceState = "ResourceState";
    public const string HandDeckState = "HandDeckState";
    public const string EndTurn = "EndTurn";
    public const string EndTurnAck = "EndTurnAck";

    public static string CreateHandDiscardReveal(int cardId, int requestId)
    {
        return JsonUtility.ToJson(new OnlineHandDiscardRevealDto
        {
            action = HandDiscardReveal,
            cardId = cardId,
            requestId = requestId
        });
    }

    public static string CreateHandDiscardRevealComplete(int requestId)
    {
        return JsonUtility.ToJson(new OnlineHandDiscardRevealCompleteDto
        {
            action = HandDiscardRevealComplete,
            requestId = requestId
        });
    }

    public static string CreateDiscardThinkWait(int requestId)
    {
        return JsonUtility.ToJson(new OnlineDiscardThinkDto
        {
            action = DiscardThinkWait,
            requestId = requestId
        });
    }

    public static string CreateDiscardThinkComplete(int requestId)
    {
        return JsonUtility.ToJson(new OnlineDiscardThinkDto
        {
            action = DiscardThinkComplete,
            requestId = requestId
        });
    }

    public static string CreateCommandPlayRevealComplete(int requestId)
    {
        return JsonUtility.ToJson(new OnlineCommandPlayRevealCompleteDto
        {
            action = CommandPlayRevealComplete,
            requestId = requestId
        });
    }

    public static string CreateDeployUnit(
        int cardId,
        int instanceId,
        int deployTargetZoneOwnerSide = 0,
        bool allowOffTurnDeploy = false,
        int deployOverrideAp = 0,
        int deployOverrideHp = 0,
        bool deployForceUnitType = false,
        int deployPrintedType = -1,
        bool deployAsRested = false,
        bool includeResourceSnapshot = false,
        int resourceAfter = 0,
        int exResourceAfter = 0,
        int levelAfter = 0,
        int requestId = 0)
    {
        // extras は必要なときだけ付ける（通常配備のパケット肥大化を防ぐ）
        OnlineDeployUnitExtras extras = null;
        if (allowOffTurnDeploy
            || deployOverrideAp > 0
            || deployOverrideHp > 0
            || deployForceUnitType
            || deployPrintedType >= 0
            || deployAsRested)
        {
            extras = new OnlineDeployUnitExtras
            {
                allowOffTurnDeploy = allowOffTurnDeploy,
                deployOverrideAp = deployOverrideAp,
                deployOverrideHp = deployOverrideHp,
                deployForceUnitType = deployForceUnitType,
                deployPrintedType = deployPrintedType,
                deployAsRested = deployAsRested
            };
        }

        // OnlineBattleActionPayload 全文はフィールド過多で EOS ~1170B を超えやすいため lean DTO のみ送る。
        // 受信側は JsonUtility.FromJson<OnlineBattleActionPayload> で不足フィールドが default になり解釈できる。
        // 支払後リソースは同梱する（別 ResourceState メッセージに頼ると届かない／遅れやすい）。
        return JsonUtility.ToJson(new OnlineDeployUnitDto
        {
            action = DeployUnit,
            cardId = cardId,
            instanceId = instanceId,
            deployTargetZoneOwnerSide = deployTargetZoneOwnerSide,
            deployUnitExtras = extras,
            includeResourceSnapshot = includeResourceSnapshot,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter,
            requestId = requestId
        });
    }

    public static string CreateDeployBase(
        int cardId,
        int baseHpAfter,
        int exBaseAfter,
        int shieldCountAfter,
        int[] shieldZoneCardIds,
        bool includeResourceSnapshot = false,
        int resourceAfter = 0,
        int exResourceAfter = 0,
        int levelAfter = 0)
    {
        return JsonUtility.ToJson(new OnlineDeployBaseDto
        {
            action = DeployBase,
            cardId = cardId,
            baseHpAfter = baseHpAfter,
            defenderExBaseAfter = exBaseAfter,
            defenderShieldAfter = shieldCountAfter,
            shieldZoneCardIds = shieldZoneCardIds ?? Array.Empty<int>(),
            includeResourceSnapshot = includeResourceSnapshot,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter
        });
    }

    public static string CreateDeployShield(
        int cardId,
        int shieldCountAfter,
        int[] shieldZoneCardIds,
        bool includeResourceSnapshot = false,
        int resourceAfter = 0,
        int exResourceAfter = 0,
        int levelAfter = 0)
    {
        return JsonUtility.ToJson(new OnlineDeployShieldDto
        {
            action = DeployShield,
            cardId = cardId,
            defenderShieldAfter = shieldCountAfter,
            shieldZoneCardIds = shieldZoneCardIds ?? Array.Empty<int>(),
            includeResourceSnapshot = includeResourceSnapshot,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter
        });
    }

    public static string CreateAttackDeclare(
        int requestId,
        string attackKind,
        int attackerInstanceId,
        int defenderInstanceId)
    {
        return JsonUtility.ToJson(new OnlineAttackDeclareDto
        {
            action = AttackDeclare,
            requestId = requestId,
            attackKind = attackKind ?? string.Empty,
            attackerInstanceId = attackerInstanceId,
            defenderInstanceId = defenderInstanceId
        });
    }

    public static string CreateBlockResponse(int requestId, int blockerInstanceId)
    {
        return JsonUtility.ToJson(new OnlineBlockResponseDto
        {
            action = BlockResponse,
            requestId = requestId,
            blockerInstanceId = blockerInstanceId
        });
    }

    public static string CreateShieldAttack(
        int attackerInstanceId,
        int defenderShieldAfter,
        int defenderExBaseAfter,
        bool directAttackWin,
        int[] brokenShieldCardIds = null,
        int shieldBreakRequestId = 0,
        bool shieldBreakSimultaneousReveal = false,
        int defenderDeployedBaseHpAfter = -1,
        bool includeDefenderAreaSnapshot = false)
    {
        bool areaSync = includeDefenderAreaSnapshot
            || attackerInstanceId <= 0
            || defenderDeployedBaseHpAfter >= 0;
        // lean DTO（OnlineBattleActionPayload 全体だと EOS ~1170B 超過し得る）
        return JsonUtility.ToJson(new OnlineShieldAttackDto
        {
            action = ShieldAttack,
            attackerInstanceId = attackerInstanceId,
            defenderShieldAfter = defenderShieldAfter,
            defenderExBaseAfter = defenderExBaseAfter,
            directAttackWin = directAttackWin,
            brokenShieldCardIds = brokenShieldCardIds,
            requestId = shieldBreakRequestId,
            shieldBreakSimultaneousReveal = shieldBreakSimultaneousReveal,
            defenderDeployedBaseHpAfter = defenderDeployedBaseHpAfter,
            includeDefenderAreaSnapshot = areaSync
        });
    }

    public static string CreateShieldBreakComplete(
        int requestId,
        int defenderShieldAfter = -1,
        int defenderExBaseAfter = -1,
        int defenderDeployedBaseHpAfter = -1,
        int deployedBaseCardId = 0,
        int[] shieldZoneCardIds = null,
        int[] brokenShieldCardIds = null,
        int[] burstDeployedUnitCardIds = null,
        int[] burstDeployedUnitInstanceIds = null,
        int[] burstDeployedUnitAp = null,
        int[] burstDeployedUnitHp = null,
        int[] burstDeployedUnitPrintedType = null)
    {
        OnlineBurstDeployedUnitsSnapshot burstUnits = null;
        if (burstDeployedUnitCardIds != null && burstDeployedUnitCardIds.Length > 0)
        {
            burstUnits = new OnlineBurstDeployedUnitsSnapshot
            {
                cardIds = burstDeployedUnitCardIds,
                instanceIds = burstDeployedUnitInstanceIds,
                ap = burstDeployedUnitAp,
                hp = burstDeployedUnitHp,
                printedType = burstDeployedUnitPrintedType
            };
        }

        return JsonUtility.ToJson(new OnlineShieldBreakCompleteDto
        {
            action = ShieldBreakComplete,
            requestId = requestId,
            defenderShieldAfter = defenderShieldAfter,
            defenderExBaseAfter = defenderExBaseAfter,
            defenderDeployedBaseHpAfter = defenderDeployedBaseHpAfter,
            cardId = deployedBaseCardId,
            shieldZoneCardIds = shieldZoneCardIds,
            brokenShieldCardIds = brokenShieldCardIds,
            burstDeployedUnits = burstUnits
        });
    }

    public static string CreateMountPilot(
        int hostInstanceId,
        int pilotCardId,
        bool includeResourceSnapshot = false,
        int resourceAfter = 0,
        int exResourceAfter = 0,
        int levelAfter = 0,
        int requestId = 0)
    {
        // OnlineBattleActionPayload 全文は EOS ~1170B を超えやすいため lean DTO のみ送る
        return JsonUtility.ToJson(new OnlineMountPilotDto
        {
            action = MountPilot,
            instanceId = hostInstanceId,
            cardId = pilotCardId,
            includeResourceSnapshot = includeResourceSnapshot,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter,
            requestId = requestId
        });
    }

    public static string CreateUnitAttack(
        int attackerInstanceId,
        int defenderInstanceId,
        int attackerHp,
        int defenderHp,
        bool blockCombat = false,
        bool skipAttackDeclarationRest = false,
        bool includeDefenderAreaSnapshot = false,
        int defenderShieldAfter = -1,
        int defenderExBaseAfter = -1,
        int defenderDeployedBaseHpAfter = -1,
        int defenderOnDestroyedRequestId = 0)
    {
        // requestId を防御側 OnDestroyed 待機 ID に流用
        return JsonUtility.ToJson(new OnlineUnitAttackDto
        {
            action = UnitAttack,
            attackerInstanceId = attackerInstanceId,
            defenderInstanceId = defenderInstanceId,
            attackerHp = attackerHp,
            defenderHp = defenderHp,
            blockCombat = blockCombat,
            skipAttackDeclarationRest = skipAttackDeclarationRest,
            includeDefenderAreaSnapshot = includeDefenderAreaSnapshot,
            defenderShieldAfter = defenderShieldAfter,
            defenderExBaseAfter = defenderExBaseAfter,
            defenderDeployedBaseHpAfter = defenderDeployedBaseHpAfter,
            requestId = defenderOnDestroyedRequestId
        });
    }

    public static string CreateOnActionBegin(
        int requestId,
        int actingZoneSide,
        string context,
        int attackerInstanceId,
        int actionStepSessionId = 0)
    {
        // OnlineBattleActionPayload 全体を載せると EOS ~1170B を超え得るため lean DTO のみ送る
        return JsonUtility.ToJson(new OnlineOnActionBeginDto
        {
            action = OnActionBegin,
            requestId = requestId,
            actingZoneSide = actingZoneSide,
            onActionContext = context ?? string.Empty,
            attackerInstanceId = attackerInstanceId,
            actionStepSessionId = actionStepSessionId
        });
    }

    public static string CreateOnActionEnd(
        int requestId,
        int actingZoneSide,
        int actionStepPassKind,
        int sessionPlayerActionEnded,
        int sessionEnemyActionEnded,
        int resourceAfter,
        int exResourceAfter,
        int levelAfter,
        int turnEndCommit = 0)
    {
        return JsonUtility.ToJson(new OnlineOnActionEndDto
        {
            action = OnActionEnd,
            requestId = requestId,
            actingZoneSide = actingZoneSide,
            actionStepPassKind = actionStepPassKind,
            sessionPlayerActionEnded = sessionPlayerActionEnded,
            sessionEnemyActionEnded = sessionEnemyActionEnded,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter,
            turnEndCommit = turnEndCommit
        });
    }

    public static string CreateOnActionCommandUsed(
        int cardId,
        int actingZoneSide,
        string context,
        int cardCost,
        int cardLevel,
        int targetCardId = -1,
        bool includeResourceSnapshot = false,
        int resourceAfter = 0,
        int exResourceAfter = 0,
        int levelAfter = 0,
        int requestId = 0)
    {
        return JsonUtility.ToJson(new OnlineOnActionCommandUsedDto
        {
            action = OnActionCommandUsed,
            cardId = cardId,
            actingZoneSide = actingZoneSide,
            onActionContext = context ?? string.Empty,
            cardCost = cardCost,
            cardLevel = cardLevel,
            targetCardId = targetCardId,
            includeResourceSnapshot = includeResourceSnapshot,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter,
            requestId = requestId
        });
    }

    /// <summary>EX／リソース変動のスナップショット同期（送信側視点のゾーン側）。</summary>
    public static string CreateResourceState(
        int actingZoneSide,
        int resourceAfter,
        int exResourceAfter,
        int levelAfter)
    {
        return JsonUtility.ToJson(new OnlineResourceStateDto
        {
            action = ResourceState,
            actingZoneSide = actingZoneSide,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter
        });
    }

    /// <summary>手札枚数／山札残数のスナップショット同期（送信側視点のゾーン側）。カード ID は送らない。</summary>
    public static string CreateHandDeckState(
        int actingZoneSide,
        int handCount,
        int deckRemainCount)
    {
        return JsonUtility.ToJson(new OnlineHandDeckStateDto
        {
            action = HandDeckState,
            actingZoneSide = actingZoneSide,
            handCount = handCount,
            deckRemainCount = deckRemainCount
        });
    }

    public static string CreateEndTurn(int requestId)
    {
        return JsonUtility.ToJson(new OnlineEndTurnDto
        {
            action = EndTurn,
            requestId = requestId
        });
    }

    public static string CreateEndTurnAck(int requestId)
    {
        return JsonUtility.ToJson(new OnlineEndTurnAckDto
        {
            action = EndTurnAck,
            requestId = requestId
        });
    }

    public static bool TryParse(string raw, out OnlineBattleActionPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            payload = JsonUtility.FromJson<OnlineBattleActionPayload>(raw);
            if (payload == null || string.IsNullOrWhiteSpace(payload.action))
            {
                return false;
            }

            switch (payload.action)
            {
                case ShieldAttack:
                    // attackerInstanceId==0 は効果ダメージ／突破の防御領域同期
                    return payload.attackerInstanceId > 0
                        || payload.directAttackWin
                        || payload.includeDefenderAreaSnapshot
                        || payload.defenderDeployedBaseHpAfter >= 0;
                case UnitAttack:
                case AttackDeclare:
                    return payload.attackerInstanceId > 0;
                case BlockResponse:
                case OnActionBegin:
                case OnActionEnd:
                case ShieldBreakComplete:
                    return payload.requestId > 0;
                case OnActionCommandUsed:
                    return payload.cardId > 0;
                case CommandPlayRevealComplete:
                case HandDiscardRevealComplete:
                case DiscardThinkWait:
                case DiscardThinkComplete:
                case EndTurn:
                case EndTurnAck:
                    return payload.requestId > 0;
                case HandDiscardReveal:
                    return payload.cardId > 0 && payload.requestId > 0;
                case ResourceState:
                    // actingZoneSide=0（Player）も有効。旧判定は意図どおり通るが明示してブレを防ぐ
                    return true;
                case HandDeckState:
                    // hand=0 / deck=0 も有効。欠落フィールドは受信側でクランプする
                    return true;
                case DeployUnit:
                    return payload.cardId > 0 && payload.instanceId > 0;
                case DeployBase:
                    // cardId>0: 通常ベース配備。cardId=0: 出資者等の EXベースのみ同期。
                    return payload.cardId > 0
                        || (payload.cardId == 0 && payload.defenderExBaseAfter >= 0);
                case DeployShield:
                    return payload.cardId > 0;
                case MountPilot:
                    return payload.cardId > 0 && payload.instanceId > 0;
                default:
                    return payload.cardId > 0;
            }
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>DeployUnit 専用。親 payload が null のときは省略されメッセージ肥大化を防ぐ。</summary>
[Serializable]
public class OnlineDeployUnitExtras
{
    public bool allowOffTurnDeploy;
    public int deployOverrideAp;
    public int deployOverrideHp;
    public bool deployForceUnitType;
    public int deployPrintedType = -1;
    /// <summary>true なら受信側も配備直後に REST（ジオングヘッド等）。</summary>
    public bool deployAsRested;
}

/// <summary>DeployUnit 送信用 lean DTO（EOS ~1170B 対策）。</summary>
[Serializable]
public class OnlineDeployUnitDto
{
    public string action;
    public int cardId;
    public int instanceId;
    public int deployTargetZoneOwnerSide;
    public OnlineDeployUnitExtras deployUnitExtras;
    /// <summary>true なら支払後リソースを同梱（受信側で即レスト表示）。</summary>
    public bool includeResourceSnapshot;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
    /// <summary>相手確認 OK 待ち用。0 は待たない。</summary>
    public int requestId;
}

/// <summary>MountPilot 送信用 lean DTO（EOS ~1170B 対策）。</summary>
[Serializable]
public class OnlineMountPilotDto
{
    public string action;
    public int instanceId;
    public int cardId;
    public bool includeResourceSnapshot;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
    /// <summary>相手確認 OK 待ち用。0 は待たない。</summary>
    public int requestId;
}

/// <summary>UnitAttack 送信用 lean DTO（EOS ~1170B 対策）。</summary>
[Serializable]
public class OnlineUnitAttackDto
{
    public string action;
    public int attackerInstanceId;
    public int defenderInstanceId;
    public int attackerHp;
    public int defenderHp;
    public bool blockCombat;
    public bool skipAttackDeclarationRest;
    public bool includeDefenderAreaSnapshot;
    public int defenderShieldAfter;
    public int defenderExBaseAfter;
    public int defenderDeployedBaseHpAfter;
    /// <summary>防御側 OnDestroyed 待機 requestId（0=なし）。</summary>
    public int requestId;
}

/// <summary>DeployBase 送信用 lean DTO。</summary>
[Serializable]
public class OnlineDeployBaseDto
{
    public string action;
    public int cardId;
    public int baseHpAfter;
    public int defenderExBaseAfter;
    public int defenderShieldAfter;
    public int[] shieldZoneCardIds;
    public bool includeResourceSnapshot;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
}

/// <summary>DeployShield 送信用 lean DTO。</summary>
[Serializable]
public class OnlineDeployShieldDto
{
    public string action;
    public int cardId;
    public int defenderShieldAfter;
    public int[] shieldZoneCardIds;
    public bool includeResourceSnapshot;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
}

/// <summary>AttackDeclare 送信用 lean DTO。</summary>
[Serializable]
public class OnlineAttackDeclareDto
{
    public string action;
    public int requestId;
    public string attackKind;
    public int attackerInstanceId;
    public int defenderInstanceId;
}

/// <summary>BlockResponse 送信用 lean DTO。</summary>
[Serializable]
public class OnlineBlockResponseDto
{
    public string action;
    public int requestId;
    public int blockerInstanceId;
}

/// <summary>OnActionCommandUsed 送信用 lean DTO。</summary>
[Serializable]
public class OnlineOnActionCommandUsedDto
{
    public string action;
    public int cardId;
    public int actingZoneSide;
    public string onActionContext;
    public int cardCost;
    public int cardLevel;
    public int targetCardId = -1;
    public bool includeResourceSnapshot;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
    public int requestId;
}

/// <summary>CommandPlayRevealComplete 送信用 lean DTO。</summary>
[Serializable]
public class OnlineCommandPlayRevealCompleteDto
{
    public string action;
    public int requestId;
}

/// <summary>ResourceState 送信用 lean DTO。</summary>
[Serializable]
public class OnlineResourceStateDto
{
    public string action;
    public int actingZoneSide;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
}

/// <summary>HandDeckState 送信用 lean DTO。</summary>
[Serializable]
public class OnlineHandDeckStateDto
{
    public string action;
    public int actingZoneSide;
    public int handCount;
    public int deckRemainCount;
}

/// <summary>EndTurn 送信用 lean DTO。</summary>
[Serializable]
public class OnlineEndTurnDto
{
    public string action;
    public int requestId;
}

/// <summary>EndTurnAck 送信用 lean DTO。</summary>
[Serializable]
public class OnlineEndTurnAckDto
{
    public string action;
    public int requestId;
}

/// <summary>HandDiscardReveal 送信用 lean DTO。</summary>
[Serializable]
public class OnlineHandDiscardRevealDto
{
    public string action;
    public int cardId;
    public int requestId;
}

/// <summary>HandDiscardRevealComplete 送信用 lean DTO。</summary>
[Serializable]
public class OnlineHandDiscardRevealCompleteDto
{
    public string action;
    public int requestId;
}

/// <summary>DiscardThinkWait / DiscardThinkComplete 送信用 lean DTO。</summary>
[Serializable]
public class OnlineDiscardThinkDto
{
    public string action;
    public int requestId;
}

/// <summary>ShieldBreakComplete 用バースト配備ユニット列。</summary>
[Serializable]
public class OnlineBurstDeployedUnitsSnapshot
{
    public int[] cardIds;
    public int[] instanceIds;
    public int[] ap;
    public int[] hp;
    public int[] printedType;
}

/// <summary>OnActionBegin 専用 lean payload（EOS ~1170B 対策）。</summary>
[Serializable]
public class OnlineOnActionBeginDto
{
    public string action;
    public int requestId;
    public int actingZoneSide;
    public string onActionContext;
    public int attackerInstanceId;
    public int actionStepSessionId;
}

/// <summary>OnActionEnd 専用 lean payload（EOS ~1170B 対策）。</summary>
[Serializable]
public class OnlineOnActionEndDto
{
    public string action;
    public int requestId;
    public int actingZoneSide;
    public int actionStepPassKind;
    public int sessionPlayerActionEnded;
    public int sessionEnemyActionEnded;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
    public int turnEndCommit;
}

/// <summary>ShieldAttack 専用 lean payload。</summary>
[Serializable]
public class OnlineShieldAttackDto
{
    public string action;
    public int attackerInstanceId;
    public int defenderShieldAfter;
    public int defenderExBaseAfter;
    public bool directAttackWin;
    public int[] brokenShieldCardIds;
    public int requestId;
    public bool shieldBreakSimultaneousReveal;
    public int defenderDeployedBaseHpAfter;
    public bool includeDefenderAreaSnapshot;
}

/// <summary>ShieldBreakComplete 専用 lean payload。</summary>
[Serializable]
public class OnlineShieldBreakCompleteDto
{
    public string action;
    public int requestId;
    public int defenderShieldAfter;
    public int defenderExBaseAfter;
    public int defenderDeployedBaseHpAfter;
    public int cardId;
    public int[] shieldZoneCardIds;
    public int[] brokenShieldCardIds;
    public OnlineBurstDeployedUnitsSnapshot burstDeployedUnits;
}
