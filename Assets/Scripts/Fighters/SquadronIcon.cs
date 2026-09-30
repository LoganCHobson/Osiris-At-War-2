using UnityEngine;
using UnityEngine.UI;

public class SquadronIcon : MonoBehaviour
{
    public Image image;
    public Image healthFill;
    public Color healthyColor = new Color(0.3f, 1f, 0.35f, 1f);
    public Color criticalColor = new Color(1f, 0.25f, 0.2f, 1f);
    public float pixelSize = 34f;
    public float canvasSize = 10f;
    public float heightOffset = 9f;

    public Color friendlyColor = new Color(0.35f, 0.75f, 1f, 0.9f);
    public Color enemyColor = new Color(1f, 0.35f, 0.3f, 0.9f);
    public Color selectedColor = new Color(0.5f, 1f, 0.5f, 1f);

    private Camera cam;
    private bool selected;
    private bool friendly = true;
    private UnitHealthManager health;
    private float shownHealth = -1f;

    private void Start()
    {
        health = GetComponentInParent<UnitHealthManager>();
        Refresh();
    }

    private void UpdateHealth()
    {
        if (healthFill == null || health == null || health.maxHealth <= 0f) return;

        float fraction = Mathf.Clamp01(health.currentHealth / health.maxHealth);
        if (Mathf.Approximately(fraction, shownHealth)) return;

        shownHealth = fraction;
        healthFill.fillAmount = fraction;
        healthFill.color = Color.Lerp(criticalColor, healthyColor, fraction);
    }

    public void SetSide(bool playerSide)
    {
        friendly = playerSide;
        Refresh();
    }

    public void SetSelected(bool value)
    {
        selected = value;
        Refresh();
    }

    private void Refresh()
    {
        if (image != null)
        {
            image.color = selected ? selectedColor : friendly ? friendlyColor : enemyColor;
        }
    }

    private void LateUpdate()
    {
        UpdateHealth();

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        Transform anchor = transform.parent != null ? transform.parent : transform;
        transform.position = anchor.position + Vector3.up * heightOffset;
        transform.rotation = cam.transform.rotation;

        float distance = Vector3.Distance(cam.transform.position, transform.position);
        float worldPerPixel = cam.orthographic
            ? cam.orthographicSize * 2f / Screen.height
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;

        float scale = pixelSize * worldPerPixel / Mathf.Max(0.01f, canvasSize);
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(scale / parentScale.x, scale / parentScale.y, scale / parentScale.z);
    }
}
