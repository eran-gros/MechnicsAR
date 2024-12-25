using UnityEngine;

public class DynamicLineRenderer : MonoBehaviour
{
    private LineRenderer lineRenderer;

    void Start()
    {
        // Dynamically add a LineRenderer component
        lineRenderer = gameObject.AddComponent<LineRenderer>();

        // Set the width of the line
        lineRenderer.startWidth = 0.01f;
        lineRenderer.endWidth = 0.01f;

        // Set the material (use a default material or assign one in the Resources folder)
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        // Set the color
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.blue;

        // Set other properties as needed
        lineRenderer.positionCount = 2; // Minimum 2 points to draw a line
    }

    public void DrawLine(Vector3 startPoint, Vector3 endPoint)
    {
        // Set the start and end positions of the line
        lineRenderer.SetPosition(0, startPoint);
        lineRenderer.SetPosition(1, endPoint);
    }
}