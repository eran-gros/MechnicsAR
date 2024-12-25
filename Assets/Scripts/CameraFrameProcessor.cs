using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System;
using Unity.Collections;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;
using Unity.VisualScripting;

public class CameraFrameProcessor : MonoBehaviour
{
    ARCameraManager arCameraManager;
    private Vector3 previousPosition = Vector3.zero;
    public double maxAreaMin = 20;

    public double velocity_factor = 0.0f;
    private Vector3 velocity = Vector3.zero;
    private Queue<Vector3> positionHistory = new Queue<Vector3>();
    private int smoothingWindow = 5; // Number of frames to average
    private bool isFirstFrame = true; // To handle the initial frame
    
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
    private Vector3 GetSmoothedPosition(Vector3 newPosition)
    {
    positionHistory.Enqueue(newPosition);

    if (positionHistory.Count > smoothingWindow)
    {
        positionHistory.Dequeue(); // Remove the oldest position
    }

    Vector3 average = Vector3.zero;
    foreach (var position in positionHistory)
    {
        average += position;
    }

    return average / positionHistory.Count;
    }
    void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        // Try to get the latest camera image
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            return;
        }

        // Process the image (you need to dispose of it after processing)
        StartCoroutine(ProcessCameraImage(cpuImage));
    }

    System.Collections.IEnumerator ProcessCameraImage(XRCpuImage cpuImage)
    {
        // Convert the image to a format usable by Unity
        // For example, convert to Texture2D

        // Get the conversion parameters
        var conversionParams = new XRCpuImage.ConversionParams
        {
            // Get the entire image
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),

            // Downsample if needed
            outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),

            // Choose RGBA format
            outputFormat = TextureFormat.RGB24,

            // Flip vertical axis
            transformation = XRCpuImage.Transformation.MirrorY
        };

        // Create a buffer to hold the image data
        int size = cpuImage.GetConvertedDataSize(conversionParams);
        var buffer = new NativeArray<byte>(size, Allocator.Temp);

        // Convert the image
        cpuImage.Convert(conversionParams, buffer);

        // Don't forget to dispose of the image
        cpuImage.Dispose();

        // Create a Texture2D
        // Use RGB24 to exclude the alpha channel

    Mat imgMat = new Mat(cpuImage.height, cpuImage.width, CvType.CV_8UC3);

    // Create a single-row matrix from the buffer
    Mat rawMat = new Mat(1, buffer.Length, CvType.CV_8UC1);
    rawMat.put(0, 0, buffer.ToArray());

    // Reshape to a 3-channel matrix with the correct number of rows
    rawMat = rawMat.reshape(3, cpuImage.height);

    // Copy reshaped data to imgMat
    rawMat.copyTo(imgMat);

    // Release rawMat when done
    rawMat.release();

    // Dispose of the buffer
    buffer.Dispose();


    // Dispose of the buffer
        // Copy the image data into the texture

// ... (other using directives)

    // Inside ProcessCameraImage coroutine, after creating the Texture2D

    // Convert Texture2D to OpenCV Mat
// Use CvType.CV_8UC3 for a 3-channel image
   

    // Now you can process imgMat using OpenCV functions
    // For example, convert to HSV and perform color thresholding
    Mat imgHSV = new Mat();
    Imgproc.cvtColor(imgMat, imgHSV, Imgproc.COLOR_RGB2HSV);

    // Define HSV range for yellow color
    Scalar lowerYellow = new Scalar(20, 100, 100);
    Scalar upperYellow = new Scalar(30, 255, 255);

    // Threshold the HSV image to get only yellow colors
    Mat mask = new Mat();
    Core.inRange(imgHSV, lowerYellow, upperYellow, mask);
        
    // Find contours (blobs) in the mask
    List<MatOfPoint> contours = new List<MatOfPoint>();
    Imgproc.findContours(mask, contours, new Mat(), Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);

    // Process contours to find the largest one (assuming it's the tennis ball)
    double maxArea = 0;
    MatOfPoint maxContour = null;
    foreach (var contour in contours)
    {
        double area = Imgproc.contourArea(contour);
        if (area > maxArea)
        {
            maxArea = area;
            maxContour = contour;
        }
    }

    if ((maxContour != null) || (maxArea<maxAreaMin))
    {
    // Calculate the centroid of the contour
    Moments moments = Imgproc.moments(maxContour);
    int cx = (int)(moments.m10 / moments.m00);
    int cy = (int)(moments.m01 / moments.m00);

    Vector2 screenPoint = new Vector2(cx, cy);
    List<ARRaycastHit> hits = new List<ARRaycastHit>();
    ARRaycastManager arRaycastManager = FindObjectOfType<ARRaycastManager>();
    if (arRaycastManager != null && arRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinBounds | TrackableType.FeaturePoint))
    {
        Vector3 worldPosition = hits[0].pose.position;
        Vector3 soomthenedPos = GetSmoothedPosition(worldPosition);
        // Calculate velocity if not the first frame
        if (!isFirstFrame)
        {
            velocity = (soomthenedPos - previousPosition) / Time.deltaTime;
            Debug.Log($"Velocity: {velocity}");
        }
        else
        {
            isFirstFrame = false;
        }

        // Update previous position
        previousPosition = worldPosition;
                GetComponent<DynamicLineRenderer>().DrawLine(soomthenedPos, (velocity *(float) velocity_factor) + soomthenedPos);

        // Use velocity for visualizations or further calculations
    }
    }
    

    // Don't forget to release Mats
    imgMat.release();
    imgHSV.release();
    mask.release();
    foreach (var contour in contours)
    {
        contour.release();
    }
        

        // Now you can use the texture for processing
        // For example, pass it to OpenCV for Unity

        yield return null;
    }
}

