using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 開発者モードの認可・設定（UI は持たない。Online パネル側にトグルを置く）。
/// </summary>
public static class DeveloperModeAccess
{
    private const string DevicesResourcePath = "Data/Json/developer_mode_devices";
    private const string PrefsStartAtLevel10 = "DeveloperMode.StartAtLevel10";
    private const int StartLevelOverride = 10;

    private static bool _loaded;
    private static bool _authorized;
    private static string _resolvedDeviceId = string.Empty;
    private static readonly HashSet<string> AllowedIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static bool IsAuthorized
    {
        get
        {
            EnsureLoaded();
            return _authorized;
        }
    }

    public static string ResolvedDeviceId
    {
        get
        {
            EnsureLoaded();
            return _resolvedDeviceId;
        }
    }

    public static bool StartAtLevel10
    {
        get => PlayerPrefs.GetInt(PrefsStartAtLevel10, 0) != 0;
        set
        {
            PlayerPrefs.SetInt(PrefsStartAtLevel10, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static bool ShouldApplyStartAtLevel10 => IsAuthorized && StartAtLevel10;

    public static void ApplyStartingLevelOverride(Gundam2024RuleScript rule)
    {
        if (rule == null || !ShouldApplyStartAtLevel10)
        {
            return;
        }

        rule.Config.startingLevel = StartLevelOverride;
        rule.Config.startingResource = StartLevelOverride;
        ForceBothSidesStartingLevel(rule);
        Debug.Log($"[DeveloperMode] Start LV/cost={StartLevelOverride}");
    }

    public static void ForceBothSidesStartingLevel(Gundam2024RuleScript rule)
    {
        if (rule == null || !ShouldApplyStartAtLevel10)
        {
            return;
        }

        if (rule.Player != null)
        {
            rule.Player.level = StartLevelOverride;
            rule.Player.resource = StartLevelOverride;
        }

        if (rule.Enemy != null)
        {
            rule.Enemy.level = StartLevelOverride;
            rule.Enemy.resource = StartLevelOverride;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _resolvedDeviceId = string.IsNullOrEmpty(SystemInfo.deviceUniqueIdentifier)
            ? "unknown"
            : SystemInfo.deviceUniqueIdentifier.Trim();
        LoadAllowedIds();

        List<string> candidates = new List<string> { _resolvedDeviceId };
#if UNITY_ANDROID && !UNITY_EDITOR
        string androidId = TryReadAndroidId();
        if (!string.IsNullOrEmpty(androidId))
        {
            candidates.Add(androidId.Trim());
        }
#endif

#if UNITY_EDITOR
        _authorized = true;
#else
        _authorized = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!string.IsNullOrEmpty(candidates[i]) && AllowedIds.Contains(candidates[i]))
            {
                _authorized = true;
                _resolvedDeviceId = candidates[i];
                break;
            }
        }
#endif

        Debug.Log(
            $"[DeveloperMode] deviceId='{_resolvedDeviceId}' authorized={_authorized} "
            + $"allowedCount={AllowedIds.Count}");
    }

    private static void LoadAllowedIds()
    {
        AllowedIds.Clear();
        TextAsset asset = Resources.Load<TextAsset>(DevicesResourcePath);
        if (asset == null || string.IsNullOrEmpty(asset.text))
        {
            return;
        }

        DeveloperModeDeviceListJson list = JsonUtility.FromJson<DeveloperModeDeviceListJson>(asset.text);
        if (list?.devices == null)
        {
            return;
        }

        for (int i = 0; i < list.devices.Length; i++)
        {
            string[] ids = list.devices[i]?.ids;
            if (ids == null)
            {
                continue;
            }

            for (int j = 0; j < ids.Length; j++)
            {
                if (!string.IsNullOrWhiteSpace(ids[j]))
                {
                    AllowedIds.Add(ids[j].Trim());
                }
            }
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static string TryReadAndroidId()
    {
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null)
                {
                    return null;
                }

                using (AndroidJavaObject contentResolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (AndroidJavaClass secure = new AndroidJavaClass("android.provider.Settings$Secure"))
                {
                    return secure.CallStatic<string>("getString", contentResolver, "android_id");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DeveloperMode] android_id read failed: {ex.Message}");
            return null;
        }
    }
#endif
}
