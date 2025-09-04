using System.Collections;
using UnityEngine;

/// <summary>
/// Rod cell used in Photon online mode. Contains grid coords and helper functions for the manager.
/// When clicked locally, the rod will request placement via the GameManagerPhoton (client -> Master).
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

    // used for static drop helper
    private static MonoBehaviour coroutineHost;

    public void Setup(GameManagerPhoton manager, int x, int z)
    {
        gameManager = manager;
        gridX = x;
        gridZ = z;

        // seed coroutine host for static helper if not set
        if (coroutineHost == null) coroutineHost = manager;

        // Get ball height using a temp of player1Ball (like before)
        GameObject temp = null;
        if (manager != null)
        {
            // try to load from Resources (we expect prefab names to be in Resources)
            GameObject prefab = Resources.Load<GameObject>(manager.player1BallPrefabName);
            if (prefab != null) temp = Instantiate(prefab);
            else temp = null;
        }

        if (temp != null)
        {
            Renderer r = temp.GetComponent<Renderer>();
            ballHeight = (r != null) ? r.bounds.size.y + spacingPadding : 0.2f;
            Destroy(temp);
        }
        else
        {
            ballHeight = 0.2f;
        }

        maxBalls = manager.maxBallsPerRod;

        // scale rod height to fit capacity (same as local)
        Renderer rodRenderer = GetComponent<Renderer>();
        float currentHeight = rodRenderer.bounds.size.y;
        float targetHeight = maxBalls * ballHeight;
        float scaleFactor = targetHeight / currentHeight;
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * scaleFactor, transform.localScale.z);

        // position so it sits on base
        float baseTopY = manager.baseObject.GetComponent<Renderer>().bounds.max.y;
        float currentBottomY = rodRenderer.bounds.min.y;
        float offset = baseTopY - currentBottomY;
        transform.position += Vector3.up * offset;
    }

    // Called locally by raycast when clicked; we just request placement via manager (which will send RPC to Master)
    public void RequestPlaceLocal()
    {
        if (gameManager == null) return;
        // Manager handles checking turn / locking / sending to Master
        // But as convenience we call the GameManager's Update flow by sending RPC in GameManager (see manager update uses raycast)
        // Alternatively you can call manager.RequestPlaceByRod(this) if you implement it; current design uses GameManager's raycast in Update.
    }

    // Return spawn world above target Y (so ball will animate dropping)
    public Vector3 GetSpawnPosition(int yIndex)
    {
        Renderer rodRenderer = GetComponent<Renderer>();
        float rodBottomY = rodRenderer.bounds.min.y;
        float yOffset = yIndex * ballHeight + (ballHeight / 2f);
        // spawn a bit above
        return new Vector3(transform.position.x, rodBottomY + yOffset + 2f, transform.position.z);
    }

    // Smooth drop helper (static): launch coroutine to move a transform to target
    public static void StartSmoothDropStatic(Transform ballTransform, Vector3 target)
    {
        if (coroutineHost != null)
        {
            coroutineHost.StartCoroutine(SmoothDropCoroutine(ballTransform, target));
        }
        else
        {
            // fallback immediate placement
            if (ballTransform != null) ballTransform.position = target;
        }
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

    // Called by the manager when recalculating counts after gravity
    public void RecalculateBallCount(int playerId, int x, int z, GameManagerPhoton manager)
    {
        int count = 0;
        for (int y = 0; y < manager.maxBallsPerRod; y++)
        {
            if (manager.board[x, y, z] != 0)
            {
                count = y + 1;
            }
        }
        currentBallCount = count;

        if (maxBalls != manager.maxBallsPerRod)
        {
            maxBalls = manager.maxBallsPerRod;

            Renderer rodRenderer = GetComponent<Renderer>();
            float currentHeight = rodRenderer.bounds.size.y;
            float targetHeight = maxBalls * ballHeight;

            float scaleFactor = targetHeight / currentHeight;
            Vector3 oldScale = transform.localScale;
            transform.localScale = new Vector3(oldScale.x, oldScale.y * scaleFactor, oldScale.z);

            float heightDiff = targetHeight - currentHeight;
            transform.position += Vector3.up * (heightDiff / 2f);
        }
    }

    public float GetBallHeight()
    {
        return ballHeight;
    }
}
