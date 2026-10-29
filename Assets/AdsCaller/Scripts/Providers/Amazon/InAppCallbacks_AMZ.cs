//using System;
//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.UI;
//using TMPro;

//[Serializable]
//public struct StoreObjects
//{
//    public string id;
//    public TextMeshProUGUI[] price;
//    public Button[] buybtn;
//    public EnumsJar.SubscriptionDuration subscriptionDuration;

//    [HideInInspector]
//    public readonly bool IsWeekly => subscriptionDuration == EnumsJar.SubscriptionDuration.Weekly;
//    [HideInInspector]
//    public readonly bool IsMonthly => subscriptionDuration == EnumsJar.SubscriptionDuration.Monthly;
//}

//public class InAppCallbacks_AMZ : MonoBehaviour, IIAPCallBacks
//{
//    public List<StoreObjects> objs = new List<StoreObjects>();
//    public static string PackageName = "com.ama.al.turkeyescape.thanksgivinggame.";
//    public static Action onsub, onunsub;
//    public Button masterPack;
//    public static bool subscribed;

//    private void OnEnable()
//    {
//        foreach (StoreObjects b in objs)
//        {
//            foreach (Button btn in b.buybtn)
//            {
//                btn.onClick.RemoveAllListeners();
//                btn.onClick.AddListener(() =>
//                {
//                    if (InAppLoadingCanvas.Instance != null)
//                        InAppLoadingCanvas.Instance.ShowPanel();

//                    InAppPurchaser_AMZ.PurchaseItem(PackageName + b.id, this);
//                });
//            }
//        }
//        ChangePrice();
//    }

//    public void ChangePrice()
//    {
//        foreach (StoreObjects b in objs)
//        {
//            foreach (TextMeshProUGUI p in b.price)
//            {
//                string rawPrice = PriceString(PackageName + b.id);
//                p.text = rawPrice;
//                if (b.IsWeekly)
//                    p.text = $"<size=25>{rawPrice}</size>\nWeekly";
//                else if (b.IsMonthly)
//                    p.text = $"<size=25>{rawPrice}</size>\nMonthly";
//            }
//        }
//    }

//    public string PriceString(string id)
//    {
//        try
//        {
//            return InAppPurchaser_AMZ.Unity_IAP_get_item_price(id);
//        }
//        catch (Exception)
//        {
//            return "Buy";
//        }
//    }

//    public void PurchaseFailed(string id)
//    {
//        Debug.LogError($"Purchase Failed: {id}");
//    }

//    public bool PurchaseSuccessful(string id)
//    {
//        if (id == PackageName + "removeads" || id == PackageName + "escapepack")
//        {
//            PurchaseRemoveAds();
//            Time.timeScale = 1f;
//        }
//        else if (id == PackageName + "chaospack" || id == PackageName + "supremepack")
//        {
//            Time.timeScale = 1f;
//        }
//        return true;
//    }

//    private void PurchaseRemoveAds()
//    {
//        PlayerPrefs.SetInt("RemoveAds", 1);
//        PlayerPrefs.SetInt("RemoveAdsPurchased", 1);
//    }

//    public void SubscriptionBundle()
//    {
//        if (masterPack != null) masterPack.interactable = false;
//        subscribed = true;
//    }

//    public void UnSubscribeBundle()
//    {
//        PlayerPrefs.SetInt("RemoveAdsSub", 0);
//        PlayerPrefs.SetInt("SkatesBought", 0);
//        if (masterPack != null) masterPack.interactable = true;
//        subscribed = false;
//    }

//    // [UNCLE'S FIX] Consolidated your duplicate enumerators into one functioning interface contract
//    public IEnumerator SyncPurchases()
//    {
//        yield return null;
//        Debug.Log("Sync Purchases is show piece for now");
//    }
//}