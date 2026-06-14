using System.Collections.Generic;
using UnityEngine;


// this the the main inventory bag which will add and remove the the items.
public class Inventory
{
    public List<inventory_slots> slots = new List<inventory_slots>();

    public bool AddItem(inventory_holder item)
    {
        foreach (var slot in slots)
        {
            if (slot.itemName == item)
            {
                if (slot.quantity >= item.maxStack)
                {
                    Debug.Log("Cannot pick more " + item.itemName + " (Max reached)");
                    return false; // ❌ failed
                }

                slot.quantity++;
                Debug.Log("Picked " + item.itemName + " | Total: " + slot.quantity);
                return true; // ✅ success
            }
        }

        inventory_slots newSlot = new inventory_slots();
        newSlot.itemName = item;
        newSlot.quantity = 1;

        slots.Add(newSlot);

        Debug.Log("Picked new item: " + item.itemName);
        return true; // ✅ success
    }

    public bool RemoveItem(inventory_holder item)
    {
        foreach (var slot in slots)
        {
            if (slot.itemName == item)
            {
                slot.quantity--;

                Debug.Log("Removed " + item.itemName + " | Remaining: " + slot.quantity);

                // 🔥 If quantity becomes 0 → remove slot
                if (slot.quantity <= 0)
                {
                    slots.Remove(slot);
                    Debug.Log(item.itemName + " removed completely from inventory");
                }

                return true; // ✅ success
            }
        }

        Debug.Log("Item not found in inventory: " + item.itemName);
        return false; // ❌ item not found
    }
}
