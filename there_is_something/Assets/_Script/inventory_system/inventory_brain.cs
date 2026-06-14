
using UnityEngine;
using System;


/// <summary>
/// this scipt is the brain which will call the function and tell like whetere we can add the item in the list or not
/// </summary>
public class inventory_brain : MonoBehaviour
{
    
    public Action onInventoryChanged;
    public Inventory inventory = new Inventory(); // ✅ FIX

    //public void PickupItem(inventory_holder item)
    //{
    //    inventory.AddItem(item);
    //    Debug.Log("Picked up: " + item.itemName);
    //}

    //public bool PickupItem(inventory_holder item)
    //{
    //    return inventory.AddItem(item);
    //}

   

    public bool PickupItem(inventory_holder item)
    {
        bool result = inventory.AddItem(item);

        if (result)
        {
            onInventoryChanged?.Invoke(); // 🔥 notify UI
        }

        return result;
    }

    public bool RemoveItem(inventory_holder item)
    {
        bool result = inventory.RemoveItem(item);

        if (result)
        {
            onInventoryChanged?.Invoke(); // 🔥 notify UI
        }

        return result;
    }

}
