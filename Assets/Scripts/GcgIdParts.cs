using System;
using UnityEngine;

/// <summary>
/// 公式サイトのカード番号パーツ。
/// セット1・カード1 → ST01-001（公式 detailSearch と同じ形式）。
/// </summary>
[Serializable]
public class GcgIdParts
{
    [Tooltip("スターター / ブースター / Eternal")]
    public GcgOfficialSetKind setKind = GcgOfficialSetKind.Unset;

    [Tooltip("セット番号。1 を入れると ST01 の 01 になる。")]
    public int setNumber;

    [Tooltip("カード番号。1 を入れると 001 になる（公式と同じ3桁）。")]
    public int cardNumber;

    public bool IsComplete()
    {
        return setKind != GcgOfficialSetKind.Unset && setNumber > 0 && cardNumber > 0;
    }

    public string ResolvePrefix()
    {
        switch (setKind)
        {
            case GcgOfficialSetKind.Starter:
                return "ST";
            case GcgOfficialSetKind.Booster:
                return "GD";
            case GcgOfficialSetKind.EternalBooster:
                return "EB";
            default:
                return string.Empty;
        }
    }

    /// <summary>例: setNumber=1, cardNumber=1 → ST01-001</summary>
    public string FormatId()
    {
        if (!IsComplete())
        {
            return string.Empty;
        }

        string prefix = ResolvePrefix();
        if (string.IsNullOrEmpty(prefix))
        {
            return string.Empty;
        }

        return prefix
            + setNumber.ToString("00")
            + "-"
            + cardNumber.ToString("000");
    }
}
