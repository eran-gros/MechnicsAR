using UnityEngine;
using UnityEngine.UI;

public class TennisBallColorController : MonoBehaviour
{
    public Slider hueSlider;
    public Slider saturationSlider;
    public Slider valueSlider;

    public TennisBallTracker tennisBallTracker;

    void Start()
    {
        // Initialize sliders with default values
        hueSlider.value = 25; // Midpoint of yellow hue range
        saturationSlider.value = 150;
        valueSlider.value = 150;

        // Add listeners
        hueSlider.onValueChanged.AddListener(UpdateColorRange);
        saturationSlider.onValueChanged.AddListener(UpdateColorRange);
        valueSlider.onValueChanged.AddListener(UpdateColorRange);
    }

    void UpdateColorRange(float value)
    {
        // Pass the updated values to the tracker
        tennisBallTracker.UpdateHSVRange(
            hueSlider.value,
            saturationSlider.value,
            valueSlider.value
        );
    }
    public Image colorBox; // Reference to the UI Image

    public void UpdateColorBox(float hue, float saturation, float value)
    {
        // Convert HSV to RGB
        Color rgbColor = Color.HSVToRGB(hue / 360f, saturation / 255f, value / 255f);

        // Set the color box
        colorBox.color = rgbColor;
        Debug.Log($"Updated Color Box: {rgbColor}");
    }
}