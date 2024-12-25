    using System;
using System.Collections.Generic;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class TennisBallTracker : MonoBehaviour
{
    public ARRaycastManager arRaycastManager;
    public float velocityFactor = 2.0f; // Multiplier for velocity visualization
    public float minCircularity = 0.8f; // Threshold for roundness
    public float minArea = 500; // Minimum area to filter small contours
    public Material boundingBoxMaterial; // Material for drawing bounding box

    private Scalar lowerYellow = new Scalar(20, 100, 100);
    private Scalar upperYellow = new Scalar(30, 255, 255);
    private Vector3 previousPosition;
    private bool isFirstFrame = true;
    public RawImage thresholdedView; // Reference to the UI RawImage

    public ARCameraManager arCameraManager;


void Awake()
    {
        arCameraManager = GetComponent<ARCameraManager>();
    }
void OnEnable()
    {
        arCameraManager.frameReceived += OnCameraFrameReceived;
    }

    void OnDisable()
    {
        arCameraManager.frameReceived -= OnCameraFrameReceived;
    }
void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        // Try to get the latest camera image
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            return;
        }

        // Process the image (you need to dispose of it after processing)
        StartCoroutine(ProcessFrame(cpuImage));
    }
    System.Collections.IEnumerator ProcessFrame(XRCpuImage cpuImage)
    {
        // Step 1: Convert to HSV
        Mat imgMat = ProcessCameraFeed(cpuImage);
        Mat balancedImage = ApplyWhiteBalance(imgMat);
        Mat imgHSV = new Mat();
        Imgproc.cvtColor(imgMat, imgHSV, Imgproc.COLOR_RGB2HSV);
        lowerYellow = HSVThresholdManager.Instance.lowerThreshold;
        upperYellow = HSVThresholdManager.Instance.upperThreshold;


        
        Mat mask = new Mat();
        mask = UpdateThresholdedImage(imgHSV);
        // Step 3: Find contours
        List<MatOfPoint> contours = new List<MatOfPoint>();
        Imgproc.findContours(mask, contours, new Mat(), Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);

        MatOfPoint maxContour = null;
        double maxArea = 0;

        foreach (var contour in contours)
        {
            // Calculate area
            double area = Imgproc.contourArea(contour);
            if (area < minArea) continue;

            // Check circularity
            double perimeter = Imgproc.arcLength(new MatOfPoint2f(contour.toArray()), true);
            double circularity = (4 * Mathf.PI * (float)area) / (perimeter * perimeter);

            if (circularity > minCircularity && area > maxArea)
            {
                maxArea = area;
                maxContour = contour;
            }
        }

        if (maxContour != null)
        {
            // Step 4: Draw bounding box
            OpenCVForUnity.CoreModule.Rect boundingRect = Imgproc.boundingRect(maxContour);
            Imgproc.rectangle(imgMat, boundingRect.tl(), boundingRect.br(), new Scalar(0, 255, 0), 2);

            // Step 5: Calculate centroid
            Moments moments = Imgproc.moments(maxContour);
            int cx = (int)(moments.m10 / moments.m00);
            int cy = (int)(moments.m01 / moments.m00);
            List<ARRaycastHit> hits = new List<ARRaycastHit>();
            Vector2 screenPoint = new Vector2(cx, cy);
            if (arRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinBounds | TrackableType.FeaturePoint))
            {
                Vector3 worldPosition = hits[0].pose.position;
                Debug.Log($"Raycast hit at: {worldPosition}");

                // Step 6: Smooth the position
                Vector3 smoothedPos = GetSmoothedPosition(worldPosition);

                // Step 7: Calculate velocity
                if (!isFirstFrame)
                {
                    Vector3 velocity = (smoothedPos - previousPosition) / Time.deltaTime;
                    Debug.Log($"Velocity: {velocity}");

                    // Visualize velocity with a line
                    GetComponent<DynamicLineRenderer>().DrawLine(smoothedPos, (velocity * velocityFactor) + smoothedPos);
                }
                else
                {
                    isFirstFrame = false;
                }

                previousPosition = smoothedPos;
            }
        }

        // Step 8: Cleanup
        imgHSV.release();
        mask.release();
        foreach (var contour in contours)
        {
            contour.release();
        }
        yield return null;
    }

Mat ProcessCameraFeed(XRCpuImage image)
{
    // Example of capturing camera feed with AR Foundation
    Mat inputMat = new Mat();
    {
        // Convert the camera image to Mat and pass to UpdateThresholdedImage
        inputMat = ConvertCpuImageToMat(image); // Implement this based on your pipeline
        if (inputMat != null)
        {
            UpdateThresholdedImage(inputMat);
        }
        image.Dispose();
    }
    return inputMat;
}

private Mat ConvertCpuImageToMat(XRCpuImage image)
{
    // Ensure the image format is supported
    if (image.format != XRCpuImage.Format.RGBA32 && image.format != XRCpuImage.Format.BGRA32)
    {
        Debug.LogError("Unsupported image format! Use RGBA32 or BGRA32.");
        return null;
    }

    // Convert the image to raw texture data
    XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams
    {
        inputRect = new RectInt(0, 0, image.width, image.height),
        outputDimensions = new Vector2Int(image.width, image.height),
        outputFormat = TextureFormat.RGBA32,
        transformation = XRCpuImage.Transformation.None
    };

    // Allocate buffer
    NativeArray<byte> rawTextureData = new NativeArray<byte>(image.GetConvertedDataSize(conversionParams), Allocator.Temp);

    // Perform the conversion
    image.Convert(conversionParams, new NativeSlice<byte>(rawTextureData));

    // Create an OpenCV Mat from the raw texture data
    Mat mat = new Mat(image.height, image.width, CvType.CV_8UC4); // 4 channels for RGBA
    mat.put(0, 0, rawTextureData.ToArray());

    // Dispose of the NativeArray
    rawTextureData.Dispose();

    return mat;

}

    Mat UpdateThresholdedImage(Mat inputMat)
{
    if (inputMat.empty())
    {
        Debug.LogError("Input Mat is empty!");
        return new Mat();
    }   

    // Apply the HSV thresholds
    Mat mask = new Mat(inputMat.cols(),inputMat.rows(),CvType.CV_8UC4);
    Core.inRange(inputMat, lowerYellow, upperYellow, mask);

    // Convert the mask to a Texture2D
    Texture2D texture = new Texture2D(mask.cols(), mask.rows(), TextureFormat.R8, false);
    Utils.matToTexture2D(mask, texture);

    // Display the texture in the RawImage
    thresholdedView.texture = texture;
    return mask;
    // Release Mats
    //imgHSV.release();
    //mask.release();
}
    private Mat ApplyWhiteBalance(Mat img)
    {
    // Convert to LAB color space
        Mat labImage = new Mat();
        Imgproc.cvtColor(img, labImage, Imgproc.COLOR_RGB2Lab);

        // Split into LAB channels
        List<Mat> labChannels = new List<Mat>();
        Core.split(labImage, labChannels);

        // Apply CLAHE (Contrast Limited Adaptive Histogram Equalization) to the L-channel
        Mat lChannel = labChannels[0];
        CLAHE clahe = Imgproc.createCLAHE(2.0, new Size(8, 8));
        clahe.apply(lChannel, lChannel);

        // Merge back the LAB channels
        labChannels[0] = lChannel;
        Core.merge(labChannels, labImage);

        // Convert back to RGB color space
        Mat balancedImage = new Mat();
        Imgproc.cvtColor(labImage, balancedImage, Imgproc.COLOR_Lab2RGB);

        // Release temporary Mats
        foreach (var channel in labChannels) channel.release();
        labImage.release();

        return balancedImage;
    }

    Vector3 GetSmoothedPosition(Vector3 position)
    {
        // Add smoothing logic if needed (e.g., low-pass filter)
        return position;
    }

    void OnDrawGizmos()
    {
        // Optional: Draw bounding box in Unity's Gizmos view
    }


    public void UpdateHSVRange(float hue, float saturation, float value)
    {
    lowerYellow = new Scalar(hue - 5, saturation - 50, value - 50);
    upperYellow = new Scalar(hue + 5, saturation + 50, value + 50);

    Debug.Log($"Updated HSV Range: Lower = {lowerYellow}, Upper = {upperYellow}");
    
    }
    
}