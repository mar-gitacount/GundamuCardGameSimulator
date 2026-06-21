using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.CloudSave;

public class CloudDeckStorageProvider : IDeckStorageProvider
{
    private const string DeckIndexKey = "deck_index";

    public bool IsCloud => true;

    public static bool IsValidCloudKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 255)
        {
            return false;
        }

        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public static string ToCloudKey(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
        {
            return string.Empty;
        }

        if (storageKey.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase))
        {
            return storageKey.Substring("cloud:".Length);
        }

        return storageKey;
    }

    public static string FormatStorageError(Exception exception)
    {
        if (exception == null)
        {
            return "Unknown error.";
        }

        Exception root = exception.GetBaseException();
        if (root is CloudSaveValidationException validation && validation.Details != null && validation.Details.Count > 0)
        {
            StringBuilder builder = new StringBuilder(root.Message);
            for (int i = 0; i < validation.Details.Count; i++)
            {
                CloudSaveValidationErrorDetail detail = validation.Details[i];
                builder.Append(" | ");
                if (!string.IsNullOrEmpty(detail.Key))
                {
                    builder.Append("key=").Append(detail.Key).Append(' ');
                }

                if (!string.IsNullOrEmpty(detail.Field))
                {
                    builder.Append("field=").Append(detail.Field).Append(' ');
                }

                if (detail.Messages != null && detail.Messages.Count > 0)
                {
                    builder.Append(string.Join(", ", detail.Messages));
                }
            }

            return builder.ToString();
        }

        return root.Message;
    }

    public async Task SaveDeckAsync(string storageKey, DeckSaveData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        string key = ResolveCloudKey(storageKey);
        string json = JsonUtility.ToJson(data, true);
        await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
        {
            { key, json },
        });

        CloudDeckIndex index = await LoadIndexAsync();
        CloudDeckIndexEntry existing = index.entries.FirstOrDefault(e => e.storageKey == key);
        if (existing == null)
        {
            index.entries.Add(new CloudDeckIndexEntry
            {
                storageKey = key,
                title = data.title,
                thumbnailId = data.thumbnailId,
            });
        }
        else
        {
            existing.title = data.title;
            existing.thumbnailId = data.thumbnailId;
        }

        await SaveIndexAsync(index);
        Debug.Log($"[DeckStorage][Cloud] Saved: {key} title:{data.title}");
    }

    public string ResolveCloudKey(string storageKey)
    {
        string key = ToCloudKey(storageKey);
        if (IsValidCloudKey(key))
        {
            return key;
        }

        string newKey = CreateNewStorageKey();
        Debug.LogWarning($"[DeckStorage][Cloud] Invalid key '{storageKey}'. Using new cloud key '{newKey}'.");
        return newKey;
    }

    public async Task<DeckSaveData> LoadDeckAsync(string storageKey)
    {
        string key = ToCloudKey(storageKey);
        if (!IsValidCloudKey(key))
        {
            throw new KeyNotFoundException($"Invalid cloud deck key: {storageKey}");
        }

        var result = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { key });
        if (!result.TryGetValue(key, out var item))
        {
            throw new KeyNotFoundException($"Cloud deck not found: {key}");
        }

        string json = item.Value.GetAsString();
        DeckSaveData data = JsonUtility.FromJson<DeckSaveData>(json);
        if (data == null)
        {
            throw new InvalidOperationException($"Failed to parse cloud deck: {key}");
        }

        return data;
    }

    public async Task DeleteDeckAsync(string storageKey)
    {
        string key = ToCloudKey(storageKey);
        if (!IsValidCloudKey(key))
        {
            return;
        }

        await CloudSaveService.Instance.Data.Player.DeleteAsync(key);

        CloudDeckIndex index = await LoadIndexAsync();
        index.entries.RemoveAll(e => e.storageKey == key);
        await SaveIndexAsync(index);
        Debug.Log($"[DeckStorage][Cloud] 削除: {key}");
    }

    public async Task<List<DeckStorageEntry>> ListDecksAsync()
    {
        CloudDeckIndex index = await LoadIndexAsync();
        List<DeckStorageEntry> entries = new List<DeckStorageEntry>(index.entries.Count);
        for (int i = 0; i < index.entries.Count; i++)
        {
            CloudDeckIndexEntry entry = index.entries[i];
            if (entry == null || string.IsNullOrEmpty(entry.storageKey))
            {
                continue;
            }

            if (!IsValidCloudKey(entry.storageKey))
            {
                continue;
            }

            string title = string.IsNullOrEmpty(entry.title) ? entry.storageKey : entry.title;
            entries.Add(new DeckStorageEntry(entry.storageKey, title, true));
        }

        return entries;
    }

    public string CreateNewStorageKey()
    {
        return "deck_" + Guid.NewGuid().ToString("N");
    }

    private async Task<CloudDeckIndex> LoadIndexAsync()
    {
        var result = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { DeckIndexKey });
        if (!result.TryGetValue(DeckIndexKey, out var item))
        {
            return new CloudDeckIndex();
        }

        string json = item.Value.GetAsString();
        if (string.IsNullOrEmpty(json))
        {
            return new CloudDeckIndex();
        }

        CloudDeckIndex index = JsonUtility.FromJson<CloudDeckIndex>(json);
        if (index == null || index.entries == null)
        {
            return new CloudDeckIndex();
        }

        return index;
    }

    private Task SaveIndexAsync(CloudDeckIndex index)
    {
        if (index == null)
        {
            index = new CloudDeckIndex();
        }

        if (index.entries == null)
        {
            index.entries = new List<CloudDeckIndexEntry>();
        }

        string json = JsonUtility.ToJson(index, true);
        return CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
        {
            { DeckIndexKey, json },
        });
    }
}
