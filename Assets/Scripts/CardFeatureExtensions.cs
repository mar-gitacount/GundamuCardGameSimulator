using System.Collections.Generic;

/// <summary>
/// <see cref="CardData"/> の Feature 参照ヘルパー。
/// </summary>
public static class CardFeatureExtensions
{
    public static bool HasFeature(this CardData card, CardFeatureData feature)
    {
        if (card == null || feature == null || card.features == null)
        {
            return false;
        }

        for (int i = 0; i < card.features.Count; i++)
        {
            CardFeatureData owned = card.features[i];
            if (owned == null)
            {
                continue;
            }

            // 同一アセット参照、または同一 id（ロード経路でインスタンスが分かれる場合）
            if (owned == feature || owned.id == feature.id)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasAnyFeature(this CardData card, IReadOnlyList<CardFeatureData> features)
    {
        if (card == null || features == null || features.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < features.Count; i++)
        {
            CardFeatureData required = features[i];
            if (required == null)
            {
                continue;
            }

            if (card.HasFeature(required) || card.HasFeatureId(required.id))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasFeatureId(this CardData card, int featureId)
    {
        if (card == null || card.features == null)
        {
            return false;
        }

        for (int i = 0; i < card.features.Count; i++)
        {
            CardFeatureData feature = card.features[i];
            if (feature != null && feature.id == featureId)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasFeatureKey(this CardData card, string featureKey)
    {
        if (card == null || string.IsNullOrWhiteSpace(featureKey) || card.features == null)
        {
            return false;
        }

        string key = featureKey.Trim();
        for (int i = 0; i < card.features.Count; i++)
        {
            CardFeatureData feature = card.features[i];
            if (feature != null
                && !string.IsNullOrEmpty(feature.featureKey)
                && string.Equals(feature.featureKey, key, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void SetFeaturesFromIds(this CardData card, int[] featureIds)
    {
        if (card == null)
        {
            return;
        }

        if (card.features == null)
        {
            card.features = new List<CardFeatureData>();
        }
        else
        {
            card.features.Clear();
        }

        if (featureIds == null || featureIds.Length == 0)
        {
            return;
        }

        card.features.AddRange(CardFeatureRegistry.ResolveIds(featureIds));
    }
}
