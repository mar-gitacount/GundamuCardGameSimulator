using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// エフェクトバトル（攻撃宣言・レストなしでダメージ交換）と実行可否確認 UI。
/// </summary>
public partial class BattleGameMain
{
    private void TryBeginOptionalConfirmedEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onAccepted,
        Action onDeclined)
    {
        if (effect == null || !effect.optionalPlayerConfirm)
        {
            onAccepted?.Invoke();
            return;
        }

        if (ownerType == PlayerType.Enemy)
        {
            onAccepted?.Invoke();
            return;
        }

        StartCoroutine(ShowOptionalEffectConfirmCoroutine(effect, onAccepted, onDeclined));
    }

    private IEnumerator ShowOptionalEffectConfirmCoroutine(
        EffectData effect,
        Action onAccepted,
        Action onDeclined)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onDeclined?.Invoke();
            yield break;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "OptionalEffectConfirm",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.62f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OptionalEffectTitle", UIAnchor.TopCenter, 760, 48);
        if (effect != null && effect.type == EffectType.EffectBattle)
        {
            title.SetLocalizedText("エフェクトバトルを開始しますか？", "Start Effect Battle?");
        }
        else if (effect != null && effect.type == EffectType.MountSelfFromTrashAsPilot)
        {
            title.SetLocalizedText("トラッシュからこのパイロットを搭乗させますか？", "Set this Pilot from Trash?");
        }
        else if (effect != null && effect.type == EffectType.ActivateObservedSpecialMoveCommandOnMain)
        {
            title.SetLocalizedText(
                "捨てた〔必殺技〕の【メイン】を発動しますか？",
                "Activate [Main] of the discarded (Special Move)?");
        }
        else if (effect != null
            && effect.type == EffectType.Buff
            && effect.statTarget == EffectStatTarget.IncomingDamageReduction)
        {
            title.SetLocalizedText(
                "味方ユニット1体にダメージ軽減を付与しますか？",
                "Grant damage reduction to 1 ally Unit?");
        }
        else
        {
            title.SetLocalizedText("この効果を発動しますか？", "Activate this effect?");
        }

        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -120f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("OptionalEffectSubtitle", UIAnchor.TopCenter, 760, 40);
        if (effect != null && effect.type == EffectType.EffectBattle)
        {
            subtitle.SetLocalizedText(
                "レスト不要・攻撃コストなし。選んだ敵ユニットにバトルダメージを与えます。",
                "No Rest / no attack cost. Deal battle damage with a chosen enemy Unit.");
        }
        else if (effect != null && effect.type == EffectType.MountSelfFromTrashAsPilot)
        {
            subtitle.SetLocalizedText(
                "パイロット未搭乗の味方〔MF〕ユニットを選んでください。",
                "Choose an ally [MF] Unit with no Pilot.");
        }
        else if (effect != null && effect.type == EffectType.ActivateObservedSpecialMoveCommandOnMain)
        {
            subtitle.SetLocalizedText("コストなし。辞退もできます。", "No cost. You may decline.");
        }
        else
        {
            subtitle.text = string.Empty;
        }
        subtitle.fontSize = 17;
        subtitle.color = new Color(0.85f, 0.92f, 1f, 1f);
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -170f);

        bool dismissed = false;
        bool accepted = false;

        void ClosePopup()
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
        }

        Button noBtn = root.CreateChildButton("No");
        RectTransform noRt = noBtn.GetComponent<RectTransform>();
        noRt.sizeDelta = new Vector2(180f, 50f);
        noRt.anchorMin = new Vector2(0.5f, 0f);
        noRt.anchorMax = new Vector2(0.5f, 0f);
        noRt.pivot = new Vector2(0.5f, 0f);
        noRt.anchoredPosition = new Vector2(-110f, 120f);
        TextMeshProUGUI noLabel = noBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (noLabel != null)
        {
            noLabel.text = "No";
        }

        noBtn.onClick.AddListener(() =>
        {
            accepted = false;
            dismissed = true;
            ClosePopup();
        });

        Button yesBtn = root.CreateChildButton("Yes");
        RectTransform yesRt = yesBtn.GetComponent<RectTransform>();
        yesRt.sizeDelta = new Vector2(180f, 50f);
        yesRt.anchorMin = new Vector2(0.5f, 0f);
        yesRt.anchorMax = new Vector2(0.5f, 0f);
        yesRt.pivot = new Vector2(0.5f, 0f);
        yesRt.anchoredPosition = new Vector2(110f, 120f);
        TextMeshProUGUI yesLabel = yesBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (yesLabel != null)
        {
            yesLabel.text = "Yes";
        }

        yesBtn.onClick.AddListener(() =>
        {
            accepted = true;
            dismissed = true;
            ClosePopup();
        });

        yield return new WaitUntil(() => dismissed);

        if (accepted)
        {
            onAccepted?.Invoke();
        }
        else
        {
            onDeclined?.Invoke();
        }
    }

    /// <summary>
    /// エフェクトバトル解決。攻撃宣言・レストなしでダメージ交換のみ行う。
    /// </summary>
    private void ResolveEffectBattleCombat(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner)
    {
        if (!IsCardControllerInstanceValid(attacker)
            || !IsCardControllerInstanceValid(defender)
            || attacker.Data == null
            || defender.Data == null
            || !attacker.Data.IsUnitLike()
            || !defender.Data.IsUnitLike())
        {
            Debug.LogWarning("[EffectBattle] Invalid combatants — skipped.");
            return;
        }

        if (attacker.CurrentHp <= 0 || defender.CurrentHp <= 0)
        {
            Debug.Log("[EffectBattle] Combatant HP is 0 — skipped.");
            return;
        }

        PlayerType defenderOwner = ResolveCardOwner(defender.transform);
        int attackerStrike = GetUnitStrikeDamagePower(attacker);
        int defenderStrike = GetUnitStrikeDamagePower(defender);

        Debug.Log(
            $"[EffectBattle] {attacker.Data.cardName}(AP:{attackerStrike}) vs {defender.Data.cardName}(AP:{defenderStrike}) "
            + $"owners atk:{attackerOwner} def:{defenderOwner} note:no REST / no attack declare / no block");

        int attackerHpBefore = attacker.CurrentHp;
        int defenderHpBefore = defender.CurrentHp;

        ApplyUnitVsUnitCombatDamageExchange(
            attacker,
            defender,
            attackerOwner,
            defenderOwner,
            attackerStrike,
            defenderStrike);

        int attackerHpAfter = attacker.CurrentHp;
        int defenderHpAfter = defender.CurrentHp;

        AssignBattleInstanceIdIfNeeded(attacker);
        AssignBattleInstanceIdIfNeeded(defender);

        // 撃破→突破を先に解決し、UnitAttack 1通に防御領域スナップショットを同梱する
        // （別メッセージの ShieldAttack だと相手側で領域同期が欠落することがあった）
        bool includeAreaSnapshot = false;
        int areaShieldAfter = -1;
        int areaExBaseAfter = -1;
        int areaBaseHpAfter = -1;
        if (defenderHpAfter <= 0)
        {
            ClearPendingDefenderDeployedBaseHpForOnlineSync();
            _suppressOnlineDefenderAreaStateNotify = true;
            try
            {
                SendCardToTrash(defender, defenderOwner, attacker);
            }
            finally
            {
                _suppressOnlineDefenderAreaStateNotify = false;
            }

            if (gundamRule != null)
            {
                Gundam2024RuleScript.PlayerState enemyState = gundamRule.Enemy;
                areaShieldAfter = enemyState.shield;
                areaExBaseAfter = enemyState.exBase;
            }

            areaBaseHpAfter = ConsumePendingDefenderDeployedBaseHpForOnlineSync();
            if (areaBaseHpAfter < 0)
            {
                areaBaseHpAfter = ResolveOnlineSyncDeployedBaseHp(Gundam2024RuleScript.PlayerSide.Enemy);
            }

            includeAreaSnapshot = true;
        }

        NotifyLocalUnitAttackResolved(
            attacker,
            defender,
            attackerHpAfter,
            defenderHpAfter,
            blockCombat: false,
            skipAttackDeclarationRest: true,
            includeDefenderAreaSnapshot: includeAreaSnapshot,
            defenderShieldAfter: areaShieldAfter,
            defenderExBaseAfter: areaExBaseAfter,
            defenderDeployedBaseHpAfter: areaBaseHpAfter);

        if (attacker.CurrentHp <= 0)
        {
            SendCardToTrash(attacker, attackerOwner);
        }

        Debug.Log(
            $"[EffectBattle] Result {attacker.Data.cardName} HP:{attackerHpBefore}->{attacker.CurrentHp} "
            + $"{defender.Data.cardName} HP:{defenderHpBefore}->{defender.CurrentHp} "
            + $"areaSnap:{includeAreaSnapshot} baseHp:{areaBaseHpAfter}");

        ClearEndOfBattleCombatModifiers("effect battle");
        SyncAllResourceViewsFromRule();
    }

    private void ApplyEffectBattleToTargets(
        CardController sourceCard,
        PlayerType ownerType,
        List<CardController> targets)
    {
        if (sourceCard == null || targets == null || targets.Count == 0)
        {
            return;
        }

        CardController attacker = sourceCard;
        if (attacker.Data == null || !attacker.Data.IsUnitLike())
        {
            // パイロット側から発火した場合は搭乗ホストを攻撃側にする
            if (_pilotMountEffectHostUnit != null
                && _pilotMountEffectHostUnit.Data != null
                && _pilotMountEffectHostUnit.Data.IsUnitLike())
            {
                attacker = _pilotMountEffectHostUnit;
            }
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController defender = targets[i];
            if (defender == null || defender.Data == null || !defender.Data.IsUnitLike())
            {
                continue;
            }

            ResolveEffectBattleCombat(attacker, defender, ownerType);
            break;
        }
    }
}
