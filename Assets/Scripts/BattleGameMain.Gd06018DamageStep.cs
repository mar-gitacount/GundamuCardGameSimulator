using System;
using UnityEngine;

/// <summary>
/// GD06-018 専用。UI で選んだカード参照をそのまま保持し、
/// キュベレイが受ける戦闘ダメージだけをそのカードへ移す。キュベレイ本体は 0。
/// </summary>
public partial class BattleGameMain
{
    private bool _gd06018Active;
    private CardController _gd06018PickedLive;
    private PlayerType _gd06018Owner;
    private int _gd06018RecipientCardId;
    private bool _gd06018RecipientIsBase;

    private void ArmGd06018ReplacementDamageStep(
        CardController source,
        CardController picked,
        PlayerType owner)
    {
        if (picked == null || picked.Data == null)
        {
            return;
        }

        CardController livePicked = ResolveLiveIncomingRedirectPicked(picked, owner) ?? picked;
        _gd06018Active = true;
        _gd06018PickedLive = livePicked;
        _gd06018Owner = owner;
        _gd06018RecipientCardId = livePicked.Data.id;
        _gd06018RecipientIsBase = livePicked.Data.type == Type.Base || IsCardInBaseSlot(livePicked);
        int hostCardId = source != null && source.Data != null ? source.Data.id : 0;
        CardController host = MatchIncomingRedirectHostInAttackFlow(hostCardId)
            ?? ResolveEffectSourceBattleHost(source)
            ?? source;
        if (host != null)
        {
            owner = ResolveIncomingRedirectHostOwner(host, owner);
            _gd06018Owner = owner;
        }

        if (host != null && host.Data != null && host.Data.IsUnitLike())
        {
            host.SetIncomingBattleDamageRedirectUntilEndOfBattle(livePicked);
            host.SetIncomingBattleDamageRedirectMeta(
                _gd06018RecipientCardId,
                _gd06018RecipientIsBase,
                (int)owner);
        }

        Debug.Log(
            $"[GD06-018] UI選択を保持 → {livePicked.Data.cardName}(id:{livePicked.Data.id} type:{livePicked.Data.type}) "
            + $"source:{source?.Data?.cardName}(id:{source?.Data?.id}) host:{host?.Data?.cardName}");
    }

    /// <summary>
    /// キュベレイがダメージ対象なら、選択カードへ移してキュベレイは受けない。
    /// true ならこのダメージは処理済み。
    /// </summary>
    private bool TryMoveGd06018IncomingDamageOffQubeley(
        CardController damageTarget,
        CardController damageSource,
        int damage)
    {
        if (!_gd06018Active || damage <= 0 || !IsGd06018QubeleyMkII(damageTarget))
        {
            return false;
        }

        CardController picked = GetGd06018PickedLive();
        if (picked != null && picked != damageTarget && picked.CurrentHp > 0)
        {
            Debug.Log(
                $"[GD06-018] キュベレイの被ダメージ {damage} をキャンセル → {picked.Data.cardName} へ移行");
            ApplyResolvedBattleDamageToRecipient(picked, damageTarget, damageSource, damage);
        }
        else
        {
            Debug.LogWarning("[GD06-018] 移行先なし。キュベレイの被ダメージのみキャンセル");
        }

        return true;
    }

    private bool TryApplyGd06018ReplacementDamageStep(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner,
        int attackerStrike,
        int defenderStrike)
    {
        if (!_gd06018Active)
        {
            return false;
        }

        CardController qubeley;
        CardController opponent;
        PlayerType qubeleyOwner;
        PlayerType opponentOwner;
        int qubeleyStrike;
        int opponentStrike;
        if (IsGd06018QubeleyMkII(attacker))
        {
            qubeley = attacker;
            opponent = defender;
            qubeleyOwner = attackerOwner;
            opponentOwner = defenderOwner;
            qubeleyStrike = attackerStrike;
            opponentStrike = defenderStrike;
        }
        else if (IsGd06018QubeleyMkII(defender))
        {
            qubeley = defender;
            opponent = attacker;
            qubeleyOwner = defenderOwner;
            opponentOwner = attackerOwner;
            qubeleyStrike = defenderStrike;
            opponentStrike = attackerStrike;
        }
        else
        {
            return false;
        }

        CardController picked = GetGd06018PickedLive();
        Debug.Log(
            $"[GD06-018] 通常交換スキップ picked:{picked?.Data?.cardName} "
            + $"opponent:{opponent?.Data?.cardName} in:{opponentStrike} out:{qubeleyStrike}");

        if (picked != null && picked != qubeley && opponentStrike > 0)
        {
            ApplyCombatDamageIfNotImmune(
                picked,
                opponent,
                ResolveCardOwner(picked.transform),
                opponentOwner,
                opponentStrike);
        }

        if (opponent != null && qubeleyStrike > 0)
        {
            ApplyCombatDamageIfNotImmune(
                opponent,
                qubeley,
                opponentOwner,
                qubeleyOwner,
                qubeleyStrike);
        }

        return true;
    }

    private CardController GetGd06018PickedLive()
    {
        CardController live = ResolveLiveIncomingRedirectPicked(_gd06018PickedLive, _gd06018Owner);
        if (live != null && live.Data != null && live.CurrentHp > 0)
        {
            return live;
        }

        if (_gd06018RecipientCardId <= 0)
        {
            return null;
        }

        return FindIncomingRedirectRecipientByCardId(
            _gd06018RecipientCardId,
            _gd06018RecipientIsBase,
            _gd06018Owner,
            _battleIncomingRedirectHost);
    }

    private CardController ResolveLiveIncomingRedirectPicked(CardController picked, PlayerType owner)
    {
        if (picked == null || picked.Data == null)
        {
            return null;
        }

        if (picked.Data.type == Type.Base || IsCardInBaseSlot(picked))
        {
            return FindOwnerBaseForIncomingRedirect(owner, picked.Data.id)
                ?? FindLiveOwnerBaseMatching(picked)
                ?? (picked.CurrentHp > 0 ? picked : null);
        }

        return FindLiveBattleZoneUnitMatching(picked)
            ?? (picked.CurrentHp > 0 && picked.Data.IsUnitLike() ? picked : null);
    }

    private static bool IsGd06018QubeleyMkII(CardController unit)
    {
        return unit != null && IsGd06018QubeleyMkIIData(unit.Data);
    }

    private static bool IsGd06018QubeleyMkIIData(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        if (data.id == Gd06018QubeleyMkIICardId)
        {
            return true;
        }

        if (string.IsNullOrEmpty(data.gcgOfficialId))
        {
            return false;
        }

        string compact = data.gcgOfficialId.Replace("-", string.Empty).Replace(" ", string.Empty);
        return compact.Equals("GD06018", StringComparison.OrdinalIgnoreCase)
            || compact.Equals("GD006018", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearGd06018ReplacementDamageStep()
    {
        _gd06018Active = false;
        _gd06018PickedLive = null;
        _gd06018RecipientCardId = 0;
        _gd06018RecipientIsBase = false;
    }
}
