public struct DeckStorageEntry
{
    public string StorageKey;
    public string DisplayName;
    public bool IsCloud;

    public DeckStorageEntry(string storageKey, string displayName, bool isCloud)
    {
        StorageKey = storageKey;
        DisplayName = displayName;
        IsCloud = isCloud;
    }
}
