using NUnit.Framework;
using UnityEngine;
using TMPro;
public class ReinforcementsManager : MonoBehaviour
{
    public int maxReinforcements = 100;
    public Fleet currentFleet; //Maybe make this a class?
    public int currentReinforcements;
    public TMP_Text reinforcementsText;

    public RectTransform container;
    public void Start()
    {
        reinforcementsText.text = currentReinforcements + " / " + maxReinforcements;

        foreach(Ship ship in currentFleet.ships)
        {
            Instantiate(ship.icon, container);
        }
    }

    public void UpdateUI()
    {
        reinforcementsText.text = currentReinforcements + " / " + maxReinforcements;
    }
}
