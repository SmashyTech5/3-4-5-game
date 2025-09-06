using System.Collections;
using UnityEngine;

/// <summary>
/// Rod cell used in Photon online mode. Contains grid coords and helper functions for the manager.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RodCellPhoton : MonoBehaviour
{
    public int maxBalls = 5;
    public float spacingPadding = 0.01f;

    [HideInInspector] public int currentBallCount = 0;
    private float ballHeight;
    [HideInInspector] public GameManagerPhoton gameManager;
    [HideInInspector] public int gridX, gridZ;

    private static MonoBehaviour coroutineHost;
    private Vector3 originalScale;

    public void Setup(GameManagerPhoton manager, int x, int z)
    {
        gameManager = manager;
        gridX = x;
        gridZ = z;

        if (coroutineHost == null) coroutineHost = manager;

        // Calculate ball height from prefab
        GameObject temp = null;
        if (manager != null && manager.player1BallPrefab != null)
        {
            temp = Instantiate(manager.player1BallPrefab);
            Renderer r = temp.GetComponent<Renderer>();
            ballHeight = (r != null) ? r.bounds.size.y : 0.2f;
            ballHeight += spacingPadding;
            Destroy(temp);
        }
        else
        {
            ballHeight = 0.2f;
        }

        // Force rod to accept manager's configured number
        maxBalls = (manager != null) ? manager.maxBallsPerRod : maxBalls;
        originalScale = transform.localScale;

        // Determine current renderer height (world space) with original scale
        Renderer rodRenderer = GetComponent<Renderer>();
        float originalWorldHeight = (rodRenderer != null) ? rodRenderer.bounds.size.y : 1f;
        if (originalWorldHeight <= 0.0001f) originalWorldHeight = 1f;

        // Desired world height = maxBalls * ballHeight
        float desiredWorldHeight = maxBalls * ballHeight;

        // Compute scale multiplier that makes renderer.bounds.size.y == desiredWorldHeight
        float scaleMultiplierY = desiredWorldHeight / originalWorldHeight;

        // Apply new localScale preserving X/Z multiplier
        transform.localScale = new Vector3(originalScale.x, originalScale.y * scaleMultiplierY, originalScale.z);

        // Position rod on top of base (recompute bounds after scale change)
        if (rodRenderer != null && manager != null && manager.baseObject != null)
        {
            // Recompute renderer after scale change by grabbing updated bounds
            rodRenderer = GetComponent<Renderer>();
            float baseTopY = manager.baseObject.GetComponent<Renderer>().bounds.max.y;
            float currentBottomY = rodRenderer.bounds.min.y;
            float offset = baseTopY - currentBottomY;
            transform.position += Vector3.up * offset;
        }
    }


    public Vector3 GetSpawnPosition(int yIndex)
    {
        Renderer rodRenderer = GetComponent<Renderer>();
        float rodBottomY;

        if (rodRenderer != null)
        {
            // Use renderer bounds (world space) for bottom
            rodBottomY = rodRenderer.bounds.min.y;
        }
        else
        {
            // Fallback: use transform.position as center and originalScale estimate
            rodBottomY = transform.position.y - (transform.localScale.y * 0.5f);
        }

        // place bead centers evenly spaced from bottom to top
        float yOffset = (yIndex + 0.5f) * ballHeight;
        return new Vector3(transform.position.x, rodBottomY + yOffset, transform.position.z);
    }


    public static void StartSmoothDropStatic(Transform ballTransform, Vector3 target)
    {
        if (coroutineHost != null)
            coroutineHost.StartCoroutine(SmoothDropCoroutine(ballTransform, target));
        else if (ballTransform != null)
            ballTransform.position = target;
    }

    private static IEnumerator SmoothDropCoroutine(Transform ball, Vector3 target)
    {
        float speed = 5f;
        if (ball == null) yield break;

        while (ball != null && Vector3.Distance(ball.position, target) > 0.01f)
        {
            ball.position = Vector3.MoveTowards(ball.position, target, Time.deltaTime * speed);
            yield return null;
        }
        if (ball != null) ball.position = target;
    }

    public void RecalculateBallCount(int playerId, int x, int z, GameManagerPhoton manager)
    {
        int count = 0;
        for (int y = 0; y < manager.maxBallsPerRod; y++)
        {
            if (manager.board[x, y, z] != 0) count = y + 1;
        }
        currentBallCount = count;

        // reset scale if maxBalls changed
        if (maxBalls != manager.maxBallsPerRod)
        {
            maxBalls = manager.maxBallsPerRod;

            Renderer rodRenderer = GetComponent<Renderer>();
            float originalWorldHeight = (rodRenderer != null) ? rodRenderer.bounds.size.y : 1f;
            if (originalWorldHeight <= 0.0001f) originalWorldHeight = 1f;

            float desiredWorldHeight = maxBalls * ballHeight;
            float scaleMultiplierY = desiredWorldHeight / originalWorldHeight;

            transform.localScale = new Vector3(originalScale.x, originalScale.y * scaleMultiplierY, originalScale.z);

            // reposition on base
            if (rodRenderer != null && manager.baseObject != null)
            {
                rodRenderer = GetComponent<Renderer>();
                float rodBottom = rodRenderer.bounds.min.y;
                float baseTopY = manager.baseObject.GetComponent<Renderer>().bounds.max.y;
                float offset = baseTopY - rodBottom;
                transform.position += Vector3.up * offset;
            }
        }
    }


    public float GetBallHeight() => ballHeight;
}
