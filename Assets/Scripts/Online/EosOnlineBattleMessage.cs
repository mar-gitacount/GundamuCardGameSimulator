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

    public const string MatchAccept = "MatchAccept";
    public const string MatchCancel = "MatchCancel";

    public static string CreateMatchAccept()
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = MatchAccept
        });
    }

    public static string CreateMatchCancel()
    {
        return JsonUtility.ToJson(new EosOnlineBattleMessage
        {
            type = MatchCancel
        });
    }

    public static string CreateEndTurn(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "EndTurn",
            payload = payload ?? string.Empty
        });
    }

    public const string EndTurnAck = "EndTurnAck";

    public static string CreateEndTurnAck(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = EndTurnAck,
            payload = payload ?? string.Empty
        });
    }

    public static string CreatePlayCard(string payload)
    {
        // seed/lobbyId を載せない（DeployUnit 等が EOS ~1170B を超えやすい）
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "PlayCard",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateAttack(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "Attack",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateAttackDeclare(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "AttackDeclare",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateBlockResponse(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "BlockResponse",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateEffectSync(string effectPayloadJson)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "EffectSync",
            payload = effectPayloadJson ?? string.Empty
        });
    }

    public static string CreateMountPilot(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "MountPilot",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateOnActionBegin(string payload)
    {
        // seed/lobbyId 等を載せない最小ラッパ（ネスト JSON のエスケープ膨張を抑える）
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "OnActionBegin",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateOnActionEnd(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "OnActionEnd",
            payload = payload ?? string.Empty
        });
    }

    public const string OnActionCommandUsed = "OnActionCommandUsed";
    public const string CommandPlayRevealComplete = "CommandPlayRevealComplete";

    public static string CreateOnActionCommandUsed(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = OnActionCommandUsed,
            payload = payload ?? string.Empty
        });
    }

    public static string CreateCommandPlayRevealComplete(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = CommandPlayRevealComplete,
            payload = payload ?? string.Empty
        });
    }

    public static string CreateMulliganSync(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "MulliganSync",
            payload = payload ?? string.Empty
        });
    }

    public static string CreateShieldBreakComplete(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "ShieldBreakComplete",
            payload = payload ?? string.Empty
        });
    }

    public const string HandDiscardReveal = "HandDiscardReveal";
    public const string HandDiscardRevealComplete = "HandDiscardRevealComplete";

    public static string CreateHandDiscardReveal(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = HandDiscardReveal,
            payload = payload ?? string.Empty
        });
    }

    public static string CreateHandDiscardRevealComplete(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = HandDiscardRevealComplete,
            payload = payload ?? string.Empty
        });
    }

    public static string CreateZoneSync(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = "ZoneSync",
            payload = payload ?? string.Empty
        });
    }

    public const string ResourceState = "ResourceState";

    public static string CreateResourceState(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = ResourceState,
            payload = payload ?? string.Empty
        });
    }

    public const string HandDeckState = "HandDeckState";

    public static string CreateHandDeckState(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = HandDeckState,
            payload = payload ?? string.Empty
        });
    }

    public const string OnDestroyedComplete = "OnDestroyedComplete";

    public static string CreateOnDestroyedComplete(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = OnDestroyedComplete,
            payload = payload ?? string.Empty
        });
    }

    /// <summary>自分が破壊時効果を解決中なので、相手は effectthink で待て、という通知。</summary>
    public const string EffectThinkWait = "EffectThinkWait";

    public static string CreateEffectThinkWait(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = EffectThinkWait,
            payload = payload ?? string.Empty
        });
    }

    public const string OpponentUnitPickRequest = "OpponentUnitPickRequest";
    public const string OpponentUnitPickResponse = "OpponentUnitPickResponse";

    public static string CreateOpponentUnitPickRequest(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = OpponentUnitPickRequest,
            payload = payload ?? string.Empty
        });
    }

    public static string CreateOpponentUnitPickResponse(string payload)
    {
        return JsonUtility.ToJson(new EosOnlineLeanEnvelope
        {
            type = OpponentUnitPickResponse,
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

/// <summary>type + payload のみ。送信用に seed/lobbyId を載せずバイト数を抑える。</summary>
[Serializable]
public class EosOnlineLeanEnvelope
{
    public string type;
    public string payload;
}
