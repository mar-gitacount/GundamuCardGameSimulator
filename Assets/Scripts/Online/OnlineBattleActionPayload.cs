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
    public string attackKind;

    public const string DeployUnit = "DeployUnit";
    public const string DeployBase = "DeployBase";
    public const string DeployShield = "DeployShield";
    public const string ShieldAttack = "ShieldAttack";
    public const string UnitAttack = "UnitAttack";
    public const string AttackDeclare = "AttackDeclare";
    public const string BlockResponse = "BlockResponse";
    public const string AttackKindShield = "Shield";
    public const string AttackKindUnitVsUnit = "UnitVsUnit";

    public static string CreateDeployUnit(int cardId, int instanceId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = DeployUnit,
            cardId = cardId,
            instanceId = instanceId
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
        bool directAttackWin)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = ShieldAttack,
            attackerInstanceId = attackerInstanceId,
            defenderShieldAfter = defenderShieldAfter,
            defenderExBaseAfter = defenderExBaseAfter,
            directAttackWin = directAttackWin
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
                case UnitAttack:
                case AttackDeclare:
                    return payload.attackerInstanceId > 0;
                case BlockResponse:
                    return payload.requestId > 0;
                case DeployUnit:
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
