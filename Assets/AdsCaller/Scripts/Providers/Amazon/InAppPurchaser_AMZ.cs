//using UnityEngine;
//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine.UI;
//using System;
//using com.amazon.device.iap.cpt;

//public class InAppPurchaser_AMZ : MonoBehaviour
//{
//    #region Variables
//    // Amazon kept the exact same interface name for the new Appstore SDK to make porting easier.
//    private IAmazonIapV2 iapService;
//    public List<SKU> SKUS;

//    private const string TAG = "Amazon InApp Purchases: ";
//    private static List<string> m_PendingProducts = new List<string>();
//    private static List<string> m_CompletedProducts = new List<string>();

//    public static InAppPurchaser_AMZ instance;
//    private static bool mIsLiveContext;

//    [SerializeField] public GameObject AckDialog;
//    [SerializeField] public Text prodTxt;

//    private bool activeInScene;
//    private static IIAPCallBacks callbackObj;

//    private static Dictionary<string, ProductData> productDataCache = new Dictionary<string, ProductData>();
//    #endregion

//    #region Lifecycle methods
//    void Awake()
//    {
//        if (instance == null) instance = this;
//        else if (instance != this) Destroy(gameObject);

//        DontDestroyOnLoad(gameObject);
//        activeInScene = true;
//    }

//    private void Start()
//    {
//        LoadInApps();
//    }
//    #endregion

//    public static void log(string msg)
//    {
//        Debug.Log(TAG + msg);
//    }

//    public void LoadInApps()
//    {
//        if (SKUS != null && SKUS.Count > 0 && activeInScene)
//        {
//            if (iapService == null)
//            {
//                log("Initializing Amazon Appstore SDK...");
//                InitializePurchasing();
//            }
//        }
//    }

//    public void InitializePurchasing()
//    {
//        iapService = AmazonIapV2Impl.Instance;

//        // [UNCLE'S FIX] - NEW APPSTORE SDK VERIFICATION
//        // The new SDK requires a public key (PEM file). If it's missing, this will blow up.
//        // This replaces the old hardcoded IS_SANDBOX_MODE checks.
//        try
//        {
//            GetAppstoreSDKModeOutput modeOutput = iapService.GetAppstoreSDKMode();
//            log("Appstore SDK Mode: " + modeOutput.AppstoreSDKMode);
//        }
//        catch (Exception e)
//        {
//            log("SDK Mode Check Failed! Did you forget the AppstoreAuthenticationKey.pem in StreamingAssets? Error: " + e.Message);
//        }

//        // Hook up Amazon event listeners
//        iapService.AddGetUserDataResponseListener(OnGetUserDataResponse);
//        iapService.AddPurchaseResponseListener(OnPurchaseResponse);
//        iapService.AddGetProductDataResponseListener(OnGetProductDataResponse);
//        iapService.AddGetPurchaseUpdatesResponseListener(OnGetPurchaseUpdatesResponse);

//        // Fetch Product Data
//        SkusInput request = new SkusInput();
//        List<string> skuList = new List<string>();
//        foreach (SKU sku in SKUS)
//        {
//            skuList.Add(sku.ID);
//        }
//        request.Skus = skuList;
//        iapService.GetProductData(request);

//        // Sync previous purchases immediately
//        ResetInput updatesRequest = new ResetInput();
//        updatesRequest.Reset = true;
//        iapService.GetPurchaseUpdates(updatesRequest);
//    }

//    public static bool IsInitialized()
//    {
//        return instance != null && instance.iapService != null;
//    }

//    public static void Register(IIAPCallBacks pCallbackObj)
//    {
//        callbackObj = pCallbackObj;
//    }

//    public static void UnRegister()
//    {
//        callbackObj = null;
//    }

//    public static void BuyProductID(string productId)
//    {
//        if (IsInitialized())
//        {
//            log($"Initiating purchase for: '{productId}'");

//            // Assuming AdsCaller is still part of your project logic
//            AdsCaller._pauseAppOpen = true;

//            if (!m_PendingProducts.Contains(productId))
//                m_PendingProducts.Add(productId);

//            SkuInput request = new SkuInput();
//            request.Sku = productId;
//            instance.iapService.Purchase(request);
//        }
//        else
//        {
//            log("BuyProductID FAIL. Amazon Appstore SDK not initialized yet.");
//        }
//    }

//    public static void PurchaseItem(string sku, MonoBehaviour context)
//    {
//        log("purchase item::" + sku);
//        Register(context as IIAPCallBacks);

//        int index = m_PendingProducts.IndexOf(sku);
//        if (index == -1)
//        {
//            BuyProductID(sku);
//        }
//        else
//        {
//            ShowPendingPurchase();
//        }
//    }

//    public static void ShowPendingPurchase()
//    {
//        log("Purchase is already pending. Hold tight.");
//    }

//    public static void restorePurchases(MonoBehaviour context)
//    {
//        if (context != null)
//        {
//            callbackObj = context as IIAPCallBacks;
//            if (!IsInitialized())
//            {
//                log("RestorePurchases FAIL. Not initialized.");
//                return;
//            }

//            log("Firing RestorePurchases via Amazon SDK...");
//            ResetInput request = new ResetInput();
//            request.Reset = true;
//            instance.iapService.GetPurchaseUpdates(request);
//        }
//    }

//    #region Amazon Callbacks
//    private void OnGetUserDataResponse(GetUserDataResponse args)
//    {
//        log($"User ID: {args.AmazonUserData.UserId}, Marketplace: {args.AmazonUserData.Marketplace}");
//    }

//    private void OnGetProductDataResponse(GetProductDataResponse args)
//    {
//        if (args.Status == "SUCCESSFUL")
//        {
//            foreach (var kvp in args.ProductDataMap)
//            {
//                productDataCache[kvp.Key] = kvp.Value;
//            }
//            log($"Product data mapped for {args.ProductDataMap.Count} items.");
//        }
//    }

//    private void OnPurchaseResponse(PurchaseResponse args)
//    {
//        log($"Purchase Response Status: {args.Status}");

//        if (args.Status == "SUCCESSFUL")
//        {
//            string sku = args.PurchaseReceipt.Sku;
//            string receiptId = args.PurchaseReceipt.ReceiptId;

//            // Important: Tell Amazon the item was delivered!
//            NotifyFulfillmentInput request = new NotifyFulfillmentInput();
//            request.ReceiptId = receiptId;
//            request.FulfillmentResult = "FULFILLED";
//            iapService.NotifyFulfillment(request);

//            m_PendingProducts.Remove(sku);
//            if (!m_CompletedProducts.Contains(sku)) m_CompletedProducts.Add(sku);

//            if (callbackObj != null)
//            {
//                PlayerPrefs.SetInt("OneInappPurchased", 1);
//                callbackObj.PurchaseSuccessful(sku);
//                ShowAcknowledgePurchase(true, sku);
//                UnRegister();
//            }
//        }
//        else
//        {
//            if (callbackObj != null)
//            {
//                callbackObj.PurchaseFailed(args.PurchaseReceipt != null ? args.PurchaseReceipt.Sku : "Unknown");
//                UnRegister();
//            }
//        }
//    }

//    private void OnGetPurchaseUpdatesResponse(GetPurchaseUpdatesResponse args)
//    {
//        if (args.Status == "SUCCESSFUL")
//        {
//            foreach (var receipt in args.Receipts)
//            {
//                if (receipt.CancelDate == 0) // 0 means active
//                {
//                    if (!m_CompletedProducts.Contains(receipt.Sku)) m_CompletedProducts.Add(receipt.Sku);

//                    if (callbackObj != null)
//                    {
//                        callbackObj.PurchaseSuccessful(receipt.Sku);
//                    }
//                }
//            }

//            if (args.HasMore)
//            {
//                ResetInput request = new ResetInput();
//                request.Reset = false;
//                iapService.GetPurchaseUpdates(request);
//            }
//            else
//            {
//                if (callbackObj != null)
//                {
//                    PlayerPrefs.SetInt("OneInappPurchased", 1);
//                    ShowAcknowledgePurchase(true, null);
//                }
//            }
//        }
//    }
//    #endregion

//    public void ShowAcknowledgePurchase(bool liveContext, string skuId)
//    {
//        mIsLiveContext = liveContext;
//        string name = string.IsNullOrEmpty(skuId) ? "Restored Items" : getProductName(skuId);

//        if (prodTxt != null)
//        {
//            prodTxt.text = $"Congratulations! Product {name} is Successfully Yours.";
//        }

//        if (AckDialog != null) AckDialog.SetActive(true);
//    }

//    public string getProductName(string packageId)
//    {
//        foreach (SKU sku in SKUS)
//        {
//            if (sku.ID.Equals(packageId)) return sku.name;
//        }
//        return packageId;
//    }

//    public void HideAckDialog()
//    {
//        if (AckDialog != null) AckDialog.SetActive(false);
//    }

//    public bool CheckIfSubscriptionExist(string subsId)
//    {
//        return m_CompletedProducts.Contains(subsId);
//    }

//    public static string Unity_IAP_get_item_price(string _ProductID)
//    {
//        if (productDataCache.ContainsKey(_ProductID))
//        {
//            return productDataCache[_ProductID].Price;
//        }
//        return "Buy";
//    }
//}

//public interface IIAPCallBacks
//{
//    bool PurchaseSuccessful(string id);
//    void PurchaseFailed(string id);
//    IEnumerator SyncPurchases();
//}

//[System.Serializable]
//public class SKU
//{
//    public string ID;
//    public string name;
//    public EnumsJar.ProductType productType;
//}