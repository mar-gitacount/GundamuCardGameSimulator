using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class LocalDeckStorageProvider : IDeckStorageProvider
{
    public bool IsCloud => false;

    public Task SaveDeckAsync(string storageKey, DeckSaveData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        string fullPath = ResolveFullPath(storageKey);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(fullPath, json);
        Debug.Log($"[DeckStorage][Local] 保存: {fullPath}");
        return Task.CompletedTask;
    }

    public Task<DeckSaveData> LoadDeckAsync(string storageKey)
    {
        string fullPath = ResolveFullPath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Deck file not found: {fullPath}");
        }

        string json = File.ReadAllText(fullPath);
        DeckSaveData data = JsonUtility.FromJson<DeckSaveData>(json);
        return Task.FromResult(data);
    }

    public Task DeleteDeckAsync(string storageKey)
    {
        string fullPath = ResolveFullPath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Debug.Log($"[DeckStorage][Local] 削除: {fullPath}");
        }

        return Task.CompletedTask;
    }

    public Task<List<DeckStorageEntry>> ListDecksAsync()
    {
        List<DeckStorageEntry> entries = new List<DeckStorageEntry>();
        string path = Application.persistentDataPath;
        if (!Directory.Exists(path))
        {
            return Task.FromResult(entries);
        }

        string[] files = Directory.GetFiles(path, "Deck_*.json");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        Array.Reverse(files);

        for (int i = 0; i < files.Length; i++)
        {
            string fullPath = files[i];
            string fileName = Path.GetFileName(fullPath);
            string title = fileName;
            DateTime lastWrite = File.GetLastWriteTime(fullPath);
            try
            {
                DeckSaveData data = JsonUtility.FromJson<DeckSaveData>(File.ReadAllText(fullPath));
                if (data != null && !string.IsNullOrEmpty(data.title))
                {
                    title = data.title;
                }

                if (data != null && data.updatedAtUnix > 0)
                {
                    lastWrite = DateTimeOffset.FromUnixTimeSeconds(data.updatedAtUnix).LocalDateTime;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DeckStorage][Local] 一覧読込失敗 {fullPath}: {e.Message}");
            }

            entries.Add(new DeckStorageEntry(fullPath, title, false, lastWrite));
        }

        return Task.FromResult(entries);
    }

    public string CreateNewStorageKey()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = "Deck_" + timestamp + ".json";
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    private static string ResolveFullPath(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
        {
            throw new ArgumentException("storageKey is empty.");
        }

        if (Path.IsPathRooted(storageKey))
        {
            return storageKey;
        }

        return Path.Combine(Application.persistentDataPath, storageKey);
    }
}
