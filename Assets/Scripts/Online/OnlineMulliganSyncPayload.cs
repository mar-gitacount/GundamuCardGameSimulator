using System;
using UnityEngine;

/// <summary>オンライン対戦のマリガン同期（decide → bootstrap の2段）。</summary>
[Serializable]
public class OnlineMulliganSyncPayload
{
  public const string PhaseDecide = "decide";
  public const string PhaseBootstrap = "bootstrap";

  public string phase;
  public bool performedMulligan;
  public int deckRemainCount;
  public int[] shieldCardIds;

  public static bool TryParse(string json, out OnlineMulliganSyncPayload payload)
  {
    payload = null;
    if (string.IsNullOrWhiteSpace(json))
    {
      return false;
    }

    try
    {
      payload = JsonUtility.FromJson<OnlineMulliganSyncPayload>(json);
      return payload != null && !string.IsNullOrWhiteSpace(payload.phase);
    }
    catch
    {
      return false;
    }
  }

  public static string ToJsonDecide(bool performedMulligan, int deckRemainCount)
  {
    return JsonUtility.ToJson(new OnlineMulliganSyncPayload
    {
      phase = PhaseDecide,
      performedMulligan = performedMulligan,
      deckRemainCount = deckRemainCount
    });
  }

  public static string ToJsonBootstrap(int deckRemainCount, int[] shieldCardIds)
  {
    return JsonUtility.ToJson(new OnlineMulliganSyncPayload
    {
      phase = PhaseBootstrap,
      deckRemainCount = deckRemainCount,
      shieldCardIds = shieldCardIds ?? Array.Empty<int>()
    });
  }
}
