using System;
using System.Collections;
using UnityEngine;
using GoogleMobileAds.Api;

/// <summary>
/// AdMob（インタースティシャル＋ホームバナー）。
/// Android 実機および Editor で表示（Editor はプラグインのテスト実装）。
/// </summary>
public sealed class AdMobAdsService : MonoBehaviour
{
    public static AdMobAdsService Instance { get; private set; }

    private const string AndroidTestInterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";
    private const string AndroidTestBannerUnitId = "ca-app-pub-3940256099942544/6300978111";
    private const string AndroidProductionInterstitialUnitId = "ca-app-pub-2383157187090434/8567546228";
    private const string AndroidProductionBannerUnitId = "ca-app-pub-2383157187090434/3091375763";

    [SerializeField]
    [Tooltip("ONの間はテスト広告。確認できたらOFFで本番ユニット。")]
    private bool useTestAds = true;

    [SerializeField]
    private bool enableAds = true;

    [SerializeField]
    private float initializeDelaySeconds = 1f;

    private InterstitialAd _interstitialAd;
    private BannerView _bannerView;
    private bool _initializing;
    private bool _initialized;
    private bool _isLoadingInterstitial;
    private bool _initFailed;
    private bool _wantBannerVisible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_ANDROID || UNITY_EDITOR
        EnsureInstanceExists();
#endif
    }

    private static void EnsureInstanceExists()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject(nameof(AdMobAdsService));
        DontDestroyOnLoad(go);
        go.AddComponent<AdMobAdsService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (enableAds)
        {
            _wantBannerVisible = true;
            StartCoroutine(InitializeAfterDelay());
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        DestroyInterstitial();
        DestroyBanner();
    }

    public static void TryShowOnMatchEnd()
    {
        EnsureInstanceExists();
        if (Instance != null)
        {
            Instance.ShowInterstitialIfReady();
        }
    }

    /// <summary>ホーム（デッキ一覧など）でバナー表示。</summary>
    public static void ShowMenuBanner()
    {
        EnsureInstanceExists();
        if (Instance == null)
        {
            return;
        }

        Instance._wantBannerVisible = true;
        Debug.Log("[AdMob] ShowMenuBanner requested.");
        if (!Instance._initialized && !Instance._initializing && !Instance._initFailed)
        {
            Instance.StartCoroutine(Instance.InitializeAfterDelay());
            return;
        }

        Instance.EnsureBannerVisible();
    }

    /// <summary>バトル中はバナー非表示。</summary>
    public static void HideMenuBanner()
    {
        if (Instance == null)
        {
            return;
        }

        Instance._wantBannerVisible = false;
        Instance.HideBannerInternal();
    }

    private IEnumerator InitializeAfterDelay()
    {
        if (initializeDelaySeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(initializeDelaySeconds);
        }

        yield return null;
        InitializeAndPreload();
    }

    private void InitializeAndPreload()
    {
        if (!enableAds || _initializing || _initialized || _initFailed)
        {
            return;
        }

        _initializing = true;
        try
        {
            MobileAds.RaiseAdEventsOnUnityMainThread = true;
            Debug.Log($"[AdMob] Initialize start (testAds={useTestAds} editor={Application.isEditor})");
            MobileAds.Initialize(status =>
            {
                _initializing = false;
                if (status == null)
                {
                    _initFailed = true;
                    Debug.LogError("[AdMob] Initialize failed (null status).");
                    return;
                }

                _initialized = true;
                Debug.Log("[AdMob] Initialize OK.");
                LoadInterstitial();
                if (_wantBannerVisible)
                {
                    EnsureBannerVisible();
                }
            });
        }
        catch (Exception e)
        {
            _initializing = false;
            _initFailed = true;
            Debug.LogError($"[AdMob] Initialize exception: {e}");
        }
    }

    private string ResolveInterstitialUnitId()
    {
        return useTestAds ? AndroidTestInterstitialUnitId : AndroidProductionInterstitialUnitId;
    }

    private string ResolveBannerUnitId()
    {
        return useTestAds ? AndroidTestBannerUnitId : AndroidProductionBannerUnitId;
    }

    private void LoadInterstitial()
    {
        if (!enableAds || !_initialized || _isLoadingInterstitial || _initFailed)
        {
            return;
        }

        try
        {
            DestroyInterstitial();
            _isLoadingInterstitial = true;
            InterstitialAd.Load(ResolveInterstitialUnitId(), new AdRequest(), (ad, error) =>
            {
                _isLoadingInterstitial = false;
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[AdMob] Interstitial load failed: {error}");
                    return;
                }

                _interstitialAd = ad;
                ad.OnAdFullScreenContentClosed += () =>
                {
                    DestroyInterstitial();
                    LoadInterstitial();
                };
                ad.OnAdFullScreenContentFailed += _ =>
                {
                    DestroyInterstitial();
                    LoadInterstitial();
                };
                Debug.Log("[AdMob] Interstitial loaded.");
            });
        }
        catch (Exception e)
        {
            _isLoadingInterstitial = false;
            Debug.LogError($"[AdMob] Interstitial load exception: {e}");
        }
    }

    private void ShowInterstitialIfReady()
    {
        if (!enableAds || _initFailed)
        {
            return;
        }

        if (!_initialized)
        {
            if (!_initializing)
            {
                StartCoroutine(InitializeAfterDelay());
            }

            return;
        }

        try
        {
            if (_interstitialAd != null && _interstitialAd.CanShowAd())
            {
                _interstitialAd.Show();
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AdMob] Interstitial show exception: {e}");
            return;
        }

        LoadInterstitial();
    }

    private void EnsureBannerVisible()
    {
        if (!enableAds || _initFailed || !_initialized || !_wantBannerVisible)
        {
            Debug.Log($"[AdMob] EnsureBanner skip enable={enableAds} initFail={_initFailed} inited={_initialized} want={_wantBannerVisible}");
            return;
        }

        try
        {
            if (_bannerView == null)
            {
                AdSize size = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
                string unitId = ResolveBannerUnitId();
                Debug.Log($"[AdMob] Create banner (BOTTOM) unit={unitId}");
                _bannerView = new BannerView(unitId, size, AdPosition.Bottom);
                _bannerView.OnBannerAdLoaded += () =>
                {
                    Debug.Log("[AdMob] Banner LOADED — bottom of screen.");
                    if (_wantBannerVisible && _bannerView != null)
                    {
                        _bannerView.Show();
                    }
                };
                _bannerView.OnBannerAdLoadFailed += error =>
                {
                    Debug.LogWarning($"[AdMob] Banner load failed: {error}");
                    StartCoroutine(RetryBannerAfterDelay(8f));
                };
                _bannerView.LoadAd(new AdRequest());
            }

            _bannerView.Show();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AdMob] Banner exception: {e}");
        }
    }

    private IEnumerator RetryBannerAfterDelay(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (!_wantBannerVisible || _initFailed)
        {
            yield break;
        }

        DestroyBanner();
        EnsureBannerVisible();
    }

    private void HideBannerInternal()
    {
        if (_bannerView == null)
        {
            return;
        }

        try
        {
            _bannerView.Hide();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AdMob] Banner hide exception: {e}");
        }
    }

    private void DestroyInterstitial()
    {
        if (_interstitialAd == null)
        {
            return;
        }

        try
        {
            _interstitialAd.Destroy();
        }
        catch
        {
            // ignore
        }

        _interstitialAd = null;
    }

    private void DestroyBanner()
    {
        if (_bannerView == null)
        {
            return;
        }

        try
        {
            _bannerView.Destroy();
        }
        catch
        {
            // ignore
        }

        _bannerView = null;
    }
}
