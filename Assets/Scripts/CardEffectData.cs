using System;
using System.Collections.Generic;

public enum EffectTiming
{
    OnPlayed,       // 手札から出した時（召喚時）
    OnTurnStart,    // 自分のターン開始時
    OnTurnEnd,      // 自分のターン終了時
    OnAttack,       // 攻撃する時
    OnAction,       // 任意アクション（攻撃時/ターン終了時に手札から実行可能）
    OnDestroyed,    // 破壊された時
    OnEndOfGame     // ゲーム終了時
}

public enum EffectType
{
    Damage,
    Draw,
    Buff,
    Debuff
}

public enum TargetType
{
    Self,
    AllyUnit,
    EnemyUnit,
    AllyAllUnits,
    EnemyAllUnits,
    SelfPlayer,
    EnemyPlayer
}

public enum EffectSelectionMode
{
    AttackedTargetOnly,
    SelectSingleEnemyUnit,
    SelectMultipleEnemyUnits
}

public enum EffectStatTarget
{
    AP,
    HP,
    Both
}

[Serializable]
public class EffectData
{
    public EffectType type;
    public int value;
    public TargetType target;
    public EffectSelectionMode selectionMode = EffectSelectionMode.AttackedTargetOnly;
    public EffectStatTarget statTarget = EffectStatTarget.Both;
}

[Serializable]
public class TimedEffectData
{
    public EffectTiming timing;
    public List<EffectData> effects = new List<EffectData>();
}

