using System.Collections.Generic;

/// <summary>
/// <see cref="CardData"/> の PilotId 参照ヘルパー。
/// </summary>
public static class CardPilotIdExtensions
{
    public static bool HasPilotId(this CardData card, CardPilotIdData pilotId)
    {
        if (card == null || pilotId == null || card.pilotIds == null)
        {
            return false;
        }

        for (int i = 0; i < card.pilotIds.Count; i++)
        {
            CardPilotIdData owned = card.pilotIds[i];
            if (owned == null)
            {
                continue;
            }

            if (owned == pilotId || owned.id == pilotId.id)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasAnyPilotId(this CardData card, IReadOnlyList<CardPilotIdData> pilotIds)
    {
        if (card == null || pilotIds == null || pilotIds.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < pilotIds.Count; i++)
        {
            CardPilotIdData required = pilotIds[i];
            if (required == null)
            {
                continue;
            }

            if (card.HasPilotId(required) || card.HasPilotIdValue(required.id))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasPilotIdValue(this CardData card, int pilotIdValue)
    {
        if (card == null || card.pilotIds == null)
        {
            return false;
        }

        for (int i = 0; i < card.pilotIds.Count; i++)
        {
            CardPilotIdData pilotId = card.pilotIds[i];
            if (pilotId != null && pilotId.id == pilotIdValue)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasPilotKey(this CardData card, string pilotKey)
    {
        if (card == null || string.IsNullOrWhiteSpace(pilotKey) || card.pilotIds == null)
        {
            return false;
        }

        string key = pilotKey.Trim();
        for (int i = 0; i < card.pilotIds.Count; i++)
        {
            CardPilotIdData pilotId = card.pilotIds[i];
            if (pilotId != null
                && !string.IsNullOrEmpty(pilotId.pilotKey)
                && string.Equals(pilotId.pilotKey, key, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void SetPilotIdsFromIds(this CardData card, int[] pilotIdIds)
    {
        if (card == null)
        {
            return;
        }

        if (card.pilotIds == null)
        {
            card.pilotIds = new List<CardPilotIdData>();
        }
        else
        {
            card.pilotIds.Clear();
        }

        if (pilotIdIds == null || pilotIdIds.Length == 0)
        {
            return;
        }

        card.pilotIds.AddRange(CardPilotIdRegistry.ResolveIds(pilotIdIds));
    }
}
