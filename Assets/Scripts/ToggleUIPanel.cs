using UnityEngine;

public class ToggleUIPanel : MonoBehaviour
{
    public GameObject slidersPanel; // Reference to the sliders panel
    private bool isPanelVisible = true; // Track the visibility state

    public void TogglePanel()
    {
        if (slidersPanel != null)
        {
            isPanelVisible = !isPanelVisible; // Toggle the state
            slidersPanel.SetActive(isPanelVisible); // Show or hide the panel
        }
    }
}