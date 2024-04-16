using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using System.Linq;
using UnityEngine.Rendering;
using RGN.Modules.VirtualItems;
using RGN.Modules.Store;
using RGN.Modules.Currency;
using RGN.Modules.Inventory;

#if UNITY_ANALYTICS
using UnityEngine.Analytics;
#endif

public class ShopItemList : ShopList
{
    static public Consumable.ConsumableType[] s_ConsumablesTypes = System.Enum.GetValues(typeof(Consumable.ConsumableType)) as Consumable.ConsumableType[];

    public async override void Populate()
    {
        m_RefreshCallback = null;
        foreach (Transform t in listRoot)
        {
            Destroy(t.gameObject);
        }

        List<VirtualItem> virtualItems = await VirtualItemsModule.I.GetVirtualItemsAsync();

        for (int i = 0; i < s_ConsumablesTypes.Length; ++i)
        {
            Consumable c = ConsumableDatabase.GetConsumbale(s_ConsumablesTypes[i]);
            if (c != null)
            {
                VirtualItem virtualItem = virtualItems.FirstOrDefault(o => o.name == c.name);

                prefabItem.InstantiateAsync().Completed += (op) =>
                {
                    if (op.Result == null || !(op.Result is GameObject))
                    {
                        Debug.LogWarning(string.Format("Unable to load item shop list {0}.", prefabItem.RuntimeKey));
                        return;
                    }
                    GameObject newEntry = op.Result;
                    newEntry.transform.SetParent(listRoot, false);

                    ShopItemListItem itm = newEntry.GetComponent<ShopItemListItem>();

                    itm.buyButton.image.sprite = itm.buyButtonSprite;

                    if (virtualItem != null)
                    {
                        itm.nameText.text = virtualItem.name;
                        itm.pricetext.text = virtualItem.GetCustomCoinPrice("fishbone").ToString();
                        itm.buyButton.onClick.AddListener(delegate () { BuyVirtualItem(virtualItem, c); });
                    }
                    else
                    {
                        itm.nameText.text = c.GetConsumableName();
                        itm.pricetext.text = c.GetPrice().ToString();
                        itm.buyButton.onClick.AddListener(delegate () { Buy(c); });
                    }

                    itm.icon.sprite = c.icon;

                    if (c.GetPremiumCost() > 0)
                    {
                        itm.premiumText.transform.parent.gameObject.SetActive(true);
                        itm.premiumText.text = c.GetPremiumCost().ToString();
                    }
                    else
                    {
                        itm.premiumText.transform.parent.gameObject.SetActive(false);
                    }

                    itm.countText.gameObject.SetActive(true);

                    m_RefreshCallback += delegate () { RefreshButton(itm, c, virtualItem != null); };
                    RefreshButton(itm, c, virtualItem != null);
                };
            }
        }
    }

    protected async void RefreshButton(ShopItemListItem itemList, Consumable c, bool isVirtualItem)
    {
        int count = 0;
        PlayerData.instance.consumables.TryGetValue(c.GetConsumableType(), out count);
        if (isVirtualItem)
        {
            List<InventoryItemData> inventory = await InventoryModule.I.GetWithVirtualItemsDataForCurrentAppAsync();
            foreach (var inventoryItem in inventory)
            {
                if (inventoryItem.virtualItem.name == c.name)
                {
                    itemList.countText.text = inventoryItem.quantity.ToString();
                    break;
                }
            }
        }
        else
        {
            itemList.countText.text = count.ToString();
        }

        if (c.GetPrice() > PlayerData.instance.coins)
        {
            itemList.buyButton.interactable = false;
            itemList.pricetext.color = Color.red;
        }
        else
        {
            itemList.pricetext.color = Color.black;
        }

        if (c.GetPremiumCost() > PlayerData.instance.premium)
        {
            itemList.buyButton.interactable = false;
            itemList.premiumText.color = Color.red;
        }
        else
        {
            itemList.premiumText.color = Color.black;
        }
    }

    public async void BuyVirtualItem(VirtualItem virtualItem, Consumable c)
    {
        List<string> itemsToPurchase = new List<string>() { virtualItem.id };
        PurchaseResult purchaseResult = await StoreModule.I.BuyVirtualItemsAsync(itemsToPurchase);

        PlayerData.instance.coins -= c.GetPrice();
        PlayerData.instance.premium -= c.GetPremiumCost();
        PlayerData.instance.Add(c.GetConsumableType());
        PlayerData.instance.Save();

        Refresh();
    }

    public void Buy(Consumable c)
    {
        PlayerData.instance.coins -= c.GetPrice();
        PlayerData.instance.premium -= c.GetPremiumCost();
        PlayerData.instance.Add(c.GetConsumableType());
        PlayerData.instance.Save();

#if UNITY_ANALYTICS // Using Analytics Standard Events v0.3.0
        var transactionId = System.Guid.NewGuid().ToString();
        var transactionContext = "store";
        var level = PlayerData.instance.rank.ToString();
        var itemId = c.GetConsumableName();
        var itemType = "consumable";
        var itemQty = 1;

        AnalyticsEvent.ItemAcquired(
            AcquisitionType.Soft,
            transactionContext,
            itemQty,
            itemId,
            itemType,
            level,
            transactionId
        );

        if (c.GetPrice() > 0)
        {
            AnalyticsEvent.ItemSpent(
                AcquisitionType.Soft, // Currency type
                transactionContext,
                c.GetPrice(),
                itemId,
                PlayerData.instance.coins, // Balance
                itemType,
                level,
                transactionId
            );
        }

        if (c.GetPremiumCost() > 0)
        {
            AnalyticsEvent.ItemSpent(
                AcquisitionType.Premium, // Currency type
                transactionContext,
                c.GetPremiumCost(),
                itemId,
                PlayerData.instance.premium, // Balance
                itemType,
                level,
                transactionId
            );
        }
#endif

        Refresh();
    }
}
