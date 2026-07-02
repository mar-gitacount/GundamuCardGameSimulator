using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// エネミーAI向け：プレイヤー公開カード・トラッシュのメモリと CardDatabase 色検索によるデッキ推論（ログ一覧）。
/// </summary>
public partial class BattleGameMain
{
    public struct EnemyAiMemorizedPlayerCardSnapshot
    {
        public int CardId;
        public string CardName;
        public CardColor Color;
        public Type CardType;
        public string Category;
        public string Detail;
    }

    private struct EnemyAiMemorizedPlayerCardEntry
    {
        public int CardId;
        public string CardName;
        public CardColor Color;
        public Type CardType;
        public string Category;
        public string Detail;

        public EnemyAiMemorizedPlayerCardSnapshot ToSnapshot()
        {
            return new EnemyAiMemorizedPlayerCardSnapshot
            {
                CardId = CardId,
                CardName = CardName,
                Color = Color,
                CardType = CardType,
                Category = Category,
                Detail = Detail,
            };
        }

        public static EnemyAiMemorizedPlayerCardEntry FromCardData(CardData data, string category, string detail)
        {
            return new EnemyAiMemorizedPlayerCardEntry
            {
                CardId = data.id,
                CardName = data.cardName,
                Color = data.color,
                CardType = data.type,
                Category = category,
                Detail = detail,
            };
        }

        public static EnemyAiMemorizedPlayerCardEntry FromCardId(int cardId, string category, string detail)
        {
            CardData data = ResolveEnemyAiCardDataForMemory(cardId);
            if (data == null)
            {
                return new EnemyAiMemorizedPlayerCardEntry
                {
                    CardId = cardId,
                    CardName = $"id:{cardId}",
                    Color = CardColor.Colorless,
                    CardType = Type.Command,
                    Category = category,
                    Detail = detail,
                };
            }

            return FromCardData(data, category, detail);
        }
    }

    private readonly List<CardColor> enemyAiObservedPlayerCardColors = new List<CardColor>();
    private readonly Dictionary<CardColor, int> enemyAiObservedPlayerColorCounts = new Dictionary<CardColor, int>();
    private readonly HashSet<int> enemyAiRevealedPlayerCardIds = new HashSet<int>();
    private readonly List<EnemyAiMemorizedPlayerCardEntry> enemyAiMemorizedPlayerPlayedCards = new List<EnemyAiMemorizedPlayerCardEntry>();
    private readonly List<EnemyAiMemorizedPlayerCardEntry> enemyAiMemorizedPlayerTrashCards = new List<EnemyAiMemorizedPlayerCardEntry>();
    private readonly Dictionary<int, int> enemyAiMemorizedPlayerTrashCounts = new Dictionary<int, int>();
    private readonly List<CardData> enemyAiCurrentInferenceCandidates = new List<CardData>();
    private readonly List<CardData> enemyAiCurrentAffordableInferenceCandidates = new List<CardData>();

    public IReadOnlyList<CardColor> EnemyAiObservedPlayerCardColorSequence => enemyAiObservedPlayerCardColors;
    public IReadOnlyDictionary<CardColor, int> EnemyAiObservedPlayerColorCounts => enemyAiObservedPlayerColorCounts;
    public IReadOnlyList<CardData> EnemyAiCurrentInferenceCandidates => enemyAiCurrentInferenceCandidates;
    public IReadOnlyList<CardData> EnemyAiCurrentAffordableInferenceCandidates => enemyAiCurrentAffordableInferenceCandidates;

    public IReadOnlyList<EnemyAiMemorizedPlayerCardSnapshot> EnemyAiMemorizedPlayerPlayedCards =>
        BuildEnemyAiMemorizedPlayerCardSnapshots(enemyAiMemorizedPlayerPlayedCards);

    public IReadOnlyList<EnemyAiMemorizedPlayerCardSnapshot> EnemyAiMemorizedPlayerTrashCards =>
        BuildEnemyAiMemorizedPlayerCardSnapshots(enemyAiMemorizedPlayerTrashCards);

    public IReadOnlyDictionary<int, int> EnemyAiMemorizedPlayerTrashCounts => enemyAiMemorizedPlayerTrashCounts;

    private void BindEnemyAiPlayerTrashObservation()
    {
        if (cardGameRule == null)
        {
            return;
        }

        cardGameRule.OnCardAddedToTrash -= OnEnemyAiPlayerCardAddedToTrash;
        cardGameRule.OnCardAddedToTrash += OnEnemyAiPlayerCardAddedToTrash;
    }

    private void UnbindEnemyAiPlayerTrashObservation()
    {
        if (cardGameRule == null)
        {
            return;
        }

        cardGameRule.OnCardAddedToTrash -= OnEnemyAiPlayerCardAddedToTrash;
    }

    private void OnEnemyAiPlayerCardAddedToTrash(int cardId)
    {
        RecordEnemyAiMemorizedPlayerTrashCard(cardId, "TrashZone");
    }

    private void ClearEnemyAiObservedPlayerCardMemory()
    {
        enemyAiObservedPlayerCardColors.Clear();
        enemyAiObservedPlayerColorCounts.Clear();
        enemyAiRevealedPlayerCardIds.Clear();
        enemyAiMemorizedPlayerPlayedCards.Clear();
        enemyAiMemorizedPlayerTrashCards.Clear();
        enemyAiMemorizedPlayerTrashCounts.Clear();
        enemyAiCurrentInferenceCandidates.Clear();
        enemyAiCurrentAffordableInferenceCandidates.Clear();
    }

    private void RecordEnemyAiObservedPlayerCardPlay(CardController card, string playKind)
    {
        if (card == null || card.Data == null)
        {
            return;
        }

        MemorizeEnemyAiPlayerPlayedCard(card.Data, playKind);
        RefreshEnemyAiPlayerDeckInferenceFromDatabase();
    }

    private void MemorizeEnemyAiPlayerPlayedCard(CardData data, string playKind)
    {
        CardColor color = data.color;
        enemyAiObservedPlayerCardColors.Add(color);
        if (!enemyAiObservedPlayerColorCounts.TryGetValue(color, out int count))
        {
            count = 0;
        }

        enemyAiObservedPlayerColorCounts[color] = count + 1;
        enemyAiRevealedPlayerCardIds.Add(data.id);
        enemyAiMemorizedPlayerPlayedCards.Add(EnemyAiMemorizedPlayerCardEntry.FromCardData(data, "Played", playKind));
        LogEnemyAiObservedPlayerCardMemory(data, color, playKind);
    }

    private void RecordEnemyAiMemorizedPlayerTrashCard(int cardId, string trashKind)
    {
        if (cardId < 0)
        {
            return;
        }

        EnemyAiMemorizedPlayerCardEntry entry = EnemyAiMemorizedPlayerCardEntry.FromCardId(cardId, "Trash", trashKind);
        enemyAiMemorizedPlayerTrashCards.Add(entry);
        if (!enemyAiMemorizedPlayerTrashCounts.TryGetValue(cardId, out int count))
        {
            count = 0;
        }

        enemyAiMemorizedPlayerTrashCounts[cardId] = count + 1;
        enemyAiRevealedPlayerCardIds.Add(cardId);

        CardData data = ResolveEnemyAiCardDataForMemory(cardId);
        string name = data != null ? data.cardName : $"id:{cardId}";
        Debug.Log(
            $"[EnemyAI][PlayerTrash] 記録 card:{name}(id:{cardId}) kind:{trashKind} "
            + $"trashTotal:{enemyAiMemorizedPlayerTrashCards.Count} idCount:{enemyAiMemorizedPlayerTrashCounts[cardId]}");
        RefreshEnemyAiPlayerDeckInferenceFromDatabase();
    }

    private static CardData ResolveEnemyAiCardDataForMemory(int cardId)
    {
        if (DeckSettinObject.Instance != null)
        {
            CardData fromDeck = DeckSettinObject.Instance.GetCardDataById(cardId);
            if (fromDeck != null)
            {
                return fromDeck;
            }
        }

        return CardDatabase.Instance != null ? CardDatabase.Instance.GetById(cardId) : null;
    }

    private void LogEnemyAiObservedPlayerCardMemory(CardData data, CardColor latestColor, string playKind)
    {
        StringBuilder order = new StringBuilder();
        for (int i = 0; i < enemyAiObservedPlayerCardColors.Count; i++)
        {
            if (i > 0)
            {
                order.Append(',');
            }

            order.Append(enemyAiObservedPlayerCardColors[i]);
        }

        StringBuilder counts = new StringBuilder();
        bool firstCount = true;
        foreach (KeyValuePair<CardColor, int> entry in enemyAiObservedPlayerColorCounts)
        {
            if (!firstCount)
            {
                counts.Append(' ');
            }

            counts.Append(entry.Key).Append(':').Append(entry.Value);
            firstCount = false;
        }

        Debug.Log(
            $"[EnemyAI][PlayerReveal] 記録 color:{latestColor} card:{data.cardName}(id:{data.id}) "
            + $"play:{playKind} sequence:[{order}] counts:{{{counts}}} playedMem:{enemyAiMemorizedPlayerPlayedCards.Count}");
    }

    private void RefreshEnemyAiPlayerDeckInferenceFromDatabase()
    {
        if (CardDatabase.Instance == null)
        {
            Debug.LogWarning("[EnemyAI][PlayerInference] CardDatabase.Instance が無いため推論をスキップします。");
            return;
        }

        List<CardColor> searchColors = CollectEnemyAiObservedSearchColors();
        enemyAiCurrentInferenceCandidates.Clear();
        enemyAiCurrentAffordableInferenceCandidates.Clear();

        StringBuilder log = new StringBuilder();
        log.AppendLine("[EnemyAI][PlayerInference] ===== デッキ推論一覧 =====");
        AppendEnemyAiMemorizedPlayerCardsSection(log, "公開カード", enemyAiMemorizedPlayerPlayedCards);
        AppendEnemyAiMemorizedPlayerCardsSection(log, "トラッシュ", enemyAiMemorizedPlayerTrashCards);
        log.AppendLine(FormatEnemyAiTrashCountSummaryLine());
        log.AppendLine(FormatEnemyAiFieldAndDeckConstraintLine());

        if (searchColors.Count == 0)
        {
            log.AppendLine("観測色なし — 色ベース DB 検索はスキップします。");
            Debug.Log(log.ToString());
            return;
        }

        log.AppendLine($"検索色: {FormatEnemyAiColorList(searchColors)}");
        log.AppendLine("--- 色別 DB 検索（メモリ除外後） ---");

        HashSet<int> listedIds = new HashSet<int>();
        for (int i = 0; i < searchColors.Count; i++)
        {
            CardColor color = searchColors[i];
            List<CardData> dbMatches = CardDatabase.Instance.FindByColor(color);
            List<CardData> candidates = FilterEnemyAiInferenceCandidatesByMemory(dbMatches);
            AppendEnemyAiInferenceColorSection(log, color, dbMatches.Count, candidates, listedIds);
            MergeEnemyAiInferenceCandidates(enemyAiCurrentInferenceCandidates, candidates);
        }

        log.AppendLine("--- 統合推論候補（重複除去・メモリ除外後） ---");
        if (enemyAiCurrentInferenceCandidates.Count == 0)
        {
            log.AppendLine("  (候補なし)");
        }
        else
        {
            for (int i = 0; i < enemyAiCurrentInferenceCandidates.Count; i++)
            {
                log.AppendLine(FormatEnemyAiInferenceListLine(i + 1, enemyAiCurrentInferenceCandidates[i]));
            }
        }

        log.AppendLine(FormatEnemyAiPlayerResourceStateLine());
        log.AppendLine("--- 色別 利用可能コスト推論（メモリ除外後） ---");

        HashSet<int> listedAffordableIds = new HashSet<int>();
        for (int i = 0; i < searchColors.Count; i++)
        {
            CardColor color = searchColors[i];
            List<CardData> dbMatches = CardDatabase.Instance.FindByColor(color);
            List<CardData> candidates = FilterEnemyAiInferenceCandidatesByMemory(dbMatches);
            List<CardData> affordable = FilterEnemyAiInferenceCandidatesByPlayerAffordableCost(candidates);
            AppendEnemyAiInferenceAffordableColorSection(log, color, candidates.Count, affordable, listedAffordableIds);
            MergeEnemyAiInferenceCandidates(enemyAiCurrentAffordableInferenceCandidates, affordable);
        }

        log.AppendLine("--- 統合利用可能コスト推論候補 ---");
        if (enemyAiCurrentAffordableInferenceCandidates.Count == 0)
        {
            log.AppendLine("  (候補なし)");
        }
        else
        {
            for (int i = 0; i < enemyAiCurrentAffordableInferenceCandidates.Count; i++)
            {
                log.AppendLine(FormatEnemyAiInferenceListLine(i + 1, enemyAiCurrentAffordableInferenceCandidates[i]));
            }
        }

        log.AppendLine(
            $"[EnemyAI][PlayerInference] 合計候補 {enemyAiCurrentInferenceCandidates.Count} 件 / "
            + $"利用可能 {enemyAiCurrentAffordableInferenceCandidates.Count} 件 "
            + $"(観測色 {searchColors.Count} 色 / 公開 {enemyAiMemorizedPlayerPlayedCards.Count} / トラッシュ {enemyAiMemorizedPlayerTrashCards.Count})");
        Debug.Log(log.ToString());
    }

    private List<CardColor> CollectEnemyAiObservedSearchColors()
    {
        List<CardColor> colors = new List<CardColor>();
        foreach (CardColor color in enemyAiObservedPlayerColorCounts.Keys)
        {
            colors.Add(color);
        }

        colors.Sort();
        return colors;
    }

    private Dictionary<int, int> CountEnemyAiPlayerFieldCardCopies()
    {
        Dictionary<int, int> counts = new Dictionary<int, int>();
        for (int i = 0; i < playerBattleZoneCards.Count; i++)
        {
            CardController unit = playerBattleZoneCards[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            IncrementEnemyAiCardCopyCount(counts, unit.Data.id);
            if (unit.MountedPilot != null && unit.MountedPilot.Data != null)
            {
                IncrementEnemyAiCardCopyCount(counts, unit.MountedPilot.Data.id);
            }
        }

        if (cardGameRule != null && cardGameRule.DeployedBase != null && cardGameRule.DeployedBase.Data != null)
        {
            IncrementEnemyAiCardCopyCount(counts, cardGameRule.DeployedBase.Data.id);
        }

        return counts;
    }

    private static void IncrementEnemyAiCardCopyCount(Dictionary<int, int> counts, int cardId)
    {
        if (!counts.TryGetValue(cardId, out int count))
        {
            count = 0;
        }

        counts[cardId] = count + 1;
    }

    private int GetEnemyAiPlayerDeckCopyCount(int cardId)
    {
        if (playerDeckData != null && playerDeckData.TryGetValue(cardId, out int count))
        {
            return count;
        }

        return 0;
    }

    private bool EnemyAiPlayerCardFullyAccountedInMemory(int cardId)
    {
        int deckCopies = GetEnemyAiPlayerDeckCopyCount(cardId);
        if (deckCopies <= 0)
        {
            return true;
        }

        Dictionary<int, int> fieldCounts = CountEnemyAiPlayerFieldCardCopies();
        int trashCount = enemyAiMemorizedPlayerTrashCounts.TryGetValue(cardId, out int trash) ? trash : 0;
        int fieldCount = fieldCounts.TryGetValue(cardId, out int field) ? field : 0;
        return trashCount + fieldCount >= deckCopies;
    }

    private List<CardData> FilterEnemyAiInferenceCandidatesByMemory(List<CardData> source)
    {
        List<CardData> result = new List<CardData>();
        if (source == null)
        {
            return result;
        }

        for (int i = 0; i < source.Count; i++)
        {
            CardData card = source[i];
            if (card == null || EnemyAiPlayerCardFullyAccountedInMemory(card.id))
            {
                continue;
            }

            result.Add(card);
        }

        return result;
    }

    private bool EnemyAiPlayerCanAffordCardNow(CardData card)
    {
        if (card == null || gundamRule == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Player;
        return gundamRule.CanPlayCardWithAnyEx(side, card.level, card.cost);
    }

    private List<CardData> FilterEnemyAiInferenceCandidatesByPlayerAffordableCost(List<CardData> source)
    {
        List<CardData> result = new List<CardData>();
        if (source == null)
        {
            return result;
        }

        for (int i = 0; i < source.Count; i++)
        {
            CardData card = source[i];
            if (card != null && EnemyAiPlayerCanAffordCardNow(card))
            {
                result.Add(card);
            }
        }

        return result;
    }

    private string FormatEnemyAiPlayerResourceStateLine()
    {
        if (gundamRule == null)
        {
            return "プレイヤー利用コスト: (不明)";
        }

        Gundam2024RuleScript.PlayerState state = gundamRule.Player;
        return $"プレイヤー利用コスト: Lv{state.TotalLevel} Resource{state.resource} Ex{state.exResource}";
    }

    private static void AppendEnemyAiInferenceAffordableColorSection(
        StringBuilder log,
        CardColor color,
        int inferenceCount,
        List<CardData> affordable,
        HashSet<int> listedIds)
    {
        log.AppendLine($"[{color}] 推論候補 {inferenceCount} 件 → 利用可能 {affordable.Count} 件");
        if (affordable.Count == 0)
        {
            log.AppendLine("  (利用可能候補なし)");
            return;
        }

        int lineIndex = 0;
        for (int i = 0; i < affordable.Count; i++)
        {
            CardData card = affordable[i];
            if (card == null || listedIds.Contains(card.id))
            {
                continue;
            }

            listedIds.Add(card.id);
            lineIndex++;
            log.AppendLine("  " + FormatEnemyAiInferenceListLine(lineIndex, card));
        }
    }

    private static void MergeEnemyAiInferenceCandidates(List<CardData> merged, List<CardData> add)
    {
        if (add == null || add.Count == 0)
        {
            return;
        }

        HashSet<int> existing = new HashSet<int>();
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i] != null)
            {
                existing.Add(merged[i].id);
            }
        }

        for (int i = 0; i < add.Count; i++)
        {
            CardData card = add[i];
            if (card == null || existing.Contains(card.id))
            {
                continue;
            }

            merged.Add(card);
            existing.Add(card.id);
        }

        merged.Sort((a, b) =>
        {
            int colorCompare = a.color.CompareTo(b.color);
            return colorCompare != 0 ? colorCompare : a.id.CompareTo(b.id);
        });
    }

    private static void AppendEnemyAiInferenceColorSection(
        StringBuilder log,
        CardColor color,
        int dbTotal,
        List<CardData> candidates,
        HashSet<int> listedIds)
    {
        log.AppendLine($"[{color}] DB {dbTotal} 件 → 推論候補 {candidates.Count} 件");
        if (candidates.Count == 0)
        {
            log.AppendLine("  (候補なし)");
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CardData card = candidates[i];
            if (card == null || listedIds.Contains(card.id))
            {
                continue;
            }

            listedIds.Add(card.id);
            log.AppendLine("  " + FormatEnemyAiInferenceListLine(i + 1, card));
        }
    }

    private void AppendEnemyAiMemorizedPlayerCardsSection(
        StringBuilder log,
        string title,
        List<EnemyAiMemorizedPlayerCardEntry> entries)
    {
        log.AppendLine($"--- メモリ: {title} ({entries.Count} 件) ---");
        if (entries.Count == 0)
        {
            log.AppendLine("  (なし)");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            log.AppendLine(FormatEnemyAiMemorizedPlayerCardLine(i + 1, entries[i]));
        }
    }

    private string FormatEnemyAiMemorizedPlayerCardLine(int index, EnemyAiMemorizedPlayerCardEntry entry)
    {
        return $"  {index,3}. [{entry.Category}/{entry.Detail}] {entry.Color,-9} {entry.CardType,-7} id:{entry.CardId,3} {entry.CardName}";
    }

    private string FormatEnemyAiTrashCountSummaryLine()
    {
        if (enemyAiMemorizedPlayerTrashCounts.Count == 0)
        {
            return "トラッシュ集計: (なし)";
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("トラッシュ集計: ");
        bool first = true;
        List<int> ids = new List<int>(enemyAiMemorizedPlayerTrashCounts.Keys);
        ids.Sort();
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (!first)
            {
                sb.Append(", ");
            }

            CardData data = ResolveEnemyAiCardDataForMemory(id);
            string name = data != null ? data.cardName : $"id:{id}";
            sb.Append($"{name}(id:{id})x{enemyAiMemorizedPlayerTrashCounts[id]}");
            first = false;
        }

        return sb.ToString();
    }

    private string FormatEnemyAiFieldAndDeckConstraintLine()
    {
        Dictionary<int, int> fieldCounts = CountEnemyAiPlayerFieldCardCopies();
        StringBuilder sb = new StringBuilder();
        sb.Append($"場のプレイヤーカード: {fieldCounts.Count} 種");
        if (fieldCounts.Count > 0)
        {
            sb.Append(" [");
            bool first = true;
            foreach (KeyValuePair<int, int> entry in fieldCounts)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                CardData data = ResolveEnemyAiCardDataForMemory(entry.Key);
                string name = data != null ? data.cardName : $"id:{entry.Key}";
                sb.Append($"{name}x{entry.Value}");
                first = false;
            }

            sb.Append(']');
        }

        return sb.ToString();
    }

    private static string FormatEnemyAiInferenceListLine(int index, CardData card)
    {
        if (card == null)
        {
            return $"{index,3}. (null)";
        }

        string stats = card.IsUnitLike()
            ? $"AP{card.power}/HP{card.hp}"
            : card.type == Type.Pilot
                ? "AP+ pilot"
                : "-";
        return $"{index,3}. id:{card.id,3} Lv{card.level} Cost{card.cost} {card.type,-7} {card.color,-9} {stats,-8} {card.cardName}";
    }

    private static string FormatEnemyAiColorList(List<CardColor> colors)
    {
        if (colors == null || colors.Count == 0)
        {
            return "(なし)";
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < colors.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(colors[i]);
        }

        return sb.ToString();
    }

    private static List<EnemyAiMemorizedPlayerCardSnapshot> BuildEnemyAiMemorizedPlayerCardSnapshots(
        List<EnemyAiMemorizedPlayerCardEntry> source)
    {
        List<EnemyAiMemorizedPlayerCardSnapshot> snapshots = new List<EnemyAiMemorizedPlayerCardSnapshot>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            snapshots.Add(source[i].ToSnapshot());
        }

        return snapshots;
    }
}
