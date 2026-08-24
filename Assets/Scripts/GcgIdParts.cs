using System;
using UnityEngine;

/// <summary>
/// 公式サイトのカード番号パーツ。
/// セット1・カード1 → ST01-001。トークンはカード番号のみ → T-001。
/// </summary>
[Serializable]
public class GcgIdParts
{
    [Tooltip("スターター / ブースター / Eternal / トークン")]
    public GcgOfficialSetKind setKind = GcgOfficialSetKind.Unset;

    [Tooltip("セット番号（ST01 の 01）。トークンでは不要（無視される）。")]
    public int setNumber;

    [Tooltip("カード番号。1 を入れると 001 になる（公式と同じ3桁）。トークンは T-001。")]
    public int cardNumber;

    public bool IsToken()
    {
        return setKind == GcgOfficialSetKind.Token;
    }

    public bool IsComplete()
    {
        if (setKind == GcgOfficialSetKind.Unset || cardNumber <= 0)
        {
            return false;
        }

        // トークンは setNumber 不要（T-001）
        if (IsToken())
        {
            return true;
        }

        return setNumber > 0;
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
            case GcgOfficialSetKind.Token:
                return "T";
            default:
                return string.Empty;
        }
    }

    /// <summary>例: ST01-001 / T-001</summary>
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

        if (IsToken())
        {
            return prefix + "-" + cardNumber.ToString("000");
        }

        return prefix
            + setNumber.ToString("00")
            + "-"
            + cardNumber.ToString("000");
    }
}
