using Core.Purchases.Data;
using Core.Purchases.Delegates;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UniRx;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;
using UnityEngine.Purchasing;

public class UnityInAppProvider : IInAppProvider, IDisposable, IPurchaseRestorer
{
    private const string EnvironmentName = "production";

    private readonly Dictionary<string, (InAppProductInfo gameData, Product defaultData)> productsMap = new(20);
    private readonly List<InAppProductDefinition> initialProductsToFetch;
    private readonly ReactiveProperty<ServiceState> currentState;
    private readonly List<string> purchaseIds = new(10);
    private readonly IAnalyticsService analyticsService;
    private StoreController storeController;
    private bool isRestoring = false;

    public event ProductIdsListEventDelegate PurchasesFetched;
    public event Action RestoringFailed;

    public event ProductEventDelegate PurchaseStarted;
    public event ProductEventDelegate PurchaseFailed;
    public event ProductEventDelegate PurchaseCompleted;

    public IReadOnlyReactiveProperty<ServiceState> CurrentState => currentState;
    public IReadOnlyList<InAppProductInfo> Products { get; private set; }
    public IReadOnlyList<string> PurchaseIds => purchaseIds;

    public UnityInAppProvider(
        List<InAppProductDefinition> initialProductsToFetch,
        IAnalyticsService analyticsService)
    {
        this.initialProductsToFetch = initialProductsToFetch.ToList();
        currentState = new ReactiveProperty<ServiceState>(ServiceState.None);
        this.analyticsService = analyticsService;
    }

    public void BuyProduct(string productId)
    {
        if (productsMap.TryGetValue(productId, out (InAppProductInfo gameData, Product defaultData) product))
        {
            storeController.PurchaseProduct(product.defaultData);
            PurchaseStarted?.Invoke(product.gameData);
        }
        else
        {
            PurchaseFailed?.Invoke(null);
        }
    }

    public bool TryGetProductInfo(string productId, out InAppProductInfo info)
    {
        info = null;

        if (productsMap.TryGetValue(productId, out (InAppProductInfo customData, Product defaultData) productData))
        {
            info = productData.customData;
            return true;
        }

        Debug.LogWarning($"[IAP] {nameof(UnityInAppProvider)} - Products list doesn't contains product with ID {productId}.");
        return false;
    }

    public void RestorePurchases()
    {
        if (isRestoring)
        {
            Debug.LogWarning($"[IAP] {nameof(UnityInAppProvider)} - Restoration already in progress");
            return;
        }

        isRestoring = true;

        Debug.Log($"[IAP] {nameof(UnityInAppProvider)} Starting purchase restoration.");

        storeController.RestoreTransactions((success, error) =>
        {
            if (!success)
            {
                RestoringFailed?.Invoke();
                Debug.LogError($"[IAP] {nameof(UnityInAppProvider)} RestoreTransactions failed: {error}");
                isRestoring = false;
            }
        });
    }

    public void Dispose()
    {
        if (storeController != null)
        {
            storeController.OnStoreDisconnected -= OnStoreDisconnected;
            storeController.OnProductsFetched -= OnProductsFetched;
            storeController.OnProductsFetchFailed -= OnProductsFetchFailed;
            storeController.OnPurchasesFetched -= OnPurchasesFetched;
            storeController.OnPurchasesFetchFailed -= OnPurchasesFetchFailed;
            storeController.OnPurchasePending -= OnPurchasePending;
            storeController.OnPurchaseConfirmed -= OnPurchaseConfirmed;
            storeController.OnPurchaseFailed -= OnPurchaseFailed;
            storeController.OnPurchaseDeferred -= OnPurchaseDeferred;
            storeController = null;
        }

        initialProductsToFetch.Clear();
        productsMap.Clear();
    }

    public async UniTask Initialize()
    {
        Debug.Log($"[IAP] {nameof(UnityInAppProvider)} - Initialization started");

        try
        {
            await UnityServices.InitializeAsync(new InitializationOptions().SetEnvironmentName(EnvironmentName));

            storeController = UnityIAPServices.StoreController();

            storeController.OnStoreDisconnected += OnStoreDisconnected;
            storeController.OnProductsFetched += OnProductsFetched;
            storeController.OnProductsFetchFailed += OnProductsFetchFailed;
            storeController.OnPurchasesFetched += OnPurchasesFetched;
            storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
            storeController.OnPurchasePending += OnPurchasePending;
            storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
            storeController.OnPurchaseFailed += OnPurchaseFailed;
            storeController.OnPurchaseDeferred += OnPurchaseDeferred;

            await storeController.Connect();

            List<ProductDefinition> converted = initialProductsToFetch
                .Select(inApp => inApp.ConvertToUnityProductDefinition())
                .ToList();

            storeController.FetchProducts(converted);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IAP] Initialization failed {nameof(UnityInAppProvider)} {ex.Message}");
        }
    }

    private void OnStoreDisconnected(StoreConnectionFailureDescription description)
    {
        Debug.LogWarning($"[IAP] {nameof(UnityInAppProvider)} - Store connection down/failed. {description.Message}");
        currentState.Value = ServiceState.InitializationFailed;
    }

    private void OnProductsFetched(List<Product> products)
    {
        var inAppProducts = new List<InAppProductInfo>(20);

        foreach (var product in products)
        {
            if (product == null)
            {
                Debug.LogError($"[IAP] {nameof(UnityInAppProvider)} - Fetched product is NULL.");
                continue;
            }

            InAppProductInfo inAppProduct = product.ConvertToInAppProductInfo();
            inAppProducts.Add(inAppProduct);
            productsMap[inAppProduct.Id] = (inAppProduct, product);
            Debug.Log($"[IAP] {nameof(UnityInAppProvider)} - Product fetched. {inAppProduct.Id}");
        }

        Products = inAppProducts;
        //when products are ready
        storeController.FetchPurchases();
    }

    private void OnProductsFetchFailed(ProductFetchFailed failed)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append($"[IAP] {nameof(UnityInAppProvider)} - Product fetch failed. Reason: {failed.FailureReason}. Products list: ");

        foreach (ProductDefinition product in failed.FailedFetchProducts)
            stringBuilder.Append($"{product}, ");

        Debug.LogWarning(stringBuilder.ToString());
        currentState.Value = ServiceState.InitializationFailed;
    }

    private void OnPurchasesFetched(Orders orders)
    {
        HashSet<string> filter = purchaseIds.ToHashSet();
        foreach (var order in orders?.ConfirmedOrders)
        {
            if (IsValidOrder(order, out string id))
            {
                if (filter.Contains(id))
                    continue;

                filter.Add(id);
                purchaseIds.Add(id);
                Debug.Log($"[IAP] {nameof(UnityInAppProvider)} - Purchase fetched {id}.");
            }
        }

        filter.Clear();
        PurchasesFetched?.Invoke(purchaseIds);
        isRestoring = false;

        Debug.Log($"[IAP] {nameof(UnityInAppProvider)} Restore purchases completed successfully");

        if (currentState.Value == ServiceState.Initialized)
            return;

        currentState.Value = ServiceState.Initialized;
        Debug.Log($"[IAP] {nameof(UnityInAppProvider)} - The service has been initialized.");
    }

    private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription description)
    {
        Debug.LogWarning($"[IAP] {nameof(UnityInAppProvider)} - Purchases fetch failed. Reason: {description.FailureReason}. {description.Message}");
        currentState.Value = ServiceState.InitializationFailed;
    }

    private void OnPurchasePending(PendingOrder order)
    {
        Debug.Log($"[IAP] {nameof(UnityInAppProvider)} - Pending order {order}.");
        storeController.ConfirmPurchase(order);
    }

    private void OnPurchaseConfirmed(Order order)
    {
        string transactionID = order?.Info.TransactionID;
        Debug.Log($"[IAP] {nameof(UnityInAppProvider)} - Purchase confirmed {transactionID}.");
        InAppProductInfo data = productsMap[order.Info.PurchasedProductInfo[0].productId].gameData;
        PurchaseCompleted?.Invoke(data);
        analyticsService.Track(new InAppPurchaseAnalyticsEvent(data, transactionID));
    }

    private void OnPurchaseFailed(FailedOrder order)
    {
        Debug.LogWarning($"[IAP] {nameof(UnityInAppProvider)} - Purchase failed. Reason: {order.FailureReason}, {order.Details}.");
        InAppProductInfo data = productsMap[order.Info.PurchasedProductInfo[0].productId].gameData;
        PurchaseFailed?.Invoke(data);
    }

    private void OnPurchaseDeferred(DeferredOrder order)
    {
        Debug.LogWarning($"[IAP] {nameof(UnityInAppProvider)} - Purchase deferred. Reason: {order.Info}.");
    }

    private bool IsValidOrder(Order order, out string productId)
    {
        productId = null;

        var info = order?.Info?.PurchasedProductInfo;
        if (info?.Count > 0)
        {
            productId = info[0].productId;
            return !string.IsNullOrEmpty(productId);
        }

        return false;
    }
}
