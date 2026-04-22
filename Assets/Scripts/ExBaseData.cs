using UnityEngine;

/// <summary>
/// EXベースの初期値・説明（ルール詳細はプロジェクト直下の Gundam_Rules.pdf を参照）。
/// バトル開始時（マリガン後のシールド設置と同タイミング）に初期ポイントを適用する。
/// </summary>
[CreateAssetMenu(menuName = "Game/Ex Base Data", fileName = "ExBaseData")]
public class ExBaseData : ScriptableObject
{
    /// <summary>ゲーム開始時にプレイヤー双方が持つ EX ベースの値（本実装では 3）。</summary>
    [Min(0)]
    public int startingPoints = 3;

    [TextArea(2, 5)]
    public string ruleReferenceNote = "ルールの解釈・数値の根拠は Gundam_Rules.pdf を参照すること。";
}
