using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using OpenCVForUnity.CoreModule;


public class ColorSampler : MonoBehaviour
{
    public Camera arCamera; // Reference to the AR Camera
    public Button pickColorButton;
    public Text sampledColorText;
    public Scalar lowerYellow = new Scalar(0,0,0);
    public Scalar upperYellow = new Scalar(0,0,0);
    private bool isSampling = false;

    void Start()
    {
        pickColorButton.onClick.AddListener(StartSampling);
    }

    void Update()
    {
        if (isSampling && Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            Vector2 touchPosition = Input.GetTouch(0).position;

            // Convert touch position to a screen ray
            Ray ray = arCamera.ScreenPointToRay(touchPosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // Sample the color from the point in the AR feed
                Texture2D texture = (Texture2D)hit.collider.GetComponent<Renderer>().material.mainTexture;
                Color sampledColor = texture.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y);

                Debug.Log($"Sampled Color: {sampledColor}");
                sampledColorText.text = $"Sampled Color: {sampledColor}";

                // Convert to HSV and set the threshold
                Color.RGBToHSV(sampledColor, out float h, out float s, out float v);
                SetHSVThreshold(h * 360, s * 255, v * 255);

                isSampling = false;
            }
        }
    }

    void StartSampling()
    {
        isSampling = true;
        Debug.Log("Tap on the screen to pick a color.");
    }

    void SetHSVThreshold(float hue, float saturation, float value)
    {
        // Adjust lower and upper bounds based on the sampled HSV values
        float hueRange = 10; // Adjust as needed
        lowerYellow = new Scalar(hue - hueRange, saturation - 50, value - 50);
        upperYellow = new Scalar(hue + hueRange, saturation + 50, value + 50);

            HSVThresholdManager.Instance.UpdateThresholds(lowerYellow, upperYellow);

        Debug.Log($"Updated HSV Range: Lower = {lowerYellow}, Upper = {upperYellow}");
    }
}