using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum FleetRole { Line, Screen, Carrier, Fighter, Interceptor, Bomber }

[System.Serializable]
public class Ship : MonoBehaviour
{
    public GameObject icon;
    [Tooltip("Name shown in menus. Leave empty to derive it from the asset name (Human_FighterSquadron -> Fighter Squadron).")]
    public string displayName;

    public GameObject prefab => gameObject;

    public int cost;
    public float buildTime = 20f;
    public float combatPower = 100f;
    [Min(0)] public int populationCost = 2;
    public FleetRole fleetRole = FleetRole.Line;

    // Only capital ships can lead a fleet - the lead is the ship that opens an attack on its own.
    public bool CanLead => fleetRole == FleetRole.Line || fleetRole == FleetRole.Screen || fleetRole == FleetRole.Carrier;

    private Sprite cachedIconSprite;

    public Sprite IconSprite
    {
        get
        {
            if (cachedIconSprite != null || icon == null) return cachedIconSprite;

            Image sourceImage = icon.GetComponentInChildren<Image>();
            if (sourceImage != null && sourceImage.sprite != null)
            {
                cachedIconSprite = sourceImage.sprite;
                return cachedIconSprite;
            }

            RawImage sourceRawImage = icon.GetComponentInChildren<RawImage>();
            if (sourceRawImage != null && sourceRawImage.texture is Texture2D texture2D)
            {
                cachedIconSprite = Sprite.Create(texture2D, new Rect(0, 0, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f));
            }

            return cachedIconSprite;
        }
    }
}
