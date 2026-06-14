using UnityEngine;

/// <summary>
/// this script is used for giving the multiple collectable object there own property
/// </summary>
[CreateAssetMenu(menuName = "Inventory/Item")]
public class inventory_holder : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public bool stackable;
    public int maxStack;
}
