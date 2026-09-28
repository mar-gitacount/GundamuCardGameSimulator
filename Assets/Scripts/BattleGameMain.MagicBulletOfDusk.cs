using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ST04-014 黄昏の魔弾専用。Lv.2以下（印刷Lv）の味方1体に、このターン中《先制攻撃》を付与する。
/// CardController の ResetRuntime で付与フラグが消えても、EntityId 側で戦闘判定を残す。
/// </summary>
public partial class BattleGameMain
{
    private const int MagicBulletOfDuskCardId = 1000581;
    private const string MagicBulletOfDuskOfficialId = "ST04-014";
    private const int MagicBulletOfDuskMaxPrintedLevel = 2;

    private readonly HashSet<EntityId> _magicBulletFirstStrikeUnityIds = new HashSet<EntityId>();

    private static bool IsMagicBulletOfDuskCard(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        if (data.id == MagicBulletOfDuskCardId)
        {
            return true;
        }

        string officialId = data.gcgOfficialId;
        if (!string.IsNullOrEmpty(officialId)
            && string.Equals(officialId.Trim(), MagicBulletOfDuskOfficialId, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string name = data.cardName;
        return !string.IsNullOrEmpty(name)
            && (name.IndexOf("Magic Bullet of Dusk", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("黄昏の魔弾", System.StringComparison.Ordinal) >= 0);
    }

    private static EffectData CreateMagicBulletOfDuskPickEffect()
    {
        return new EffectData
        {
            type = EffectType.FirstStrike,
            value = 1,
            target = TargetType.AllyUnit,
            selectionMode = EffectSelectionMode.SelectSingle,
            duration = EffectDuration.UntilEndOfTurn
        };
    }

    private List<CardController> CollectMagicBulletOfDuskTargets(PlayerType ownerType)
    {
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        List<CardController> result = new List<CardController>();
        if (zone == null)
        {
            return result;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (!IsMagicBulletOfDuskLegalTarget(unit))
            {
                continue;
            }

            result.Add(unit);
        }

        return result;
    }

    private static bool IsMagicBulletOfDuskLegalTarget(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
        {
            return false;
        }

        // 公式テキストの Lv.2以下はユニットの印刷レベル（搭乗で実効Lvが上がっても対象）。
        return unit.Data.level <= MagicBulletOfDuskMaxPrintedLevel;
    }

    private void GrantMagicBulletOfDuskFirstStrikeToTargets(List<CardController> targets)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit != null && unit.Data != null && unit.Data.IsPilot())
            {
                unit = ResolveEffectSourceBattleHost(unit) ?? unit;
            }

            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            AssignBattleInstanceIdIfNeeded(unit);
            unit.AddFirstStrikeUntilEndOfTurnGrant();
            _magicBulletFirstStrikeUnityIds.Add(unit.GetEntityId());
            Debug.Log(
                $"[ST04-014] grant FirstStrike on {unit.Data.cardName}(id:{unit.Data.id}) "
                + $"entity:{unit.GetEntityId()} count:{_magicBulletFirstStrikeUnityIds.Count}");
        }
    }

    private bool UnitHasMagicBulletOfDuskFirstStrike(CardController unit)
    {
        return unit != null && _magicBulletFirstStrikeUnityIds.Contains(unit.GetEntityId());
    }

    private void ClearMagicBulletOfDuskFirstStrikeGrants()
    {
        _magicBulletFirstStrikeUnityIds.Clear();
    }
}
