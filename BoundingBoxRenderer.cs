using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
///  A Class that draws a bounding box around a given <see cref="GameObject"/> from the perspective of the <see cref="Camera"/> that this script is attached to.
/// </summary>
public class BoundingBoxRenderer : MonoBehaviour
{
    #region Controls
    /// <summary>
    /// Game objects to draw bounding boxes around. Each object must contain at least one <see cref="Renderer"/> and a <see cref="Mesh"/>
    /// </summary>
    public GameObject[] targets;


    /// <summary>
    /// Boolean to decide whether to draw bounding box or not. Does not affect whether bounding box is calculated
    /// </summary>
    public bool drawBoundingBox = true;


    /// <summary>
    /// Desired color of the bounding box
    /// </summary>
    public Color boundingBoxColor = new Color(0f, 0f, 0f, 0.5f);
    #endregion


    #region Private members
    /// <summary>
    /// UI <see cref="Canvas"/ that bounding box will be displayed on
    /// </summary>
    private Canvas canvas;
    #endregion


    #region Events
    /// <summary>
    /// Delegate for bounding box calculation events
    /// </summary>
    public delegate void BoundingBoxCalculatedHandler(BoundingBoxInfo boundingBoxInfo);


    /// <summary>
    /// Event to notify subscribers that a bounding box has been calculated. Sends <see cref="BoundingBoxInfo"/> object.
    /// </summary>
    public event BoundingBoxCalculatedHandler BoundingBoxCalculated = delegate { };
    #endregion 


    private void Start()
    {
        Camera camera = GetComponent<Camera>();


        // Create canvas
        GameObject canvasGameObject = new GameObject();
        canvasGameObject.transform.SetParent(camera.transform);
        canvas = canvasGameObject.AddComponent<Canvas>();


        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = camera.nearClipPlane + 0.01f;
    }


    // Draw bounding box in LateUpdate() as canvas performs layout calculations at the end of the frame. Doing this in Update instead will result in the bounding box lagging by one frame.
    void LateUpdate()
    {
        Camera camera = GetComponent<Camera>();


        // Remove any previously drawn bounding boxes
        foreach (Transform child in canvas.transform) Destroy(child.gameObject); // TODO: update existing rect instead of creating new one each frame


        // Go through game objects, calculating bounding boxes, and drawing and recording data if necessary
        for (int i = 0; i < targets.Length; i++)
        {
            if (isObjectBehindCamera(camera, targets[i]) || !camera.enabled) // TODO: also check if object is off screen
            {
                continue;
            }


            Rect boundingBoxRect = getBoundingRectFromVertices(camera, getVerticesFromGameObject(targets[i]));


            if (drawBoundingBox)
            {
                drawRectOnCanvas(canvas, boundingBoxRect, boundingBoxColor);
            }


            // Notify subscribers of bounding box calculation
            BoundingBoxCalculated(new BoundingBoxInfo(objectId: i, (int)boundingBoxRect.x, (int)boundingBoxRect.y, (int)boundingBoxRect.width, (int)boundingBoxRect.height, timeSecs: Time.time));
        }
    }


    /// <summary>
    /// Gets all vertices from all <see cref="Mesh"/> objects within a given <see cref="GameObject"/>.
    /// </summary>
    /// <param name="gameObject"><see cref="GameObject"/> to get vertices from</param>
    /// <returns>
    /// <see cref="Vector3[]"/> combining all vertices found in all <see cref="Mesh"/> objects within the given <see cref="GameObject"/>. Returned points are in world space
    /// </returns>
    private Vector3[] getVerticesFromGameObject(GameObject gameObject)
    {
        Mesh mesh;
        List<Vector3[]> allVertices = new List<Vector3[]>();


        MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>();


        foreach (MeshFilter meshFilter in meshFilters)
        {
            Vector3[] vertices = meshFilter.mesh.vertices;


            // Convert vertices to world space
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = meshFilter.transform.TransformPoint(vertices[i]);
            }


            allVertices.Add(vertices);
        }


        SkinnedMeshRenderer[] skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();


        foreach (SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
        {
            // SkinnedMeshRenderer scales resulting mesh by global scale when BakeMesh() is used, so need to prevent this
            // Can't manipulate global scale directly so need to do a little trick
            Vector3 localScale = skinnedMeshRenderer.transform.localScale;
            Transform parent = skinnedMeshRenderer.transform.parent;
            skinnedMeshRenderer.transform.parent = null;
            skinnedMeshRenderer.transform.localScale = Vector3.one;
            skinnedMeshRenderer.transform.parent = parent;


            // Get the real time vertices
            mesh = new Mesh();
            skinnedMeshRenderer.BakeMesh(mesh);
            Vector3[] vertices = mesh.vertices;


            // Convert vertices to world space
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = skinnedMeshRenderer.transform.TransformPoint(vertices[i]);
            }


            allVertices.Add(vertices);


            skinnedMeshRenderer.transform.localScale = localScale;
            Destroy(mesh);
        }


        List<Vector3> combinedVertices = new List<Vector3>();
        foreach (Vector3[] vertices in allVertices)
        {
            combinedVertices.AddRange(vertices);
        }


        return combinedVertices.ToArray();
    }


    /// <summary>
    /// Calculates a bounding box around the given vertices from the perspective of the given <see cref="Camera"/>.
    /// </summary>
    /// <param name="camera"><see cref="Camera"/> who's perspective the bounding box should be calculated from</param>
    /// <param name="vertices">Vertices in world space that the resulting <see cref="Rect"/> should contain</param>
    /// <returns>
    /// <see cref="Rect"/> with values relative to the <see cref="Screen"/>
    /// </returns>
    private Rect getBoundingRectFromVertices(Camera camera, Vector3[] vertices)
    {
        if (vertices.Length == 0) return Rect.zero;


        // Convert vertices to GUI space
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = camera.WorldToScreenPoint(vertices[i]);
            vertices[i].y = Screen.height - vertices[i].y;
        }


        Vector3 min = vertices[0];
        Vector3 max = vertices[0];


        foreach (Vector3 vertex in vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }


        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }


    /// <summary>
    /// Checks if a given <see cref="GameObject"/> is behind the given <see cref="Camera"/>
    /// </summary>
    /// <param name="camera"><see cref="Camera"/> we should check against</param>
    /// <param name="gameObject"><see cref="GameObject"/> to check is behind camera or not</param>
    /// <returns>
    /// True if any part of the object is behind the <see cref="Camera"/>, false if not
    /// </returns>
    private bool isObjectBehindCamera(Camera camera, GameObject gameObject)
    {
        Bounds bounds = new Bounds();


        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }


        return (camera.WorldToScreenPoint(bounds.center).z - bounds.extents.magnitude) < 0;
    }


    /// <summary>
    /// Adds a <see cref="Rect"/> to be rendered to a given <see cref="Canvas"/>
    /// </summary>
    /// <param name="canvas"><see cref="Canvas"/> to add <see cref="Rect"/> to</param>
    /// <param name="rect"><see cref="Rect"/> to be drawn</param>
    /// <param name="color"><see cref="Color"/> that <see cref="Rect"/> should be drawn in</param>
    private void drawRectOnCanvas(Canvas canvas, Rect rect, Color color)
    {
        GameObject rectGameObject = new GameObject();
        rectGameObject.transform.SetParent(canvas.transform);
        rectGameObject.transform.localPosition = Vector3.zero;
        rectGameObject.transform.localRotation = Quaternion.identity;
        rectGameObject.transform.localScale = Vector3.one;


        Image rectImage = rectGameObject.AddComponent<Image>();
        rectImage.color = color;


        RectTransform rectTransform = rectImage.GetComponent<RectTransform>();
        rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, rect.x, rect.width);
        rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, rect.y, rect.height);
    }
}