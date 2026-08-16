using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.UI;

/// <summary>
/// カード画像を Addressables（Local）から読み込む。
/// imageAddress に加え、登録済みキー一覧から id / 名前で照合する。
/// </summary>
public static class CardSpriteLoader
{
    public const string DefaultImageFolderPrefix = "Data/Images/";

    private static bool _catalogReady;
    private static bool _catalogLoading;
    private static readonly List<string> s_allImageAddresses = new List<string>(256);
    private static readonly Dictionary<int, string> s_addressByCardId = new Dictionary<int, string>(256);
    private static readonly List<Action> s_catalogWaiters = new List<Action>();

    public static Sprite ResolveEmbeddedSprite(CardData cardData)
    {
        if (cardData == null)
        {
            return null;
        }

        if (cardData.imageName != null)
        {
            return cardData.imageName;
        }

        if (cardData.image != null)
        {
            return cardData.image;
        }

        return null;
    }

    public static Sprite ResolveEmbeddedSpriteByCardId(int cardId)
    {
        return ResolveEmbeddedSprite(FindCardData(cardId));
    }

    public static CardData FindCardData(int cardId)
    {
        if (cardId <= 0)
        {
            return null;
        }

        if (CardDatabase.Instance != null)
        {
            CardData data = CardDatabase.Instance.FindById(cardId);
            if (data != null)
            {
                return data;
            }
        }

        CardData[] all = Resources.LoadAll<CardData>("Data/Cards");
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].id == cardId)
            {
                return all[i];
            }
        }

        return null;
    }

    public static string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        return address.Trim().Trim('"').Trim('\'');
    }

    /// <summary>Addressables 初期化後に Data/Images/* のカタログを構築する。</summary>
    public static void EnsureCatalog(Action onReady)
    {
        if (_catalogReady)
        {
            onReady?.Invoke();
            return;
        }

        if (onReady != null)
        {
            s_catalogWaiters.Add(onReady);
        }

        if (_catalogLoading)
        {
            return;
        }

        _catalogLoading = true;
        AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> init =
            Addressables.InitializeAsync();

        void BuildAndNotify()
        {
            RebuildCatalogFromLocators();
            _catalogReady = true;
            _catalogLoading = false;
            List<Action> waiters = new List<Action>(s_catalogWaiters);
            s_catalogWaiters.Clear();
            for (int i = 0; i < waiters.Count; i++)
            {
                waiters[i]?.Invoke();
            }
        }

        if (init.IsDone)
        {
            BuildAndNotify();
            return;
        }

        init.Completed += _ => BuildAndNotify();
    }

    private static void RebuildCatalogFromLocators()
    {
        s_allImageAddresses.Clear();
        s_addressByCardId.Clear();

        foreach (var locator in Addressables.ResourceLocators)
        {
            if (locator == null)
            {
                continue;
            }

            foreach (object keyObj in locator.Keys)
            {
                string key = keyObj as string;
                if (string.IsNullOrEmpty(key) ||
                    !key.StartsWith(DefaultImageFolderPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!s_allImageAddresses.Contains(key))
                {
                    s_allImageAddresses.Add(key);
                }

                string leaf = key.Substring(DefaultImageFolderPrefix.Length);
                int us = leaf.IndexOf('_');
                if (us <= 0)
                {
                    continue;
                }

                if (int.TryParse(leaf.Substring(0, us), out int id) && id > 0 &&
                    !s_addressByCardId.ContainsKey(id))
                {
                    s_addressByCardId[id] = key;
                }
            }
        }

        Debug.Log($"[CardSpriteLoader] Addressables 画像カタログ: {s_allImageAddresses.Count} 件");
    }

    /// <summary>Addressables で試し得るアドレス候補（重複なし）。</summary>
    public static List<string> BuildAddressCandidates(CardData cardData)
    {
        var list = new List<string>(8);
        if (cardData == null)
        {
            return list;
        }

        void Add(string raw)
        {
            string addr = NormalizeAddress(raw);
            if (string.IsNullOrEmpty(addr))
            {
                return;
            }

            if (!addr.StartsWith(DefaultImageFolderPrefix, StringComparison.Ordinal))
            {
                addr = DefaultImageFolderPrefix + addr;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], addr, StringComparison.Ordinal))
                {
                    return;
                }
            }

            list.Add(addr);
        }

        // 1) CardData.imageAddress
        if (!string.IsNullOrWhiteSpace(cardData.imageAddress))
        {
            Add(cardData.imageAddress);
        }

        // 2) カタログ: カード ID 先頭一致（90_Kayra''s Re-GZ など）
        if (_catalogReady && cardData.id > 0 &&
            s_addressByCardId.TryGetValue(cardData.id, out string byId))
        {
            Add(byId);
        }

        // 3) カタログ: カード名を含む／末尾一致（63_Tallgeese, RewloolaRewloola）
        if (_catalogReady && !string.IsNullOrWhiteSpace(cardData.cardName))
        {
            string name = cardData.cardName.Trim();
            string bestExact = null;
            string bestEndsWith = null;
            string bestContains = null;

            for (int i = 0; i < s_allImageAddresses.Count; i++)
            {
                string addr = s_allImageAddresses[i];
                string leaf = addr.Substring(DefaultImageFolderPrefix.Length);
                if (string.Equals(leaf, name, StringComparison.OrdinalIgnoreCase))
                {
                    bestExact = addr;
                    break;
                }

                if (leaf.EndsWith("_" + name, StringComparison.OrdinalIgnoreCase) ||
                    leaf.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (bestEndsWith == null)
                    {
                        bestEndsWith = addr;
                    }
                }
                else if (leaf.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (bestContains == null)
                    {
                        bestContains = addr;
                    }
                }
            }

            if (bestExact != null)
            {
                Add(bestExact);
            }

            if (bestEndsWith != null)
            {
                Add(bestEndsWith);
            }

            if (bestContains != null)
            {
                Add(bestContains);
            }
        }

        // 4) 従来フォールバック
        if (!string.IsNullOrWhiteSpace(cardData.cardName))
        {
            string name = cardData.cardName.Trim();
            Add(DefaultImageFolderPrefix + name);
            if (cardData.id > 0)
            {
                Add(DefaultImageFolderPrefix + cardData.id + "_" + name);
            }
        }

        if (!string.IsNullOrWhiteSpace(cardData.imageAddress))
        {
            string primary = NormalizeAddress(cardData.imageAddress);
            if (primary.StartsWith(DefaultImageFolderPrefix, StringComparison.Ordinal))
            {
                string leaf = primary.Substring(DefaultImageFolderPrefix.Length);
                int us = leaf.IndexOf('_');
                if (us > 0 && int.TryParse(leaf.Substring(0, us), out _))
                {
                    Add(DefaultImageFolderPrefix + leaf.Substring(us + 1));
                }
                else if (cardData.id > 0)
                {
                    Add(DefaultImageFolderPrefix + cardData.id + "_" + leaf);
                }
            }
        }

        return list;
    }

    public static bool TryResolveAddress(CardData cardData, out string address)
    {
        List<string> candidates = BuildAddressCandidates(cardData);
        if (candidates.Count == 0)
        {
            address = null;
            return false;
        }

        address = candidates[0];
        return true;
    }

    public static void ApplyToImage(Image image, CardData cardData)
    {
        if (image == null)
        {
            return;
        }

        Sprite embedded = ResolveEmbeddedSprite(cardData);
        if (embedded != null)
        {
            image.sprite = embedded;
        }

        CardImageAddressableBinder binder = image.GetComponent<CardImageAddressableBinder>();
        if (binder == null)
        {
            binder = image.gameObject.AddComponent<CardImageAddressableBinder>();
        }

        binder.Bind(cardData);
    }

    public static void ApplyToImage(Image image, int cardId)
    {
        ApplyToImage(image, FindCardData(cardId));
    }

    public static void LoadFirstRegisteredAsync(List<string> candidates, Action<AsyncOperationHandle<Sprite>> onCompleted)
    {
        if (onCompleted == null)
        {
            return;
        }

        EnsureCatalog(() =>
        {
            if (candidates == null || candidates.Count == 0)
            {
                onCompleted(default);
                return;
            }

            TryLoadCandidateAt(candidates, 0, onCompleted);
        });
    }

    /// <summary>カタログ構築後に候補を組み直してロードする。</summary>
    public static void LoadForCardAsync(CardData cardData, Action<AsyncOperationHandle<Sprite>> onCompleted)
    {
        if (onCompleted == null)
        {
            return;
        }

        EnsureCatalog(() =>
        {
            List<string> candidates = BuildAddressCandidates(cardData);
            if (candidates.Count == 0)
            {
                onCompleted(default);
                return;
            }

            TryLoadCandidateAt(candidates, 0, onCompleted);
        });
    }

    private static void TryLoadCandidateAt(
        List<string> candidates,
        int index,
        Action<AsyncOperationHandle<Sprite>> onCompleted)
    {
        if (index >= candidates.Count)
        {
            Debug.LogWarning(
                "[CardSpriteLoader] Addressables 未登録（候補すべて失敗）: " + string.Join(" | ", candidates));
            onCompleted(default);
            return;
        }

        string address = candidates[index];
        AsyncOperationHandle<IList<IResourceLocation>> locHandle =
            Addressables.LoadResourceLocationsAsync(address);

        locHandle.Completed += op =>
        {
            bool found = op.Status == AsyncOperationStatus.Succeeded
                && op.Result != null
                && op.Result.Count > 0;

            if (op.IsValid())
            {
                Addressables.Release(op);
            }

            if (!found)
            {
                TryLoadCandidateAt(candidates, index + 1, onCompleted);
                return;
            }

            AsyncOperationHandle<Sprite> loadHandle = Addressables.LoadAssetAsync<Sprite>(address);
            if (!loadHandle.IsValid())
            {
                TryLoadCandidateAt(candidates, index + 1, onCompleted);
                return;
            }

            if (loadHandle.IsDone)
            {
                if (loadHandle.Status != AsyncOperationStatus.Succeeded || loadHandle.Result == null)
                {
                    Release(loadHandle);
                    TryLoadCandidateAt(candidates, index + 1, onCompleted);
                    return;
                }

                onCompleted(loadHandle);
                return;
            }

            loadHandle.Completed += completed =>
            {
                if (completed.Status != AsyncOperationStatus.Succeeded || completed.Result == null)
                {
                    Release(completed);
                    TryLoadCandidateAt(candidates, index + 1, onCompleted);
                    return;
                }

                onCompleted(completed);
            };
        };
    }

    public static void LoadIfRegisteredAsync(string address, Action<AsyncOperationHandle<Sprite>> onCompleted)
    {
        LoadFirstRegisteredAsync(new List<string> { NormalizeAddress(address) }, onCompleted);
    }

    public static void Release(AsyncOperationHandle<Sprite> handle)
    {
        if (handle.IsValid())
        {
            Addressables.Release(handle);
        }
    }
}

[DisallowMultipleComponent]
public sealed class CardImageAddressableBinder : MonoBehaviour
{
    private AsyncOperationHandle<Sprite> _handle;
    private int _generation;
    private Image _image;

    public void Bind(CardData cardData)
    {
        ReleaseHandle();
        _generation++;
        int generation = _generation;
        _image = GetComponent<Image>();

        if (_image == null || cardData == null)
        {
            return;
        }

        CardSpriteLoader.LoadForCardAsync(cardData, handle =>
        {
            if (this == null || generation != _generation)
            {
                CardSpriteLoader.Release(handle);
                return;
            }

            if (!handle.IsValid())
            {
                return;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                CardSpriteLoader.Release(handle);
                return;
            }

            _handle = handle;
            if (_image != null)
            {
                _image.sprite = handle.Result;
                _image.enabled = true;
                Color c = _image.color;
                if (c.a < 0.01f)
                {
                    c.a = 1f;
                    _image.color = c;
                }
            }
        });
    }

    private void OnDestroy()
    {
        ReleaseHandle();
    }

    private void ReleaseHandle()
    {
        CardSpriteLoader.Release(_handle);
        _handle = default;
    }
}
