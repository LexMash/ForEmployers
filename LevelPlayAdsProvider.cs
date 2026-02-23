using Core.Ads.Data;
using Core.Ads.Delegates;
using Core.Services.Delegates;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Services.LevelPlay;
using UnityEngine;
using AdInfo = Core.Ads.Data.AdInfo;

public class LevelPlayAdsProvider : MonoBehaviour, IAdsProvider
{
    [SerializeField] private LevelPlayAdConfig config;

    private readonly Dictionary<AdType, Func<bool>> availableMap = new();

    private IAdsStatusProvider statusProvider;
    private IAnalyticsService analyticsService;
    private LevelPlayInterstitialAd interstitialAd;
    private bool isAdShowingNow;
    private bool isInitialized;
    private int retryCounter;
    private bool interstitialLoaded;

    public IObservable<bool> IsAdsActive => statusProvider.IsAdsActive;
    public bool IsAdShowingNow => isAdShowingNow;

    public event InitializationSuccessDelegate InitializationSucceed;
    public event AdEventDelegate AdAvailabilityChanged;
    public event AdEventDelegate AdFailed;
    public event AdEventDelegate AdOpened;
    public event AdEventDelegate AdClosed;

    public void Initialize()
    {
        IGameServiceProvider serviceProvider = GameManager.Instance.ServiceProvider;
        statusProvider = serviceProvider.GetService<IAdsStatusProvider>();
        analyticsService = serviceProvider.GetService<IAnalyticsService>();

        LevelPlay.OnInitSuccess += SdkInitializationCompletedEvent;
        LevelPlay.OnInitFailed += SdkInitializationFailedEvent;
        LevelPlay.SetConsent(false);
        LevelPlay.SetMetaData("is_child_directed", "false");
        LevelPlay.Init(config.AppKey);
    }

    public bool IsAdAvailable(AdType type)
    {
        if (isInitialized &&
            statusProvider.IsAdsActive.Value &&
            !isAdShowingNow &&
            availableMap.TryGetValue(type, out Func<bool> isAvailable))
            return isAvailable();

        return false;
    }

    public void Show(AdType type, string placement = "")
    {
        switch (type) //TODO refactoring
        {
            case AdType.Interstitial:
                interstitialAd.ShowAd(placement);
                break;
        }

        isAdShowingNow = true;
        AdOpened?.Invoke(new AdInfo(type, placement));
    }

    private void SdkInitializationCompletedEvent(LevelPlayConfiguration configuration)
    {
        isInitialized = true;
        InitializeInterstitial();
        InitializationSucceed?.Invoke();
        Debug.Log($"[ADS] {nameof(LevelPlayAdsProvider)} Initialization completed.");
    }

    private void SdkInitializationFailedEvent(LevelPlayInitError error)
    {
        isInitialized = false;
        Debug.LogError($"[ADS] {nameof(LevelPlayAdsProvider)} Initialization failed.");
    }

    private void InitializeInterstitial()
    {
        interstitialAd = new LevelPlayInterstitialAd(config.InterstitialId);
        availableMap.Add(AdType.Interstitial, interstitialAd.IsAdReady);

        interstitialAd.OnAdLoaded += InterstitialOnAdLoadedEvent;
        interstitialAd.OnAdLoadFailed += InterstitialOnAdLoadFailedEvent;
        interstitialAd.OnAdDisplayed += InterstitialOnAdDisplayedEvent;
        interstitialAd.OnAdDisplayFailed += InterstitialOnAdDisplayFailedEvent;
        interstitialAd.OnAdClicked += InterstitialOnAdClickedEvent;
        interstitialAd.OnAdClosed += InterstitialOnAdClosedEvent;

        LoadInterstitial();
    }

    private void UnsubscribeFromInterstitial()
    {
        if (interstitialAd == null)
            return;

        interstitialAd.OnAdLoaded -= InterstitialOnAdLoadedEvent;
        interstitialAd.OnAdLoadFailed -= InterstitialOnAdLoadFailedEvent;
        interstitialAd.OnAdDisplayed -= InterstitialOnAdDisplayedEvent;
        interstitialAd.OnAdDisplayFailed -= InterstitialOnAdDisplayFailedEvent;
        interstitialAd.OnAdClicked -= InterstitialOnAdClickedEvent;
        interstitialAd.OnAdClosed -= InterstitialOnAdClosedEvent;
    }

    private void InterstitialOnAdLoadedEvent(LevelPlayAdInfo info)
    {
        Debug.Log($"[ADS] {nameof(LevelPlayAdsProvider)} Interstitial loaded {info.AdUnitId}, {info.AdNetwork} - {info.PlacementName}");
        interstitialLoaded = true;
        retryCounter = 0;
        AdAvailabilityChanged?.Invoke(new AdInfo(AdType.Interstitial, info.PlacementName));
    }

    private void InterstitialOnAdLoadFailedEvent(LevelPlayAdError error)
    {
        Debug.LogWarning($"[ADS] {nameof(LevelPlayAdsProvider)} Interstitial load failed. Retries {retryCounter} - {error.AdUnitId} - {error.ErrorCode}, {error.ErrorMessage}");
        interstitialLoaded = false;
        if (retryCounter == 0)
            RetryInterstitialLoading().SuppressCancellationThrow().Forget();
    }

    private void InterstitialOnAdDisplayedEvent(LevelPlayAdInfo info)
    {
        analyticsService.Track(new AdImpressionAnalyticsEvent(info));
        Debug.Log($"[ADS] {nameof(LevelPlayAdsProvider)} Interstitial displayed {info.AdUnitId}, {info.AdNetwork} - {info.PlacementName}");
    }

    private void InterstitialOnAdDisplayFailedEvent(LevelPlayAdInfo adInfo, LevelPlayAdError error)
    {
        Debug.LogWarning($"[ADS] {nameof(LevelPlayAdsProvider)} Interstitial display failed {adInfo.AdUnitId}, {error.ErrorCode} {error.ErrorMessage}");
        LoadInterstitial();
        AdInfo info = new(AdType.Interstitial, adInfo.AdNetwork);
        isAdShowingNow = false;
        AdFailed?.Invoke(info);
        AdClosed?.Invoke(info);
    }

    private void InterstitialOnAdClickedEvent(LevelPlayAdInfo info)
        => Debug.Log($"[ADS] {nameof(LevelPlayAdsProvider)} Interstitial clicked {info.AdUnitName}, {info.AdNetwork} - {info.PlacementName}");

    private void InterstitialOnAdClosedEvent(LevelPlayAdInfo info)
    {
        Debug.Log($"[ADS] {nameof(LevelPlayAdsProvider)} Interstitial closed. {info.AdUnitName} - {info.PlacementName} {info.AdNetwork}");
        isAdShowingNow = false;
        AdClosed?.Invoke(new AdInfo(AdType.Interstitial, info.PlacementName));
        LoadInterstitial();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LoadInterstitial() => interstitialAd.LoadAd();

    private async UniTask RetryInterstitialLoading()
    {
        TimeSpan delay = TimeSpan.FromSeconds(10);

        while (!interstitialLoaded && !destroyCancellationToken.IsCancellationRequested)
        {
            bool canceled = await UniTask
                .Delay(delay, true, cancellationToken: destroyCancellationToken)
                .SuppressCancellationThrow();

            if (canceled)
                return;

            retryCounter++;
            LoadInterstitial();
        }
    }

    private void OnDestroy()
    {
        availableMap.Clear();
        UnsubscribeFromInterstitial();
        interstitialAd?.Dispose();

        LevelPlay.OnInitSuccess -= SdkInitializationCompletedEvent;
        LevelPlay.OnInitFailed -= SdkInitializationFailedEvent;
    }
}
