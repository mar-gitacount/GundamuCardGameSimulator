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

    public const string DeployUnit = "DeployUnit";
    public const string DeployBase = "DeployBase";
    public const string DeployShield = "DeployShield";

    public static string CreateDeployUnit(int cardId)
    {
        return JsonUtility.ToJson(new OnlineBattleActionPayload
        {
            action = DeployUnit,
            cardId = cardId
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
            return payload != null && !string.IsNullOrWhiteSpace(payload.action) && payload.cardId > 0;
        }
        catch
        {
            return false;
        }
    }
}
