using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class inventory_slot_ui : MonoBehaviour
{
    public Image icon;
    public TextMeshProUGUI countText;
    public Button button;
    public inventory_brain brain;
    private inventory_holder currentItem;
    

    public void SetSlot(inventory_holder item, int quantity)
    {
        currentItem = item;

        icon.sprite = item.icon;
        icon.enabled = true;

        countText.text = quantity.ToString();
        countText.enabled = true;

        button.interactable = true;
    }

    public void ClearSlot()
    {
        currentItem = null;

        icon.enabled = false;
        countText.text = "";

        button.interactable = false;
    }

    // 🔥 Click action
    public void OnClick()
    {
         

        

        if (currentItem != null)
        {
            Debug.Log("Clicked on: " + currentItem.itemName );

            // 👉 Later: use item / equip / drop
        }
    }

}
