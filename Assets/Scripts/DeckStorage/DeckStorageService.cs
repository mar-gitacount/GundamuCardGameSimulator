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

    public static DeckSaveData BuildSaveData(string title, Dictionary<int, int> cardData)
    {
        DeckSaveData saveData = new DeckSaveData
        {
            title = title,
            thumbnailId = 0,
        };

        foreach (KeyValuePair<int, int> item in cardData)
        {
            if (item.Value <= 0)
            {
                continue;
            }

            if (saveData.thumbnailId == 0)
            {
                saveData.thumbnailId = item.Key;
            }

            saveData.cards.Add(new CardSlot { id = item.Key, count = item.Value });
        }

        return saveData;
    }
}
