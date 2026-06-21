using System.Collections.Generic;
using System.Threading.Tasks;

public interface IDeckStorageProvider
{
    bool IsCloud { get; }

    Task SaveDeckAsync(string storageKey, DeckSaveData data);

    Task<DeckSaveData> LoadDeckAsync(string storageKey);

    Task DeleteDeckAsync(string storageKey);

    Task<List<DeckStorageEntry>> ListDecksAsync();

    string CreateNewStorageKey();
}
