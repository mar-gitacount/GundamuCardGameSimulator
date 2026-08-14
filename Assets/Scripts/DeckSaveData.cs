using System;
using System.Collections.Generic;

[Serializable]
public class DeckSaveData
{
    public string title;
    public int thumbnailId;
    /// <summary>最終保存時刻（Unix 秒）。未設定は 0。</summary>
    public long updatedAtUnix;
    public List<CardSlot> cards = new List<CardSlot>();
}

[Serializable]
public class CardSlot
{
    public int id;
    public int count;
}

[Serializable]
public class CloudDeckIndexEntry
{
    public string storageKey;
    public string title;
    public int thumbnailId;
    public long lastSavedUnix;
}

[Serializable]
public class CloudDeckIndex
{
    public List<CloudDeckIndexEntry> entries = new List<CloudDeckIndexEntry>();
}
