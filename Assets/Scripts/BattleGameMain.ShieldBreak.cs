using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>シールド破壊時の公開 UI とバースト（OnBurst）解決。</summary>
public partial class BattleGameMain
{
    private const float ShieldBreakCardSpacing = 24f;

    private struct PendingShieldBreakBatch
    {
        public Gundam2024RuleScript.PlayerSide Side;
        public int Count;
    }

    private IEnumerator RunShieldBreakQueueCoroutine()
    {
        shieldBreakQueueRunning = true;
        try
        {
            while (pendingShieldBreakBatches.Count > 0)
            {
                PendingShieldBreakBatch batch = pendingShieldBreakBatches.Dequeue();
                yield return ProcessShieldBreakBatchCoroutine(batch.Side, batch.Count);
            }
        }
        finally
        {
            shieldBreakQueueRunning = false;
        }
    }

    private IEnumerator ProcessShieldBreakBatchCoroutine(Gundam2024RuleScript.PlayerSide side, int brokenCount)
    {
        if (brokenCount <= 0 || isMatchFinished)
        {
            yield break;
        }

        CardGameRule rule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        PlayerType shieldOwner = side == Gundam2024RuleScript.PlayerSide.Player ? PlayerType.Player : PlayerType.Enemy;
        if (rule == null)
        {
            yield break;
        }

        List<ShieldBreakTaken> takenCards = new List<ShieldBreakTaken>(brokenCount);
        for (int i = 0; i < brokenCount; i++)
        {
            if (isMatchFinished)
            {
                yield break;
            }

            if (!rule.TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken))
            {
                Debug.LogWarning(
                    $"[ShieldBreak] No shield card UI for break {i + 1}/{brokenCount} side:{side} (took {takenCards.Count})");
                break;
            }

            takenCards.Add(taken);
        }

        if (takenCards.Count == 0)
        {
            yield break;
        }

        yield return ShowShieldBreakRevealCoroutine(takenCards, shieldOwner);

        for (int i = 0; i < takenCards.Count; i++)
        {
            ShieldBreakTaken taken = takenCards[i];
            if (CardHasBurstEffects(taken.Data))
            {
                TriggerShieldBurstEffects(taken, shieldOwner);
            }
        }

        SyncAllResourceViewsFromRule();

        for (int i = 0; i < takenCards.Count; i++)
        {
            rule.CommitShieldCardToTrash(takenCards[i]);
        }
    }

    private static bool CardHasBurstEffects(CardData data)
    {
        return data != null && TimedEffectResolver.HasEffectTiming(data, EffectTiming.OnBurst);
    }

    private void TriggerShieldBurstEffects(ShieldBreakTaken taken, PlayerType shieldOwner)
    {
        if (taken.Data == null || taken.Data.timedEffects == null)
        {
            return;
        }

        CardController source = taken.Controller;
        if (source == null || source.Data == null)
        {
            Debug.LogWarning($"[Burst] No visual for shield card id:{taken.CardId} — burst skipped.");
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(shieldOwner, source);
        for (int i = 0; i < taken.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = taken.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnBurst || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null)
                {
                    continue;
                }

                Debug.Log(
                    $"[Burst] {taken.Data.cardName}(id:{taken.Data.id}) owner:{shieldOwner} → {effect.type} target:{effect.target}");
                ApplyEffect(source, shieldOwner, effect);
            }
        }
    }

    private IEnumerator ShowShieldBreakRevealCoroutine(
        List<ShieldBreakTaken> takenCards,
        PlayerType shieldOwner)
    {
        GameObject root = BuildShieldBreakRevealPanel(takenCards, shieldOwner);
        if (root == null)
        {
            yield break;
        }

        bool dismissed = false;
        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(200f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 42f);
        okBtn.onClick.AddListener(() =>
        {
            dismissed = true;
            CloseShieldBreakRevealPanel(root);
        });

        yield return new WaitUntil(() => dismissed);
    }

    private void CloseShieldBreakRevealPanel(GameObject root)
    {
        if (root != null)
        {
            Destroy(root);
        }

        if (activeOnActionPopupRoot == root)
        {
            activeOnActionPopupRoot = null;
        }

        isShieldBreakFlowOpen = false;
    }

    private GameObject BuildShieldBreakRevealPanel(
        List<ShieldBreakTaken> takenCards,
        PlayerType shieldOwner)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || takenCards == null || takenCards.Count == 0 || CardImagePrefab == null)
        {
            return null;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("ShieldBreakReveal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isShieldBreakFlowOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = true;

        int count = takenCards.Count;
        string ownerLabel = shieldOwner == PlayerType.Player ? "プレイヤー" : "エネミー";
        bool anyBurst = false;
        for (int i = 0; i < count; i++)
        {
            if (CardHasBurstEffects(takenCards[i].Data))
            {
                anyBurst = true;
                break;
            }
        }

        TextMeshProUGUI title = root.CreateChildTextCustom("ShieldBreakTitle", UIAnchor.TopCenter, 760, 44);
        title.text = count > 1 ? "シールド破壊（同時）" : "シールド破壊";
        title.fontSize = 26;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("ShieldBreakSub", UIAnchor.TopCenter, 760, 32);
        sub.text = count > 1
            ? $"{ownerLabel}のシールド{count}枚が同時に破壊されました"
            : $"{ownerLabel}のシールド1枚が破壊されました";
        sub.fontSize = 17;
        sub.color = new Color(0.88f, 0.92f, 1f, 1f);
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -52f);

        if (anyBurst)
        {
            TextMeshProUGUI burstBanner = root.CreateChildTextCustom("ShieldBurstBanner", UIAnchor.TopCenter, 640, 28);
            burstBanner.text = "【バースト】あり";
            burstBanner.fontSize = 18;
            burstBanner.color = new Color(1f, 0.85f, 0.45f, 1f);
            burstBanner.alignment = TextAlignmentOptions.Center;
            burstBanner.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -82f);
        }

        float step = BattleCardPreviewWidth + ShieldBreakCardSpacing;
        float startX = -(count - 1) * step * 0.5f;
        const float cardRowY = -118f;

        for (int i = 0; i < count; i++)
        {
            ShieldBreakTaken taken = takenCards[i];
            if (taken.Data == null)
            {
                continue;
            }

            float cardX = startX + i * step;
            bool hasBurst = CardHasBurstEffects(taken.Data);
            string caption = taken.Data.cardName + (hasBurst ? "\n【バースト】" : string.Empty);
            Color captionColor = hasBurst ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(0.92f, 0.92f, 0.92f, 1f);

            if (taken.Controller != null)
            {
                AppendNonInteractiveCardPreview(
                    root,
                    taken.Controller,
                    caption,
                    new Vector2(cardX, cardRowY),
                    captionColor);
            }
            else
            {
                TextMeshProUGUI fallback = root.CreateChildTextCustom(
                    $"ShieldCardFallback_{i}",
                    UIAnchor.TopCenter,
                    200,
                    60);
                fallback.text = caption + $"\n(ID:{taken.CardId})";
                fallback.fontSize = 14;
                fallback.color = captionColor;
                fallback.alignment = TextAlignmentOptions.Center;
                fallback.GetComponent<RectTransform>().anchoredPosition = new Vector2(cardX, cardRowY - 80f);
            }
        }

        TextMeshProUGUI hint = root.CreateChildTextCustom("ShieldBreakHint", UIAnchor.TopCenter, 760, 28);
        hint.text = "内容を確認して OK で続行（バーストは OK 後に解決）";
        hint.fontSize = 15;
        hint.color = new Color(0.8f, 0.85f, 0.9f, 1f);
        hint.alignment = TextAlignmentOptions.Center;
        float hintY = count > 1 ? -340f : -320f;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, hintY);

        return root;
    }
}
