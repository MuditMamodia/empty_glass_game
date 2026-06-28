using UnityEngine;

public class locked_door : MonoBehaviour
{
    public bool doorlocked;
    public inventory_holder requiredKey;

    public door_opening_and_closing doorOpeningSystem;
    public inventory_brain inventoryBrain;

    public bool DoorLocked
    {
        get => doorlocked;

        set
        {
            doorlocked = value;

            // Runs automatically whenever the value changes
            doorOpeningSystem.enabled = !doorlocked;
        }
    }
    public bool TryUnlock()
    {
        if (!doorlocked)
            return true;

        if (inventoryBrain.inventory.HasItem(requiredKey))
        {
            DoorLocked = false;

            // Optional:
            // inventoryBrain.RemoveItem(requiredKey);

            Debug.Log("Door unlocked");

            return true;
        }

        Debug.Log("Key not found");

        return false;
    }

}
