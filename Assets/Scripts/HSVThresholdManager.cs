using OpenCVForUnity.CoreModule;
using UnityEngine;

public class HSVThresholdManager : MonoBehaviour
{
    public static HSVThresholdManager Instance;

    public Scalar lowerThreshold = new Scalar(20, 100, 100); // Default lower threshold
    public Scalar upperThreshold = new Scalar(30, 255, 255); // Default upper threshold

    private void Awake()
    {
        // Singleton pattern for global access
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void UpdateThresholds(Scalar lower, Scalar upper)
    {
        lowerThreshold = lower;
        upperThreshold = upper;
        Debug.Log($"Updated thresholds: Lower = {lower}, Upper = {upper}");
    }
}