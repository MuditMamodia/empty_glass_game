using System.Collections.Generic;
using UnityEngine;

public class inventory_ui_handler : MonoBehaviour
{
    public inventory_brain brain;
    public List<inventory_slot_ui> slots;

    void Start()
    {
        brain.onInventoryChanged += UpdateUI;
        UpdateUI();
    }

    void UpdateUI()
    {
        var inventorySlots = brain.inventory.slots;

        for (int i = 0; i < slots.Count; i++)
        {
            if (i < inventorySlots.Count)
            {
                slots[i].SetSlot(
                    inventorySlots[i].itemName,
                    inventorySlots[i].quantity
                );
            }
            else
            {
                slots[i].ClearSlot();
            }
        }
    }
}