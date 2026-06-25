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
    public int attackerInstanceId;
    public int defenderInstanceId;
    public int attackerHp;
    public int defenderHp;
    public int defenderShieldAfter;
    public int defenderExBaseAfter;
    public bool directAttackWin;

    public const string DeployUnit = "DeployUnit";
    public const string DeployBase = "DeployBase";
    public const string DeployShield = "DeployShield";
    public const string ShieldAttack = "ShieldAttack";
    public const string UnitAttack = "UnitAttack";

    public static string CreateDeployUnit(int cardId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = DeployUnit,
            cardId = cardId
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
        int defenderHp)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = UnitAttack,
            attackerInstanceId = attackerInstanceId,
            defenderInstanceId = defenderInstanceId,
            attackerHp = attackerHp,
            defenderHp = defenderHp
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

            if (payload.action == ShieldAttack || payload.action == UnitAttack)
            {
                return payload.attackerInstanceId > 0;
            }

            return payload.cardId > 0;
        }
        catch
        {
            return false;
        }
    }
}
