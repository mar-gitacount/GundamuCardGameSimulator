using System;
using System.Collections.Generic;

[Serializable]
public class DeckSaveData
{
    public string title;
    public int thumbnailId;
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
}

[Serializable]
public class CloudDeckIndex
{
    public List<CloudDeckIndexEntry> entries = new List<CloudDeckIndexEntry>();
}
