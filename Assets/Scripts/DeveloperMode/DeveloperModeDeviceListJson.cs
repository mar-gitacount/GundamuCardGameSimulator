using System;

/// <summary>developer_mode_devices.json のルート。</summary>
[Serializable]
public class DeveloperModeDeviceListJson
{
    /// <summary>true のとき端末リストに関係なく開発者扱い。公開ビルドでは必ず false。</summary>
    public bool forceAuthorize;

    public DeveloperModeDeviceEntryJson[] devices = Array.Empty<DeveloperModeDeviceEntryJson>();
}

[Serializable]
public class DeveloperModeDeviceEntryJson
{
    public string label;
    public string[] ids = Array.Empty<string>();
}
