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
    public bool shieldBreakSimultaneousReveal;
    public int[] brokenShieldCardIds;
    public string attackKind;
    public string onActionContext;
    public int actingZoneSide;
    public int resourceAfter;
    public int exResourceAfter;
    public int levelAfter;
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
    public const string ShieldBreakComplete = "ShieldBreakComplete";
    public const string AttackKindShield = "Shield";
    public const string AttackKindUnitVsUnit = "UnitVsUnit";

    public const string HandDiscardReveal = "HandDiscardReveal";
    public const string HandDiscardRevealComplete = "HandDiscardRevealComplete";

    public static string CreateHandDiscardReveal(int cardId, int requestId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = HandDiscardReveal,
            cardId = cardId,
            requestId = requestId
        });
    }

    public static string CreateHandDiscardRevealComplete(int requestId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = HandDiscardRevealComplete,
            requestId = requestId
        });
    }

    public static string CreateDeployUnit(int cardId, int instanceId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = DeployUnit,
            cardId = cardId,
            instanceId = instanceId
        });
    }

    public static string CreateDeployBase(
        int cardId,
        int baseHpAfter,
        int exBaseAfter,
        int shieldCountAfter,
        int[] shieldZoneCardIds)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = DeployBase,
            cardId = cardId,
            baseHpAfter = baseHpAfter,
            defenderExBaseAfter = exBaseAfter,
            defenderShieldAfter = shieldCountAfter,
            shieldZoneCardIds = shieldZoneCardIds ?? Array.Empty<int>()
        });
    }

    public static string CreateDeployShield(
        int cardId,
        int shieldCountAfter,
        int[] shieldZoneCardIds)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = DeployShield,
            cardId = cardId,
            defenderShieldAfter = shieldCountAfter,
            shieldZoneCardIds = shieldZoneCardIds ?? Array.Empty<int>()
        });
    }

    public static string CreateAttackDeclare(
        int requestId,
        string attackKind,
        int attackerInstanceId,
        int defenderInstanceId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
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
        return JsonUtility.ToJson(new OnlineBattleActionPayload
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
        int defenderDeployedBaseHpAfter = -1)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = ShieldAttack,
            attackerInstanceId = attackerInstanceId,
            defenderShieldAfter = defenderShieldAfter,
            defenderExBaseAfter = defenderExBaseAfter,
            directAttackWin = directAttackWin,
            brokenShieldCardIds = brokenShieldCardIds,
            requestId = shieldBreakRequestId,
            shieldBreakSimultaneousReveal = shieldBreakSimultaneousReveal,
            defenderDeployedBaseHpAfter = defenderDeployedBaseHpAfter
        });
    }

    public static string CreateShieldBreakComplete(
        int requestId,
        int defenderShieldAfter = -1,
        int defenderExBaseAfter = -1,
        int defenderDeployedBaseHpAfter = -1,
        int deployedBaseCardId = 0,
        int[] shieldZoneCardIds = null,
        int[] brokenShieldCardIds = null)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = ShieldBreakComplete,
            requestId = requestId,
            defenderShieldAfter = defenderShieldAfter,
            defenderExBaseAfter = defenderExBaseAfter,
            defenderDeployedBaseHpAfter = defenderDeployedBaseHpAfter,
            cardId = deployedBaseCardId,
            shieldZoneCardIds = shieldZoneCardIds ?? System.Array.Empty<int>(),
            brokenShieldCardIds = brokenShieldCardIds
        });
    }

    public static string CreateMountPilot(int hostInstanceId, int pilotCardId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = MountPilot,
            instanceId = hostInstanceId,
            cardId = pilotCardId
        });
    }

    public static string CreateUnitAttack(
        int attackerInstanceId,
        int defenderInstanceId,
        int attackerHp,
        int defenderHp,
        bool blockCombat = false)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = UnitAttack,
            attackerInstanceId = attackerInstanceId,
            defenderInstanceId = defenderInstanceId,
            attackerHp = attackerHp,
            defenderHp = defenderHp,
            blockCombat = blockCombat
        });
    }

    public static string CreateOnActionBegin(
        int requestId,
        int actingZoneSide,
        string context,
        int attackerInstanceId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = OnActionBegin,
            requestId = requestId,
            actingZoneSide = actingZoneSide,
            onActionContext = context ?? string.Empty,
            attackerInstanceId = attackerInstanceId
        });
    }

    public static string CreateOnActionEnd(
        int requestId,
        int resourceAfter,
        int exResourceAfter,
        int levelAfter)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = OnActionEnd,
            requestId = requestId,
            resourceAfter = resourceAfter,
            exResourceAfter = exResourceAfter,
            levelAfter = levelAfter
        });
    }

    public static string CreateOnActionCommandUsed(
        int cardId,
        int actingZoneSide,
        string context,
        int cardCost,
        int cardLevel,
        int targetCardId = -1)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = OnActionCommandUsed,
            cardId = cardId,
            actingZoneSide = actingZoneSide,
            onActionContext = context ?? string.Empty,
            cardCost = cardCost,
            cardLevel = cardLevel,
            targetCardId = targetCardId
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
                    return payload.attackerInstanceId > 0
                        || payload.directAttackWin
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
                case HandDiscardRevealComplete:
                    return payload.requestId > 0;
                case HandDiscardReveal:
                    return payload.cardId > 0 && payload.requestId > 0;
                case DeployUnit:
                    return payload.cardId > 0 && payload.instanceId > 0;
                case DeployBase:
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
