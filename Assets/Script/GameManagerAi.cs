using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro; // only if you are using TextMeshPro

public class GameManagerAi : MonoBehaviour
{
    [Header("Grid Setup")]
    public RodCell rodPrefab;
    public Transform rodParent;
    public GameObject baseObject;
    public GameObject playerBallPrefab;
    public GameObject aiBallPrefab;
    public int gridSize = 5;
    public int maxBallsPerRod = 5;

    [Header("Game Config")]
    [SerializeField] private int totalBeats = 125;
    private int usedBeats = 0;

    private int player1Score = 0;
    private int player2Score = 0;
    private int currentPlayerId = 1;
    private int lastScoredPlayerId = 1; // NEW: Track last scoring player

    [Header("Game State Info (Debug Only)")]
    [SerializeField] private int currentTurnPlayer = 1;
    [SerializeField] private int remainingBeats = 0;
    [SerializeField] private int p1Score = 0;
    [SerializeField] private int p2Score = 0;

    public int[,,] board;
    private GameObject[,,] ballObjects;
    private RodCell[,] rods;

    private bool isTurnLocked = false;
    [Header("Turn UI")]
    public Image playerHighlight;
    public Image aiHighlight;
    public Image playerTurnImage; // make sure this is the Image component, not just GameObject
    public Image aiTurnImage;

    [Header("Highlight Colors")]
    public Color activeColor = Color.white; // alpha 1
    public Color inactiveColor = new Color(1, 1, 1, 0.3f); // alpha 0.3


    [Header("Turn Image Fade")]
    public float fadeDuration = 0.5f;
    [Header("End Game UI")]
    public Image youWinImage;
    public Image youLoseImage;
    public Image tieImage; // optional, for draws

    private bool gameOver = false; // NEW flag

    [Header("Rod Spacing")]
    public float rodSpacingMultiplier = 1.3f;  // Added spacing control
    [Header("Last Move Panel")]
    public GameObject lastMovePanel;
    public float lastMovePanelDuration = 2f;
    private Coroutine lastMovePanelRoutine;
    [Header("Last Two Turns Indicator")]
    private bool lastTwoTurnsShown = false;
    public GameObject ExitPanel;
    [Header("Score UI")]
    public TextMeshProUGUI playerScoreText;
    public TextMeshProUGUI aiScoreText;



    void Start()
    {
        AdManager.Instance.DisplayBanner();
        remainingBeats = totalBeats;
        currentTurnPlayer = currentPlayerId;
        GenerateBoard();

        // Start with highlights hidden
        playerHighlight.enabled = false;
        aiHighlight.enabled = false;
        playerTurnImage.enabled = false;
        aiTurnImage.enabled = false;

        // Delay showing highlights to avoid flash on scene load
        StartCoroutine(ShowHighlightsAfterDelay(0.5f));
        if (lastMovePanel != null)
            lastMovePanel.SetActive(false);
        UpdateScoreUI();


    }
    private IEnumerator ShowHighlightsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Now update UI as normal
        playerHighlight.enabled = (currentPlayerId == 1);
        aiHighlight.enabled = (currentPlayerId == 2);

        playerTurnImage.enabled = true;
        aiTurnImage.enabled = true;

        playerTurnImage.color = (currentPlayerId == 1) ? activeColor : new Color(1, 1, 1, 0);
        aiTurnImage.color = (currentPlayerId == 2) ? activeColor : new Color(1, 1, 1, 0);
    }
    void GenerateBoard()
    {
        board = new int[gridSize, maxBallsPerRod, gridSize];
        ballObjects = new GameObject[gridSize, maxBallsPerRod, gridSize];
        rods = new RodCell[gridSize, gridSize];

        Renderer baseRenderer = baseObject.GetComponent<Renderer>();
        Bounds baseBounds = baseRenderer.bounds;
        float width = baseBounds.size.x;
        float depth = baseBounds.size.z;

        // ====== KEY MODIFICATION START ====== //
        // Calculate base spacing without multiplier
        float baseSpacingX = width / (gridSize + 1);
        float baseSpacingZ = depth / (gridSize + 1);

        // Apply spacing multiplier
        float spacingX = baseSpacingX * rodSpacingMultiplier;
        float spacingZ = baseSpacingZ * rodSpacingMultiplier;

        // Adjust start position to center rods with new spacing
        float totalWidth = spacingX * (gridSize - 1);
        float totalDepth = spacingZ * (gridSize - 1);
        float startX = baseBounds.center.x - totalWidth / 2;
        float startZ = baseBounds.center.z - totalDepth / 2;
        // ====== KEY MODIFICATION END ====== //

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                // Calculate position with new spacing
                float xPos = startX + x * spacingX;
                float zPos = startZ + z * spacingZ;

                RodCell rod = Instantiate(rodPrefab, Vector3.zero, Quaternion.identity, rodParent);
                rod.Setup(this, x, z);

                // Position rod
                rod.transform.position = new Vector3(xPos, rod.transform.position.y, zPos);
                rods[x, z] = rod;
            }
        }
    }


    void Update()
    {
        if (gameOver) return;
        if (isTurnLocked) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                RodCell rod = hit.collider.GetComponent<RodCell>();
                if (rod != null && currentPlayerId == 1) // only player's turn
                {
                    isTurnLocked = true;
                    rod.TryPlaceBall(currentPlayerId); // ✅ no more special case here
                }
            }
        }

        UpdateTurnUI();
    }



    public bool CanPlayerPlaceBall(int playerId)
    {
        return usedBeats < totalBeats;
    }

    public void ReportBallPlaced(int x, int y, int z, int playerId, GameObject ballObj)
    {
        board[x, y, z] = playerId;
        ballObjects[x, y, z] = ballObj;

        usedBeats++;
        remainingBeats = totalBeats - usedBeats;

        // ✅ Show last two turns panel BEFORE the very last turn
        if (remainingBeats == 3 && !lastTwoTurnsShown && lastMovePanel != null)
        {
            lastTwoTurnsShown = true;
            ShowLastMovePanelNow();
        }

        List<Vector3Int> winningBalls = GetWinningPositions(playerId);
        if (winningBalls != null && winningBalls.Count > 0)
        {
            lastScoredPlayerId = playerId;

            if (playerId == 1)
            {
                player1Score++;
                p1Score = player1Score;
            }
            else
            {
                player2Score++;
                p2Score = player2Score;
            }
            UpdateScoreUI(); // ✅ update UI right after score
            AudioManager.Instance.PlayScore();   // 🔊 play match sound
            StartCoroutine(BlinkAndDestroy(winningBalls));
        }
        else
        {
            if (usedBeats >= totalBeats)
            {
                StartCoroutine(DeclareWinnerAfterDelay(0.5f));
            }
            else
            {
                currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
                currentTurnPlayer = currentPlayerId;

                if (currentPlayerId == 2)
                    StartCoroutine(DelayedDoAITurn(0.35f));
                else
                    isTurnLocked = false;
            }
        }
    }

    private IEnumerator DelayedDoAITurn(float delay)
    {
        yield return new WaitForSeconds(delay);

        // guard: if game over or no valid moves, do nothing
        if (usedBeats >= totalBeats) yield break;

        var valid = GetValidMoves();
        if (valid == null || valid.Count == 0) yield break;

        DoAITurn();
    }
    private IEnumerator DeclareWinnerAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        DeclareWinner();
    }

    IEnumerator BlinkAndDestroy(List<Vector3Int> winningBalls)
    {
        yield return new WaitForSeconds(0.2f);

        float blinkInterval = 0.2f;
        int blinkCount = 2;
        List<Renderer> renderers = new();

        foreach (var pos in winningBalls)
        {
            GameObject b = ballObjects[pos.x, pos.y, pos.z];
            if (b != null)
            {
                Renderer r = b.GetComponent<Renderer>();
                if (r != null) renderers.Add(r);
            }
        }

        for (int i = 0; i < blinkCount; i++)
        {
            foreach (var r in renderers)
            {
                if (r != null && r.gameObject != null)
                    r.enabled = false;
            }
            yield return new WaitForSeconds(blinkInterval);

            foreach (var r in renderers)
            {
                if (r != null && r.gameObject != null)
                    r.enabled = true;
            }
            yield return new WaitForSeconds(blinkInterval);
        }

        foreach (var pos in winningBalls)
        {
            GameObject obj = ballObjects[pos.x, pos.y, pos.z];
            if (obj != null)
            {
                Destroy(obj);
            }
            board[pos.x, pos.y, pos.z] = 0;
            ballObjects[pos.x, pos.y, pos.z] = null;
        }

        yield return new WaitForSeconds(0.2f);

        // ✅ Prevent repeated scoring after cascade
        int scoredPlayerBeforeGravity = lastScoredPlayerId;

        ApplyGravity();

        // Clear last scorer only if no more matches found after cascade
        StartCoroutine(ClearLastScorerAfterCascade(scoredPlayerBeforeGravity));
    }
    IEnumerator ClearLastScorerAfterCascade(int previousScorer)
    {
        yield return new WaitForSeconds(0.3f); // Wait slightly longer than ApplyGravity check

        // Check again for matching positions
        List<Vector3Int> stillMatching = GetWinningPositions(previousScorer);

        if (stillMatching == null || stillMatching.Count == 0)
        {
            lastScoredPlayerId = 0; // ✅ Now it's safe to clear
        }
    }


    void ApplyGravity()
    {
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                int writeY = 0;
                for (int readY = 0; readY < maxBallsPerRod; readY++)
                {
                    if (board[x, readY, z] != 0)
                    {
                        if (writeY != readY)
                        {
                            board[x, writeY, z] = board[x, readY, z];
                            board[x, readY, z] = 0;

                            GameObject ball = ballObjects[x, readY, z];
                            ballObjects[x, writeY, z] = ball;
                            ballObjects[x, readY, z] = null;

                            // ✅ Use actual ball height from the rod
                            float ballHeight = rods[x, z].GetBallHeight();
                            Renderer rodRenderer = rods[x, z].GetComponent<Renderer>();
                            float rodBottomY = rodRenderer.bounds.min.y;
                            Vector3 targetPos = new Vector3(
                                rods[x, z].transform.position.x,
                                rodBottomY + writeY * ballHeight + (ballHeight / 2f),
                                rods[x, z].transform.position.z
                            );
                            StartCoroutine(SmoothDrop(ball.transform, targetPos));
                        }
                        writeY++;
                    }
                }
                rods[x, z].RecalculateBallCount(currentPlayerId, x, z, this);
            }
        }

        StartCoroutine(CheckCascadeAfterDelay(0.25f));
    }



    IEnumerator SmoothDrop(Transform ball, Vector3 target)
    {
        float speed = 5f;

        // Check for destroyed object before starting loop
        if (ball == null)
            yield break;

        while (ball != null && Vector3.Distance(ball.position, target) > 0.01f)
        {
            ball.position = Vector3.MoveTowards(ball.position, target, Time.deltaTime * speed);
            yield return null;
        }

        if (ball != null)
            ball.position = target;
    }


    private IEnumerator CheckCascadeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (lastScoredPlayerId == 0)
            yield break;

        List<Vector3Int> newMatches = GetWinningPositions(lastScoredPlayerId);
        if (newMatches != null && newMatches.Count > 0)
        {
            // award score for cascade
            if (lastScoredPlayerId == 1)
            {
                player1Score++;
                p1Score = player1Score;
            }
            else
            {
                player2Score++;
                p2Score = player2Score;
            }
            UpdateScoreUI(); // ✅ keep UI in sync
            AudioManager.Instance.PlayScore();   // 🔊 play match sound on cascade too
            StartCoroutine(BlinkAndDestroy(newMatches));
        }
        else
        {
            // no more matches -> next turn
            if (usedBeats >= totalBeats)
            {
                DeclareWinner();
            }
            else
            {
                lastScoredPlayerId = 0;
                currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
                currentTurnPlayer = currentPlayerId;

                if (currentPlayerId == 2)
                {
                    StartCoroutine(DelayedDoAITurn(0.35f));
                }
                else
                {
                    isTurnLocked = false; // ✅ always unlock when player turn comes back
                }
            }
        }
    }


    private void DeclareWinner()
    {
        if (lastMovePanel != null)
            lastMovePanel.SetActive(false);

        gameOver = true;
        StopAllFades();

        playerHighlight.enabled = false;
        aiHighlight.enabled = false;
        playerTurnImage.enabled = false;
        aiTurnImage.enabled = false;

        youWinImage.gameObject.SetActive(false);
        youLoseImage.gameObject.SetActive(false);
        if (tieImage != null) tieImage.gameObject.SetActive(false);

        int pot = PlayerPrefs.GetInt("PotCoins", 0);
        int playerCoins = PlayerPrefs.GetInt("PlayerCoins", 0);

        if (player1Score > player2Score)
        {
            youWinImage.gameObject.SetActive(true);
            playerCoins += pot; // player wins pot
                                // 🔊 Play win sound
            AudioManager.Instance.PlayWin();
        }
        else if (player2Score > player1Score)
        {
            youLoseImage.gameObject.SetActive(true);
            // AI wins → player gets nothing
            // 🔊 Play lose sound
            AudioManager.Instance.PlayLose();
        }
        else if (tieImage != null)
        {
            tieImage.gameObject.SetActive(true);
            // return player's 100 coins
            playerCoins += pot / 2;
            // 🔊 Play tie sound
            AudioManager.Instance.PlayTie();
        }

        PlayerPrefs.SetInt("PlayerCoins", playerCoins);
        PlayerPrefs.DeleteKey("PotCoins"); // clear pot
        PlayerPrefs.Save();

        isTurnLocked = true;
        // 👇 Show Interstitial Ad after result
        AdManager.Instance.DisplayInterstitialWithLoading();
    }

    private List<Vector3Int> GetWinningPositions(int playerId)
    {
        List<Vector3Int> allMatches = new List<Vector3Int>();

        int requiredLength = gridSize; // full line length (like multiplayer)

        Vector3Int[] directions = new Vector3Int[]
        {
        new Vector3Int(1, 0, 0),   // X
        new Vector3Int(0, 0, 1),   // Z
        new Vector3Int(0, 1, 0),   // Y
        new Vector3Int(1, 0, 1),   // Diagonal XZ down-right
        new Vector3Int(1, 0, -1)   // Diagonal XZ down-left
        };

        foreach (var dir in directions)
        {
            int maxX = (dir.x == 1) ? gridSize - requiredLength : gridSize - 1;
            int maxY = (dir.y == 1) ? maxBallsPerRod - requiredLength : maxBallsPerRod - 1;
            int maxZ = (dir.z == 1) ? gridSize - requiredLength : gridSize - 1;

            int minX = (dir.x == -1) ? requiredLength - 1 : 0;
            int minY = (dir.y == -1) ? requiredLength - 1 : 0;
            int minZ = (dir.z == -1) ? requiredLength - 1 : 0;

            for (int startX = minX; startX <= maxX; startX++)
            {
                for (int startY = minY; startY <= maxY; startY++)
                {
                    for (int startZ = minZ; startZ <= maxZ; startZ++)
                    {
                        List<Vector3Int> match = new List<Vector3Int>();

                        for (int step = 0; step < requiredLength; step++)
                        {
                            int nx = startX + dir.x * step;
                            int ny = startY + dir.y * step;
                            int nz = startZ + dir.z * step;

                            if (nx < 0 || nx >= gridSize || ny < 0 || ny >= maxBallsPerRod || nz < 0 || nz >= gridSize)
                            {
                                match.Clear();
                                break;
                            }

                            if (board[nx, ny, nz] != playerId)
                            {
                                match.Clear();
                                break;
                            }

                            match.Add(new Vector3Int(nx, ny, nz));
                        }

                        if (match.Count == requiredLength)
                            allMatches.AddRange(match);
                    }
                }
            }
        }

        return allMatches.Count > 0 ? allMatches : null;
    }



    //ai turn
    private void DoAITurn()
    {
        // ensure it's actually AI's turn
        if (currentPlayerId != 2) return;

        StartCoroutine(AIMoveCoroutine());
    }

    // ---------------------- AIMove Coroutine ----------------------
    // ---------------------- AIMove Coroutine (replace your current one) ----------------------
    private IEnumerator AIMoveCoroutine()
    {
        // thinking delay (feels natural)
        yield return new WaitForSeconds(Random.Range(0.35f, 0.8f));

        var validMoves = GetValidMoves();
        if (validMoves == null || validMoves.Count == 0)
        {
            Debug.Log("AI: no valid moves available");
            yield break;
        }

        Vector2Int chosenRod = new Vector2Int(-1, -1);
        int roll = Random.Range(0, 100);

        // increase chance to be "hard" so it's actually challenging
        if (roll < 20) // Easy behavior (20%)
        {
            chosenRod = GetRandomMove();
        }
        else if (roll < 50) // Medium (30%)
        {
            chosenRod = GetBlockingOrMatchingMove();
        }
        else // Hard (50%)
        {
            chosenRod = GetBestScoringMove(); // now deeper evaluation
        }

        // safe fallback
        if (chosenRod.x == -1)
            chosenRod = GetRandomMove();

        if (chosenRod.x == -1)
        {
            Debug.Log("AI: fallback also failed (no move).");
            yield break;
        }

        Debug.Log($"AI choosing rod {chosenRod.x},{chosenRod.y} (roll {roll})");
        if (remainingBeats == 1)
        {
            // AI plays its final move after panel was already shown on player’s turn
            rods[chosenRod.x, chosenRod.y].TryPlaceBall(2);
        }
        else
        {
            rods[chosenRod.x, chosenRod.y].TryPlaceBall(2);
        }



        // rods[chosenRod.x, chosenRod.y].TryPlaceBall(2);
    }
    private IEnumerator DoFinalMoveWithPanel(RodCell rod, int playerId)
    {
        isTurnLocked = true;
        ShowLastMovePanelNow();
        yield return new WaitForSeconds(lastMovePanelDuration);

        if (rod != null)
            rod.TryPlaceBall(playerId);
    }




    // ---------------------- Move selection helpers ----------------------
    private Vector2Int GetRandomMove()
    {
        var validMoves = GetValidMoves();
        if (validMoves == null || validMoves.Count == 0) return new Vector2Int(-1, -1);
        return validMoves[Random.Range(0, validMoves.Count)];
    }

    private Vector2Int GetBlockingOrMatchingMove()
    {
        // 1) try to block opponent win
        Vector2Int blockMove = FindThreatMove(1);
        if (blockMove.x != -1) return blockMove;

        // 2) try to make a match for itself
        Vector2Int matchMove = FindThreatMove(2);
        if (matchMove.x != -1) return matchMove;

        // 3) fallback
        return GetRandomMove();
    }

    // ---------------------- GetBestScoringMove (deep sim) ----------------------
    private Vector2Int GetBestScoringMove()
    {
        var validMoves = GetValidMoves();
        if (validMoves == null || validMoves.Count == 0) return new Vector2Int(-1, -1);

        Vector2Int bestMove = new Vector2Int(-1, -1);
        float bestValue = float.NegativeInfinity;

        foreach (var move in validMoves)
        {
            float value = SimulateMoveScore(move.x, move.y, 2); // returns composite score (higher = better)
            if (value > bestValue)
            {
                bestValue = value;
                bestMove = move;
            }
        }

        return bestMove.x == -1 ? GetRandomMove() : bestMove;
    }



    private List<Vector2Int> GetValidMoves()
    {
        List<Vector2Int> moves = new List<Vector2Int>();
        if (board == null) return moves;

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                // if there's at least one empty y in this rod, it's a valid move
                if (GetFirstEmptyY(x, z) != -1)
                    moves.Add(new Vector2Int(x, z));
            }
        }
        return moves;
    }

    // ---------------------- Threat/Simulate helpers ----------------------
    private Vector2Int FindThreatMove(int playerId)
    {
        foreach (var move in GetValidMoves())
        {
            int y = GetFirstEmptyY(move.x, move.y);
            if (y == -1) continue;

            // simulate
            board[move.x, y, move.y] = playerId;
            bool wins = GetWinningPositions(playerId) != null;
            // undo
            board[move.x, y, move.y] = 0;

            if (wins) return move;
        }
        return new Vector2Int(-1, -1);
    }

    // ---------------------- SimulateMoveScore (deep) ----------------------
    private float SimulateMoveScore(int x, int z, int playerId)
    {
        // Copy board to temp
        int[,,] temp = new int[gridSize, maxBallsPerRod, gridSize];
        for (int ix = 0; ix < gridSize; ix++)
            for (int iy = 0; iy < maxBallsPerRod; iy++)
                for (int iz = 0; iz < gridSize; iz++)
                    temp[ix, iy, iz] = board[ix, iy, iz];

        int y = GetFirstEmptyYOnBoard(temp, x, z);
        if (y == -1) return float.NegativeInfinity; // invalid

        // Place the piece
        temp[x, y, z] = playerId;

        // 1) simulate cascades for the placed player, count cascaded clears
        int cascadeScore = SimulateAllCascadesAndScore(temp, playerId); // returns number of cleared cells (or matches)

        // 2) heuristic: partial lines / potential for player minus opponent potential
        float heuristic = EvaluateBoardHeuristic(temp, playerId);

        // 3) simulate opponent best reply (one-ply): highest cascade/opportunity opponent can get
        int opponentId = (playerId == 1) ? 2 : 1;
        int opponentMax = SimulateOpponentMaxScore(temp, opponentId);

        // Combine: weights tuned to favour cascades heavily, then positional value, and penalize giving opponent big reply
        float finalScore = cascadeScore * 150f + heuristic - opponentMax * 180f;

        return finalScore;
    }


    private int GetFirstEmptyY(int x, int z)
    {
        for (int y = 0; y < maxBallsPerRod; y++)
            if (board[x, y, z] == 0) return y;
        return -1;
    }



    //
    // ---------------------- Helpers for simulation and heuristics ----------------------
    private int GetFirstEmptyYOnBoard(int[,,] b, int x, int z)
    {
        for (int y = 0; y < maxBallsPerRod; y++)
            if (b[x, y, z] == 0) return y;
        return -1;
    }

    private List<Vector2Int> GetValidMovesOnBoard(int[,,] b)
    {
        List<Vector2Int> moves = new List<Vector2Int>();
        for (int x = 0; x < gridSize; x++)
            for (int z = 0; z < gridSize; z++)
                if (GetFirstEmptyYOnBoard(b, x, z) != -1)
                    moves.Add(new Vector2Int(x, z));
        return moves;
    }

    private void ApplyGravityOnBoard(int[,,] b)
    {
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                int writeY = 0;
                for (int readY = 0; readY < maxBallsPerRod; readY++)
                {
                    if (b[x, readY, z] != 0)
                    {
                        if (writeY != readY)
                        {
                            b[x, writeY, z] = b[x, readY, z];
                            b[x, readY, z] = 0;
                        }
                        writeY++;
                    }
                }
            }
        }
    }

    private List<Vector3Int> GetWinningPositionsOnBoard(int[,,] b, int playerId)
    {
        // same logic as your GetWinningPositions but using b
        for (int y = 0; y < maxBallsPerRod; y++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                List<Vector3Int> match = new();
                for (int x = 0; x < gridSize; x++)
                {
                    if (b[x, y, z] == playerId) match.Add(new Vector3Int(x, y, z));
                    else match.Clear();
                    if (match.Count == gridSize) return match;
                }
            }

            for (int x = 0; x < gridSize; x++)
            {
                List<Vector3Int> match = new();
                for (int z = 0; z < gridSize; z++)
                {
                    if (b[x, y, z] == playerId) match.Add(new Vector3Int(x, y, z));
                    else match.Clear();
                    if (match.Count == gridSize) return match;
                }
            }

            List<Vector3Int> diag1 = new();
            for (int i = 0; i < gridSize; i++)
            {
                if (b[i, y, i] == playerId) diag1.Add(new Vector3Int(i, y, i));
                else diag1.Clear();
            }
            if (diag1.Count == gridSize) return diag1;

            List<Vector3Int> diag2 = new();
            for (int i = 0; i < gridSize; i++)
            {
                if (b[i, y, gridSize - 1 - i] == playerId) diag2.Add(new Vector3Int(i, y, gridSize - 1 - i));
                else diag2.Clear();
            }
            if (diag2.Count == gridSize) return diag2;
        }

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                List<Vector3Int> rodMatch = new();
                for (int y = 0; y < maxBallsPerRod; y++)
                {
                    if (b[x, y, z] == playerId) rodMatch.Add(new Vector3Int(x, y, z));
                    else rodMatch.Clear();
                    if (rodMatch.Count == maxBallsPerRod) return rodMatch;
                }
            }
        }

        return null;
    }

    private int SimulateAllCascadesAndScore(int[,,] b, int playerId)
    {
        int totalCleared = 0;
        while (true)
        {
            List<Vector3Int> wins = GetWinningPositionsOnBoard(b, playerId);
            if (wins == null || wins.Count == 0) break;

            totalCleared += wins.Count;
            foreach (var p in wins)
                b[p.x, p.y, p.z] = 0;

            ApplyGravityOnBoard(b);
        }
        return totalCleared;
    }

    private int SimulateOpponentMaxScore(int[,,] b, int opponentId)
    {
        int maxScore = 0;
        var oppMoves = GetValidMovesOnBoard(b);
        foreach (var m in oppMoves)
        {
            int[,,] temp2 = new int[gridSize, maxBallsPerRod, gridSize];
            for (int ix = 0; ix < gridSize; ix++)
                for (int iy = 0; iy < maxBallsPerRod; iy++)
                    for (int iz = 0; iz < gridSize; iz++)
                        temp2[ix, iy, iz] = b[ix, iy, iz];

            int y2 = GetFirstEmptyYOnBoard(temp2, m.x, m.y);
            if (y2 == -1) continue;
            temp2[m.x, y2, m.y] = opponentId;
            int s = SimulateAllCascadesAndScore(temp2, opponentId);
            if (s > maxScore) maxScore = s;
            // early exit if opponent can get big cascade (speed optimization)
            if (maxScore >= gridSize) break;
        }
        return maxScore;
    }

    private float EvaluateBoardHeuristic(int[,,] b, int playerId)
    {
        // heuristic: reward partial lines (higher when more of player's pieces), penalize opponent potential
        int opponentId = (playerId == 1) ? 2 : 1;
        float score = 0f;

        // for each horizontal layer y
        for (int y = 0; y < maxBallsPerRod; y++)
        {
            // rows (x varies)
            for (int z = 0; z < gridSize; z++)
            {
                int pCount = 0, oCount = 0, empty = 0;
                for (int x = 0; x < gridSize; x++)
                {
                    if (b[x, y, z] == playerId) pCount++;
                    else if (b[x, y, z] == opponentId) oCount++;
                    else empty++;
                }
                if (oCount == 0) score += pCount * 12 + (gridSize - pCount) * 1;
                else if (pCount == 0) score -= oCount * 8; // penalize opponent occupancy
            }

            // columns (z varies)
            for (int x = 0; x < gridSize; x++)
            {
                int pCount = 0, oCount = 0, empty = 0;
                for (int z = 0; z < gridSize; z++)
                {
                    if (b[x, y, z] == playerId) pCount++;
                    else if (b[x, y, z] == opponentId) oCount++;
                    else empty++;
                }
                if (oCount == 0) score += pCount * 12 + (gridSize - pCount) * 1;
                else if (pCount == 0) score -= oCount * 8;
            }

            // diagonals
            int pd1 = 0, od1 = 0;
            for (int i = 0; i < gridSize; i++)
            {
                if (b[i, y, i] == playerId) pd1++;
                else if (b[i, y, i] == opponentId) od1++;
            }
            if (od1 == 0) score += pd1 * 16;
            else if (pd1 == 0) score -= od1 * 12;

            int pd2 = 0, od2 = 0;
            for (int i = 0; i < gridSize; i++)
            {
                if (b[i, y, gridSize - 1 - i] == playerId) pd2++;
                else if (b[i, y, gridSize - 1 - i] == opponentId) od2++;
            }
            if (od2 == 0) score += pd2 * 16;
            else if (pd2 == 0) score -= od2 * 12;
        }

        // verticals (rods): favour building up (player pieces stacked)
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                int pCount = 0, oCount = 0;
                for (int y = 0; y < maxBallsPerRod; y++)
                {
                    if (b[x, y, z] == playerId) pCount++;
                    else if (b[x, y, z] == opponentId) oCount++;
                }
                if (oCount == 0) score += pCount * 10;
                else if (pCount == 0) score -= oCount * 7;
            }
        }

        return score;
    }
    private Coroutine playerFadeRoutine;
    private Coroutine aiFadeRoutine;
    private Coroutine playerTurnFadeRoutine;
    private Coroutine aiTurnFadeRoutine;

    private void UpdateTurnUI()
    {
        if (gameOver) return;
        if (currentPlayerId == 1)
        {
            // Player's turn - show player highlight, hide AI highlight
            playerHighlight.enabled = true;
            aiHighlight.enabled = false;

            // Only fade the turn images
            StopTurnImageFades();

            // Ensure images are enabled before fading
            playerTurnImage.enabled = true;
            aiTurnImage.enabled = true;

            playerTurnFadeRoutine = StartCoroutine(FadeImage(playerTurnImage, activeColor));
            aiTurnFadeRoutine = StartCoroutine(FadeImage(aiTurnImage, new Color(1, 1, 1, 0)));
        }
        else
        {
            // AI's turn - show AI highlight, hide player highlight
            playerHighlight.enabled = false;
            aiHighlight.enabled = true;

            StopTurnImageFades();

            // Ensure images are enabled before fading
            playerTurnImage.enabled = true;
            aiTurnImage.enabled = true;

            playerTurnFadeRoutine = StartCoroutine(FadeImage(playerTurnImage, new Color(1, 1, 1, 0)));
            aiTurnFadeRoutine = StartCoroutine(FadeImage(aiTurnImage, activeColor));
        }
    }


    private void StopTurnImageFades()
    {
        if (playerTurnFadeRoutine != null)
        {
            StopCoroutine(playerTurnFadeRoutine);
            playerTurnFadeRoutine = null;
        }
        if (aiTurnFadeRoutine != null)
        {
            StopCoroutine(aiTurnFadeRoutine);
            aiTurnFadeRoutine = null;
        }
    }

    // Keep fade for turn images but add completion handler
    private IEnumerator FadeImage(Image img, Color targetColor)
    {
        // Ensure image is enabled
        img.enabled = true;

        Color startColor = img.color;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            img.color = Color.Lerp(startColor, targetColor, t / fadeDuration);
            yield return null;
        }

        img.color = targetColor;

        // Only disable if completely transparent
        if (targetColor.a <= 0.01f) // Small threshold to avoid floating point issues
        {
            img.enabled = false;
        }
    }


    private void StopAllFades()
    {
        if (playerFadeRoutine != null) StopCoroutine(playerFadeRoutine);
        if (aiFadeRoutine != null) StopCoroutine(aiFadeRoutine);
        if (playerTurnFadeRoutine != null) StopCoroutine(playerTurnFadeRoutine);
        if (aiTurnFadeRoutine != null) StopCoroutine(aiTurnFadeRoutine);
    }

    public void OnHomeButton()
    {
        // Go back to Main Menu
        SceneManager.LoadScene("MainMenu");
    }

    public void OnRetryButton()
    {
        // Reload current scene
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ShowLastMovePanelNow()
    {
        if (lastMovePanel == null) return;

        lastMovePanel.SetActive(true);

        if (lastMovePanelRoutine != null)
            StopCoroutine(lastMovePanelRoutine);

        lastMovePanelRoutine = StartCoroutine(HideLastMovePanelAfterDelay());
    }

    private IEnumerator HideLastMovePanelAfterDelay()
    {
        yield return new WaitForSeconds(lastMovePanelDuration);

        if (lastMovePanel != null)
            lastMovePanel.SetActive(false);

        lastMovePanelRoutine = null;
    }
    // ================= EXIT PANEL HANDLERS =================
    public void OpenExitPanel()
    {
        if (ExitPanel != null)
            ExitPanel.SetActive(true);
    }

    public void OnExitYes()
    {
#if UNITY_EDITOR
        // Stop play mode in editor
        UnityEditor.EditorApplication.isPlaying = false;
#else
    // Quit the game on device
    Application.Quit();
#endif
    }

    public void OnExitNo()
    {
        if (ExitPanel != null)
            ExitPanel.SetActive(false);
    }
    private void UpdateScoreUI()
    {
        if (playerScoreText != null)
            playerScoreText.text = "" + player1Score;

        if (aiScoreText != null)
            aiScoreText.text = "" + player2Score;
    }


}
