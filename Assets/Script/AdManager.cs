using UnityEngine;
using UnityEngine.UI;
using GoogleMobileAds;
using GoogleMobileAds.Api;
using System;
using System.Collections;

public class AdManager : MonoBehaviour
{
    public static AdManager Instance;
    public bool IsShowingAd => isShowingAd;
    // Ad References
    private BannerView bannerAd;
    private InterstitialAd interstitialAd;
    private RewardedAd rewardedAd;
    private AppOpenAd appOpenAd;

    [Header("Ad Unit IDs")]
    [SerializeField] private string androidBannerId = "ca-app-pub-3940256099942544/6300978111";
    [SerializeField] private string androidInterstitialId = "ca-app-pub-3940256099942544/1033173712";
    [SerializeField] private string androidRewardedId = "ca-app-pub-3940256099942544/5224354917";
    [SerializeField] private string androidAppOpenId = "ca-app-pub-3940256099942544/3419835294";

    [Header("UI Settings")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private float loadingDisplayTime = 0.5f;

    [Header("Configuration")]
    [SerializeField] private bool showAppOpenAds = true;
    [SerializeField] private float appOpenAdTimeout = 4f;

    private DateTime appOpenLoadTime;
    private bool isShowingAd = false;
    private bool appOpenAdEnabled = true;
  
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeMobileAds();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private void Start()
    {
        // Show on initial launch
        ShowAppOpenIfAvailable(() => { });
    }

    public bool IsInterstitialReady => interstitialAd != null && interstitialAd.CanShowAd();
    public bool IsRewardedReady => rewardedAd != null && rewardedAd.CanShowAd();
    private void InitializeMobileAds()
    {
        MobileAds.Initialize(initStatus =>
        {
            LoadAllAdFormats();
            if (showAppOpenAds) LoadAppOpenAdvertisement();
        });
    }

    private void LoadAllAdFormats()
    {
        InitializeBannerAd();
        LoadFullScreenAd();
        LoadIncentivizedAd();
    }

    #region Banner Ad Implementation
    private void InitializeBannerAd()
    {
        if (bannerAd != null) return;

        string adUnitId = GetPlatformAdId(androidBannerId, "");
        bannerAd = new BannerView(adUnitId, AdSize.Banner, AdPosition.Bottom);

        bannerAd.OnBannerAdLoaded += () =>
        {
            Debug.Log("Banner successfully loaded");
          //  CenterBannerPosition(); // <- ❌ REMOVE THIS LINE
        };

        bannerAd.OnBannerAdLoadFailed += (error) =>
        {
            Debug.LogError($"Banner Error: {error.GetMessage()}");
            Invoke(nameof(InitializeBannerAd), 10f);
        };

        bannerAd.LoadAd(new AdRequest());
    }



  /*  private void CenterBannerPosition()
    {
        int screenWidth = Screen.width;
        int bannerWidth = AdSize.Banner.Width;
        int xPosition = (screenWidth - bannerWidth) / 2;
        bannerAd.SetPosition(xPosition, 0);
    }*/

    public void DisplayBanner() => bannerAd?.Show();
    public void ConcealBanner() => bannerAd?.Hide();
    #endregion

    #region Interstitial Ad Implementation
    private void LoadFullScreenAd()
    {
        // ✅ Only skip loading if ad is still usable
        if (interstitialAd != null && interstitialAd.CanShowAd())
            return;

        string adUnitId = GetPlatformAdId(androidInterstitialId, "");

        InterstitialAd.Load(adUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogError($"Interstitial Error: {error?.GetMessage()}");
                return;
            }

            interstitialAd = ad;
            RegisterInterstitialEvents(); // ✅ Always re-register
        });
    }



    private void RegisterInterstitialEvents()
    {
        interstitialAd.OnAdFullScreenContentClosed += () =>
        {
            loadingPanel.SetActive(false);
            LoadFullScreenAd(); // ✅ This reloads after ad closes
            DisplayBanner();
        };

        interstitialAd.OnAdFullScreenContentFailed += (error) =>
        {
            loadingPanel.SetActive(false);
            LoadFullScreenAd(); // ✅ This reloads after error
            DisplayBanner();
        };

        interstitialAd.OnAdFullScreenContentOpened += () =>
        {
            loadingPanel.SetActive(false);
        };
    }


    public void DisplayInterstitialWithLoading()
    {
        if (interstitialAd != null && interstitialAd.CanShowAd())
        {
            StartCoroutine(ShowAdWithLoadingScreen(() => interstitialAd.Show()));
        }
        else
        {
            LoadFullScreenAd();
        }
    }
    #endregion

    #region Rewarded Ad Implementation
    private void LoadIncentivizedAd()
    {
        if (rewardedAd != null && rewardedAd.CanShowAd()) return;

        string adUnitId = GetPlatformAdId(androidRewardedId, "");

        RewardedAd.Load(adUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogError($"Rewarded Error: {error?.GetMessage()}");
                return;
            }

            rewardedAd = ad;
            RegisterRewardedEvents();
        });
    }


    private void RegisterRewardedEvents()
    {
        rewardedAd.OnAdFullScreenContentClosed += () =>
        {
            loadingPanel.SetActive(false);
            LoadIncentivizedAd();
        };

        rewardedAd.OnAdFullScreenContentFailed += (error) =>
        {
            loadingPanel.SetActive(false);
            LoadIncentivizedAd();
        };

        rewardedAd.OnAdFullScreenContentOpened += () =>
        {
            loadingPanel.SetActive(false);
        };
    }

    public void ShowRewardedAdvertisement(Action rewardAction, Action onNotReady = null)
    {
        if (rewardedAd != null && rewardedAd.CanShowAd())
        {
            StartCoroutine(ShowAdWithLoadingScreen(() =>
            {
                rewardedAd.Show((reward) =>
                {
                    rewardAction?.Invoke();
                });
            }));
        }
        else
        {
            Debug.LogWarning("Rewarded not ready");
            LoadIncentivizedAd(); // Try to load again
            onNotReady?.Invoke(); // 🔔 Callback for UI
        }
    }

    #endregion

    #region Common Ad Display Logic
    private IEnumerator ShowAdWithLoadingScreen(Action adShowAction)
    {
        loadingPanel.SetActive(true);
        yield return new WaitForSeconds(loadingDisplayTime);

        if (adShowAction.Target != null)
        {
            adShowAction.Invoke();
        }
        else
        {
            loadingPanel.SetActive(false);
        }
    }
    #endregion

    #region App Open Ad Implementation
    private void LoadAppOpenAdvertisement()
    {
        if (!appOpenAdEnabled) return;

        string adUnitId = GetPlatformAdId(androidAppOpenId, "");

        AppOpenAd.Load(adUnitId, new AdRequest(), (ad, error) =>
        {
            if (error != null || ad == null)
            {
                Debug.LogError($"App Open Error: {error?.GetMessage()}");
                return;
            }

            appOpenAd = ad;
            appOpenLoadTime = DateTime.Now;
            RegisterAppOpenEvents();
        });
    }

    private void RegisterAppOpenEvents()
    {
        appOpenAd.OnAdFullScreenContentClosed += () =>
        {
            isShowingAd = false;
            LoadAppOpenAdvertisement();
        };

        appOpenAd.OnAdFullScreenContentFailed += (error) =>
        {
            isShowingAd = false;
            LoadAppOpenAdvertisement();
        };
    }
    private void OnApplicationPause(bool isPaused)
    {
        // Show when app resumes from background
        if (!isPaused)
        {
            ShowAppOpenIfAvailable(() => { });
        }
    }
    public void ShowAppOpenIfAvailable(Action postAdAction)
    {
        Debug.Log($"Attempting to show app open ad. Ad is null: {appOpenAd == null}, isShowingAd: {isShowingAd}, isFresh: {IsAppOpenAdFresh()}");

        if (appOpenAd != null && !isShowingAd && IsAppOpenAdFresh())
        {
            // ⏱ Use timeout
            if ((DateTime.Now - appOpenLoadTime).TotalSeconds > appOpenAdTimeout)
            {
                Debug.Log("App open ad expired by timeout.");
                postAdAction?.Invoke();
                return;
            }

            isShowingAd = true;
            appOpenAd.Show();
            StartCoroutine(ExecuteAfterAdClose(postAdAction));
        }
        else
        {
            postAdAction?.Invoke();
        }
    }



    private IEnumerator ExecuteAfterAdClose(Action callback)
    {
        while (isShowingAd) yield return null;
        callback?.Invoke();
    }

    private bool IsAppOpenAdFresh() => (DateTime.Now - appOpenLoadTime).TotalHours < 4;
    #endregion

    #region Common Utilities
    private string GetPlatformAdId(string androidId, string iosId)
    {
#if UNITY_ANDROID
        return androidId;
#elif UNITY_IOS
            return iosId;
#else
            return "unsupported_platform";
#endif
    }
    #endregion

    public void ToggleAppOpenAds(bool enabled) => appOpenAdEnabled = enabled;
}