using System;

/// <summary>developer_mode_devices.json のルート。</summary>
[Serializable]
public class DeveloperModeDeviceListJson
{
    public DeveloperModeDeviceEntryJson[] devices = Array.Empty<DeveloperModeDeviceEntryJson>();
}

[Serializable]
public class DeveloperModeDeviceEntryJson
{
    public string label;
    public string[] ids = Array.Empty<string>();
}
