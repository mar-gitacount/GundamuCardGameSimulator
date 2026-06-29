using System;
using UnityEngine;

/// <summary>
/// P2P 上で流すオンライン対戦メッセージの最小フォーマット。
/// </summary>
[Serializable]
public class EosOnlineBattleMessage
{
    public string type;
    public int seed;
    public bool hostGoesFirst;
    public string lobbyId;
    public string payload;

    public static string CreateHello(string text)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "hello",
            payload = text ?? "hello"
        });
    }

    public static string CreatePing()
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "ping",
            payload = DateTime.UtcNow.ToString("O")
        });
    }

    public static string CreatePong()
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "pong",
            payload = DateTime.UtcNow.ToString("O")
        });
    }

    public static string CreateMatchStart(int seed, bool hostGoesFirst, string lobbyId)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "MatchStart",
            seed = seed,
            hostGoesFirst = hostGoesFirst,
            lobbyId = lobbyId ?? string.Empty
        });
    }

    public static string CreateEndTurn()
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "EndTurn"
        });
    }

    public static string CreatePlayCard(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "PlayCard",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateAttack(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "Attack",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateAttackDeclare(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "AttackDeclare",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateBlockResponse(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "BlockResponse",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateEffectSync(string effectPayloadJson)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "EffectSync",
            payload = effectPayloadJson ?? string.Empty
        });
    }

    public static string CreateMountPilot(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "MountPilot",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateOnActionBegin(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "OnActionBegin",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateOnActionEnd(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "OnActionEnd",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateMulliganSync(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "MulliganSync",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateShieldBreakComplete(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "ShieldBreakComplete",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateZoneSync(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = "ZoneSync",
            payload = payload ?? string.Empty
        });
    }

    public static bool TryParse(string raw, out EosOnlineBattleMessage message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            message = JsonUtility.FromJson<EosOnlineBattleMessage>(raw);
            return message != null && !string.IsNullOrWhiteSpace(message.type);
        }
        catch
        {
            return false;
        }
    }
}
