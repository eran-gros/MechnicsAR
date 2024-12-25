using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using Unity.Collections;

public class iPadTennisBallTracker : MonoBehaviour
{
    [Header("Debug / Test Objects")]
    public GameObject dummySpherePrefab;
    private GameObject dummySphereInstance;
    [Header("AR Components")]
    public ARCameraManager cameraManager;
    public ARRaycastManager arRaycastManager;
    public RawImage displayImage;

    [Header("Ball Tracking Parameters")]
    [Range(0,180)] public float hueMin = 30f;
    [Range(0,180)] public float hueMax = 90f;
    [Range(0,255)] public float satMin = 100f;
    [Range(0,255)] public float satMax = 255f;
    [Range(0,255)] public float valueMin = 100f;
    [Range(0,255)] public float valueMax = 255f;
    [Range(0,10000)] public float TennisMax = 10000f;

    [Header("Velocity Tracking")]
    public LineRenderer velocityLineRenderer;
    private Vector3 lastDetectedBallPosition = Vector3.zero;
    private float lastDetectionTime = 0f;
    private Vector3 lastVelocity = Vector3.zero;

    [Header("Performance")]
    public int processEveryNFrames = 3; // Process every Nth frame
    private int frameCount = 0;

    [Header("Augmented Bounding Box")]
    public GameObject boundingBoxPrefab; // Assign a prefab in the Inspector
    private GameObject currentBoundingBox;

    // Reusable objects
    private Mat kernel;
    private Texture2D displayTexture;
    private byte[] dataBuffer;
    private int currentWidth;
    private int currentHeight;

    private float fx,fy;
    private float scaleX,scaleY;
    void Start()
    {
        
        
        if (cameraManager == null)
        {
            Debug.LogError("ARCameraManager must be assigned!");
            enabled = false;
            return;
        }
        if (dummySpherePrefab != null)
        {
        dummySphereInstance = Instantiate(dummySpherePrefab);
        dummySphereInstance.SetActive(false);
        }
        if (arRaycastManager == null)
        {
            Debug.LogWarning("ARRaycastManager not assigned. AR raycasting for bounding box corners may fail.");
        }

        // Create a reusable kernel for morphology
        kernel = Imgproc.getStructuringElement(Imgproc.MORPH_ELLIPSE, new Size(5, 5));

        // Instantiate the bounding box once
        if (boundingBoxPrefab != null)
        {
            currentBoundingBox = Instantiate(boundingBoxPrefab);
            currentBoundingBox.SetActive(false);
        }

        cameraManager.frameReceived += OnCameraFrameReceived;
    }

    void OnDisable()
    {
        cameraManager.frameReceived -= OnCameraFrameReceived;

        // Release kernel when done
        if (kernel != null)
        {
            kernel.release();
            kernel = null;
        }

        // Texture will be managed by Unity GC, but you can also null it if desired.
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
{
    if (cameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
    {
        
// After deciding on how the camera feed fits in the screen, you might need a horizontal or vertical offset.
        fx = intrinsics.focalLength.x;
        fy = intrinsics.focalLength.y;
        Debug.Log($"Focal Length: fx = {fx}, fy = {fy}");
    }
    else
    {
        Debug.LogWarning("Could not retrieve camera intrinsics.");
    }

    frameCount++;
    if (frameCount % processEveryNFrames != 0)
    {
        return;
    }

    if (cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
    {
         scaleX = (float)Screen.width / (float)image.height;
         scaleY = (float)Screen.height / (float)image.width;
        ProcessCameraImage(image);
        image.Dispose();
    }
    else
    {
        Debug.LogWarning("No camera image available this frame.");
    }
}

    private void ProcessCameraImage(XRCpuImage cpuImage)
    {
        // Clamp HSV
        float hMin = Mathf.Clamp(hueMin, 0, 180);
        float hMax = Mathf.Clamp(hueMax, 0, 180);
        float sMin = Mathf.Clamp(satMin, 0, 255);
        float sMax = Mathf.Clamp(satMax, 0, 255);
        float vMin = Mathf.Clamp(valueMin, 0, 255);
        float vMax = Mathf.Clamp(valueMax, 0, 255);

        // Conversion params
        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
            transformation = XRCpuImage.Transformation.MirrorY,
            outputFormat = TextureFormat.RGBA32
        };

        int size = cpuImage.GetConvertedDataSize(conversionParams);

        // Resize the buffer if needed (only once)
        if (dataBuffer == null || dataBuffer.Length != size)
        {
            dataBuffer = new byte[size];

            // Also recreate texture if dimensions changed
            if (displayTexture != null)
            {
                Destroy(displayTexture);
            }
            displayTexture = new Texture2D(cpuImage.width, cpuImage.height, TextureFormat.R8, false);
            currentWidth = cpuImage.width;
            currentHeight = cpuImage.height;
        }

        // Convert image
        using (NativeArray<byte> rawTextureData = new NativeArray<byte>(size, Allocator.Temp))
        {
            cpuImage.Convert(conversionParams, new NativeSlice<byte>(rawTextureData));
            rawTextureData.CopyTo(dataBuffer);
        }

        // Create Mats
        Mat originalMat = new Mat(cpuImage.height, cpuImage.width, CvType.CV_8UC4);
        originalMat.put(0, 0, dataBuffer);

        Mat bgrMat = new Mat();
        Imgproc.cvtColor(originalMat, bgrMat, Imgproc.COLOR_RGBA2BGR);

        Mat hsvMat = new Mat();
        Imgproc.cvtColor(bgrMat, hsvMat, Imgproc.COLOR_BGR2HSV);
        rotateFrame(hsvMat);
        Mat binaryMask = new Mat();
        Core.inRange(hsvMat, new Scalar(hMin, sMin, vMin), new Scalar(hMax, sMax, vMax), binaryMask);

        Imgproc.morphologyEx(binaryMask, binaryMask, Imgproc.MORPH_OPEN, kernel);

        List<MatOfPoint> contours = new List<MatOfPoint>();
        Mat hierarchy = new Mat();

        Imgproc.findContours(binaryMask, contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
        hierarchy.release();

        if (contours.Count == 0)
        {
            Debug.Log("No contours found. Possibly no ball detected.");
            DisplayMask(binaryMask);
            CleanupMats(originalMat, bgrMat, hsvMat, binaryMask);
            originalMat = null;
            bgrMat =null;
            hsvMat = null;
            binaryMask = null;
            ReleaseContours(contours);
            return;
        }

        // Find largest contour
        double largestArea = 0;
        MatOfPoint largestContour = null;

        foreach (var contour in contours)
        {
            double area = Imgproc.contourArea(contour);
            if (area < TennisMax && area > largestArea)
            {
                largestArea = area;
                largestContour = contour;
            }
        }
                Moments moments = Imgproc.moments(largestContour);
        if (moments.m00 == 0)
        {
            Debug.LogWarning("M00 == 0, can't compute centroid.");
            DisplayMask(binaryMask);
            CleanupMats(originalMat, bgrMat, hsvMat, binaryMask);
            ReleaseContours(contours);
            return;
        }

        int cx = (int)(moments.m10 / moments.m00);
        int cy = (int)(moments.m01 / moments.m00);
        cy = hsvMat.height()-cy;
        //cx = hsvMat.width()-cx;
       
        OpenCVForUnity.CoreModule.Rect boundingRect = Imgproc.boundingRect(largestContour);
        // Convert image coordinates of bounding rect corners to AR world positions
        Vector2 topLeftScreen = new Vector2(boundingRect.x, boundingRect.y);
        Vector2 topRightScreen = new Vector2(boundingRect.x + boundingRect.width, boundingRect.y);
        Vector2 bottomLeftScreen = new Vector2(boundingRect.x, boundingRect.y + boundingRect.height);
        Vector2 bottomRightScreen = new Vector2(boundingRect.x + boundingRect.width, boundingRect.y + boundingRect.height);

        Vector3? topLeftWorld = ScreenToWorld(topLeftScreen);
        Vector3? topRightWorld = ScreenToWorld(topRightScreen);
        Vector3? bottomLeftWorld = ScreenToWorld(bottomLeftScreen);
        Vector3? bottomRightWorld = ScreenToWorld(bottomRightScreen);
        float ballRealDiameter = 0.047f; // Approx. diameter of a tennis ball is ~6.7cm = 0.067m
        float ballImageDiameterPixels = boundingRect.width; // Width of the bounding box in pixels
        float focalLengthInPixels = fx; // Use fx obtained from the camera intrinsics

            // Apply the formula:
            // distance = (ball_real_diameter * focal_length_in_pixels) / ball_image_diameter_in_pixels
        float distance = ballRealDiameter * focalLengthInPixels / ballImageDiameterPixels;
        float mappedX = cx * scaleX;
        float mappedY = cy * scaleY;
        Vector3 worldPosition = ConvertScreenToWorld((int)mappedX, (int)mappedY, distance);

// Enable and place the dummy sphere at the ball's position
        if (dummySphereInstance != null)
        {
            dummySphereInstance.SetActive(true);
            dummySphereInstance.transform.position = worldPosition;
            dummySphereInstance.transform.localScale = Vector3.one * 0.05f; // A small sphere
}

// Temporarily comment out UpdateBoundingBox calls or fallback.
// Just focus on the dummy sphere for now.
       // TrackBallVelocity(worldPosition);
       // DrawVelocityVector(worldPosition);

        // If we got all corners in world space, update bounding box from corners, else fallback
        if (topLeftWorld.HasValue && topRightWorld.HasValue && bottomLeftWorld.HasValue && bottomRightWorld.HasValue)
        {
            //UpdateBoundingBox(topLeftWorld.Value, topRightWorld.Value, bottomLeftWorld.Value, bottomRightWorld.Value);
        }
        else
        {
            // Fallback: just place a small box at ball center
            //UpdateBoundingBoxFallback(worldPosition);
        }
        // Assume you have a binaryMask Mat and a boundingRect from Imgproc.boundingRect()

// Draw a rectangle on the binary mask
// Scalar(255) for a white rectangle on a binary mask
// Thickness = 2 pixels (adjust as needed)
        Imgproc.rectangle(binaryMask, boundingRect.tl(), boundingRect.br(), new Scalar(255), 2);
        DisplayMask(binaryMask);

        // Cleanup
        CleanupMats(originalMat, bgrMat, hsvMat, binaryMask);
        ReleaseContours(contours);
    }


    private Vector3 ConvertScreenToWorld(int cx, int cy, float distance)
    {
        if (arRaycastManager != null)
        {
            
            Vector2 screenPoint = new Vector2(cx, cy);
            List<ARRaycastHit> hits = new List<ARRaycastHit>();
            if (arRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
            {
                return hits[0].pose.position;
            }
            else
            {
                Debug.LogWarning("No AR hit from screen coordinates, falling back to approximate position.");
            }
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            
            Debug.Log($"Estimated Distance to the ball: {distance} meters");
            Vector3 screenPos = new Vector3(cx, cy, mainCamera.nearClipPlane + 1.0f);
            return mainCamera.ScreenToWorldPoint(screenPos);
        }

        

        return Vector3.zero;
    }
    private Vector3? ScreenToWorld(Vector2 screenPoint)
    {
        if (arRaycastManager == null) return null;
        
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        if (arRaycastManager.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
        {
            return hits[0].pose.position;
        }

        // If no AR hit, return null
        return null;
    }

    private void TrackBallVelocity(Vector3 currentPosition)
    {
        float currentTime = Time.time;

        if (lastDetectedBallPosition != Vector3.zero && currentTime != lastDetectionTime)
        {
            lastVelocity = (currentPosition - lastDetectedBallPosition) / (currentTime - lastDetectionTime);
            Debug.Log($"Ball Velocity (in world coords): {lastVelocity}");
        }

        lastDetectedBallPosition = currentPosition;
        lastDetectionTime = currentTime;
    }

    private void DrawVelocityVector(Vector3 ballPosition)
    {
        if (velocityLineRenderer != null && lastVelocity != Vector3.zero)
        {
            velocityLineRenderer.positionCount = 2;
            velocityLineRenderer.SetPosition(0, ballPosition);
            velocityLineRenderer.SetPosition(1, ballPosition + lastVelocity * 0.5f);
        }
    }

    private void UpdateBoundingBox(Vector3 topLeftWorld, Vector3 topRightWorld, Vector3 bottomLeftWorld, Vector3 bottomRightWorld)
    {
        if (currentBoundingBox == null) return;

        currentBoundingBox.SetActive(true);
        /////
        Vector3 horizontal = topRightWorld - topLeftWorld;
        Vector3 vertical = bottomLeftWorld - topLeftWorld;

        // Orthonormalize to ensure a perfect rectangle
        horizontal.Normalize();
        vertical.Normalize();

        Vector3 forward = vertical;  // Forward along vertical edge
        Vector3 right = horizontal;  // Right along horizontal edge
        Vector3 up = Vector3.Cross(right, forward); // Compute a proper up direction
        forward = Vector3.Cross(up, right); // Re-orthonormalize if needed

        Quaternion rotation = Quaternion.LookRotation(forward, up);
        currentBoundingBox.transform.rotation = rotation;

        // Compute width & height
        float width = Vector3.Distance(topLeftWorld, topRightWorld);
        float height = Vector3.Distance(topLeftWorld, bottomLeftWorld);

        // Scale the bounding box
        currentBoundingBox.transform.localScale = new Vector3(width, 0.01f, height);
        /////
        
    }

    private void UpdateBoundingBoxFallback(Vector3 worldPosition)
    {
        if (currentBoundingBox == null) return;

        // Just place a small cube at the detected position
        currentBoundingBox.SetActive(true);
        currentBoundingBox.transform.position = worldPosition;
        currentBoundingBox.transform.rotation = Quaternion.identity;
        currentBoundingBox.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
    }

    private void HideBoundingBox()
    {
        if (currentBoundingBox != null)
            currentBoundingBox.SetActive(false);
    }

    private void DisplayMask(Mat mask)
    {
        
        //rotateFrame(mask);
        if (displayImage == null)
        {
            Debug.LogWarning("No RawImage assigned for display.");
            return;
        }

        // Update existing texture instead of creating a new one every frame
        if (displayTexture.width == mask.cols() && displayTexture.height == mask.rows())
        {
            
            Utils.matToTexture2D(mask, displayTexture);
            displayImage.texture = displayTexture;
        }
        else
        {
            // If dimensions differ, recreate the texture once
            if (displayTexture != null)
                Destroy(displayTexture);

            displayTexture = new Texture2D(mask.cols(), mask.rows(), TextureFormat.R8, false);
            Utils.matToTexture2D(mask, displayTexture);
            displayImage.texture = displayTexture;
        }
    }
    private void rotateFrame(Mat frame)
    {   
        //Imgproc.rotate(frame, frame, Imgproc.ROTATE_90_CLOCKWISE);

// OR Rotate 90 degrees counter-clockwise
Core.rotate(frame, frame, Core.ROTATE_90_CLOCKWISE);

// OR Rotate 180 degrees
        //Imgproc.rotate(frame, frame, Imgproc.ROTATE_180);

        // Flip horizontally
        //Core.flip(frame, frame, 1);

// Flip vertically
        Core.flip(frame, frame, 0);

// Flip both horizontally and vertically
        //Core.flip(frame, frame, -1);  
    }

    private void CleanupMats(params Mat[] mats)
    {
        foreach (var mat in mats)
        {
            if (mat != null)
            {
                mat.release();
            }
        }
    }

    private void ReleaseContours(List<MatOfPoint> contours)
    {
        foreach (var c in contours)
        {
            c.release();
        }
        contours.Clear();
    }
    
    
    // UI Hooks for runtime adjustments
//    public void OnHueMinChanged(float value) => hueMin = value;
    public void OnHueMaxChanged(float value) => hueMax = value;
    public void OnSatMinChanged(float value) => satMin = value;
    public void OnSatMaxChanged(float value) => satMax = value;
    public void OnValueMinChanged(float value) => valueMin = value;
    public void OnValueMaxChanged(float value) => valueMax = value;


    public void OnHueMinChanged(float value)
    {
        hueMin = value;
    }
    }