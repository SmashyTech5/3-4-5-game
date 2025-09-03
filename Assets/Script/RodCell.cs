using System.Collections;
using UnityEngine;

public class RodCell : MonoBehaviour
{
    public int maxBalls = 5;
    public float spacingPadding = 0.01f;

    private int currentBallCount = 0;
    private float ballHeight;
    private GameManagerAi gameManager;
    private int gridX, gridZ;

    public void Setup(GameManagerAi manager, int x, int z)
    {
        gameManager = manager;
        gridX = x;
        gridZ = z;

        // Get ball height
        GameObject temp = Instantiate(manager.playerBallPrefab);
        Renderer r = temp.GetComponent<Renderer>();
        ballHeight = (r != null) ? r.bounds.size.y + spacingPadding : 0.2f;
        Destroy(temp);

        maxBalls = manager.maxBallsPerRod;

        // Scale rod
        Renderer rodRenderer = GetComponent<Renderer>();
        float currentHeight = rodRenderer.bounds.size.y;
        float targetHeight = maxBalls * ballHeight;
        float scaleFactor = targetHeight / currentHeight;
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * scaleFactor, transform.localScale.z);

        // Position so bottom sits on base exactly
        float baseTopY = manager.baseObject.GetComponent<Renderer>().bounds.max.y;
        float currentBottomY = rodRenderer.bounds.min.y;
        float offset = baseTopY - currentBottomY;
        transform.position += Vector3.up * offset;
    }




    public GameObject TryPlaceBall(int playerId)
    {
        if (currentBallCount >= maxBalls)
            return null;

        if (!gameManager.CanPlayerPlaceBall(playerId))
            return null;

        GameObject ballToUse = (playerId == 1) ? gameManager.playerBallPrefab : gameManager.aiBallPrefab;

        Renderer rodRenderer = GetComponent<Renderer>();
        float rodBottomY = rodRenderer.bounds.min.y;
        float yOffset = currentBallCount * ballHeight + (ballHeight / 2f);
        Vector3 spawnPos = new Vector3(transform.position.x, rodBottomY + yOffset + 2f, transform.position.z);
        Vector3 targetPos = new Vector3(transform.position.x, rodBottomY + yOffset, transform.position.z);

        GameObject ball = Instantiate(ballToUse, spawnPos, Quaternion.identity);
        StartCoroutine(SmoothDrop(ball.transform, targetPos));

        gameManager.ReportBallPlaced(gridX, currentBallCount, gridZ, playerId, ball);
        currentBallCount++;
        return ball;
    }

    IEnumerator SmoothDrop(Transform ball, Vector3 target)
    {
        float speed = 5f;

        while (ball != null && ball.gameObject != null && Vector3.Distance(ball.position, target) > 0.01f)
        {
            ball.position = Vector3.MoveTowards(ball.position, target, Time.deltaTime * speed);
            yield return null;
        }

        if (ball != null && ball.gameObject != null)
            ball.position = target;
    }


    public void RecalculateBallCount(int playerId, int x, int z, GameManagerAi manager)
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

        // --- Also adjust rod height if round capacity changed ---
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