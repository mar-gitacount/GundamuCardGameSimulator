using System;

public struct DeckStorageEntry
{
    public string StorageKey;
    public string DisplayName;
    public bool IsCloud;
    public DateTime LastWriteTime;

    public DeckStorageEntry(string storageKey, string displayName, bool isCloud)
        : this(storageKey, displayName, isCloud, DateTime.MinValue)
    {
    }

    public DeckStorageEntry(string storageKey, string displayName, bool isCloud, DateTime lastWriteTime)
    {
        StorageKey = storageKey;
        DisplayName = displayName;
        IsCloud = isCloud;
        LastWriteTime = lastWriteTime;
    }
}
