using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class DeckStorageService
{
    private static readonly LocalDeckStorageProvider LocalProvider = new LocalDeckStorageProvider();
    private static readonly CloudDeckStorageProvider CloudProvider = new CloudDeckStorageProvider();

    public static IDeckStorageProvider ActiveProvider =>
        PlayerAuthService.Instance != null && PlayerAuthService.Instance.UseCloudStorage
            ? CloudProvider
            : LocalProvider;

    public static bool IsUsingCloudStorage => ActiveProvider.IsCloud;

    public static Task SaveDeckAsync(string storageKey, DeckSaveData data)
    {
        return ActiveProvider.SaveDeckAsync(storageKey, data);
    }

    public static Task<DeckSaveData> LoadDeckAsync(string storageKey)
    {
        return ActiveProvider.LoadDeckAsync(storageKey);
    }

    public static Task DeleteDeckAsync(string storageKey)
    {
        return ActiveProvider.DeleteDeckAsync(storageKey);
    }

    public static Task<List<DeckStorageEntry>> ListDecksAsync()
    {
        return ActiveProvider.ListDecksAsync();
    }

    public static string CreateNewStorageKey()
    {
        return ActiveProvider.CreateNewStorageKey();
    }

    public static string PrepareStorageKeyForSave(string storageKey)
    {
        if (IsUsingCloudStorage)
        {
            return CloudProvider.ResolveCloudKey(storageKey);
        }

        if (string.IsNullOrEmpty(storageKey))
        {
            return CreateNewStorageKey();
        }

        return storageKey;
    }

    public static string FormatStorageError(Exception exception)
    {
        if (exception == null)
        {
            return "Unknown error.";
        }

        if (IsUsingCloudStorage)
        {
            return CloudDeckStorageProvider.FormatStorageError(exception);
        }

        return exception.GetBaseException().Message;
    }

    public static DeckSaveData BuildSaveData(string title, Dictionary<int, int> cardData, int preferredThumbnailId = 0)
    {
        DeckSaveData saveData = new DeckSaveData
        {
            title = title,
            thumbnailId = 0,
            updatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        foreach (KeyValuePair<int, int> item in cardData)
        {
            if (item.Value <= 0)
            {
                continue;
            }

            saveData.cards.Add(new CardSlot { id = item.Key, count = item.Value });
        }

        saveData.thumbnailId = ResolveThumbnailId(cardData, preferredThumbnailId);
        return saveData;
    }

    /// <summary>指定 ID がデッキ内にあればそれを、なければ先頭の残カードをサムネにする。</summary>
    public static int ResolveThumbnailId(Dictionary<int, int> cardData, int preferredThumbnailId)
    {
        if (cardData != null
            && preferredThumbnailId > 0
            && cardData.TryGetValue(preferredThumbnailId, out int preferredCount)
            && preferredCount > 0)
        {
            return preferredThumbnailId;
        }

        if (cardData == null)
        {
            return 0;
        }

        foreach (KeyValuePair<int, int> item in cardData)
        {
            if (item.Value > 0)
            {
                return item.Key;
            }
        }

        return 0;
    }
}
