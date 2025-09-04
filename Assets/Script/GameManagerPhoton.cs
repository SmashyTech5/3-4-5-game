using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Photon authoritative Game Manager for 1v1 online play.
/// MasterClient = player 1, other player = player 2.
/// Clients request placements; MasterClient validates, instantiates balls via PhotonNetwork.Instantiate,
/// and broadcasts confirmed placements + cascade/destruction/motion to all clients.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class GameManagerPhoton : MonoBehaviourPunCallbacks
{
    [Header("Grid Setup")]
    public RodCellPhoton rodPrefab;
    public Transform rodParent;
    public GameObject baseObject;
    public string player1BallPrefabName; // prefab name used with PhotonNetwork.Instantiate (Resources folder or registered)
    public string player2BallPrefabName;
    public int gridSize = 5;
    public int maxBallsPerRod = 5;

    [Header("Game Config")]
    [SerializeField] private int totalBeats = 125;
    private int usedBeats = 0;

    private int player1Score = 0;
    private int player2Score = 0;
    private int currentPlayerId = 1; // 1 or 2 (1 starts)
    private int lastScoredPlayerId = 0;

    [Header("Game State Info (Debug Only)")]
    [SerializeField] private int currentTurnPlayer = 1;
    [SerializeField] private int remainingBeats = 0;
    [SerializeField] private int p1Score = 0;
    [SerializeField] private int p2Score = 0;

    // Authoritative board kept on MasterClient; clients keep a mirrored copy updated via RPCs
    public int[,,] board;
    private GameObject[,,] ballObjects; // mirrored objects (mapped by MasterClient confirm using PhotonView.Find)
    private RodCellPhoton[,] rods;

    private bool isTurnLocked = false;

    [Header("Turn UI")]
    public Image player1Highlight;
    public Image player2Highlight;
    public Image player1TurnImage;
    public Image player2TurnImage;

    [Header("Highlight Colors")]
    public Color activeColor = Color.white;
    public Color inactiveColor = new Color(1, 1, 1, 0.3f);

    [Header("Turn Image Fade")]
    public float fadeDuration = 0.5f;

    [Header("End Game UI")]
    public Image player1WinImage;
    public Image player2WinImage;
    public Image tieImage;

    private bool gameOver = false;

    [Header("Rod Spacing")]
    public float rodSpacingMultiplier = 1.3f;

    [Header("Last Move Panel")]
    public GameObject lastMovePanel;
    public float lastMovePanelDuration = 2f;
    private Coroutine lastMovePanelRoutine;
    private bool lastTwoTurnsShown = false;

    public GameObject ExitPanel;

    [Header("Score UI")]
    public TextMeshProUGUI player1ScoreText;
    public TextMeshProUGUI player2ScoreText;

    private PhotonView pv;
    // Fade coroutines (used for UI turn highlights)
    private Coroutine player1TurnFadeRoutine;
    private Coroutine player2TurnFadeRoutine;
    private Coroutine player1FadeRoutine;
    private Coroutine player2FadeRoutine;

    void Awake()
    {
        pv = GetComponent<PhotonView>();
    }

    void Start()
    {
        remainingBeats = totalBeats;
        currentTurnPlayer = currentPlayerId;
        GenerateBoard();

        // UI init
        player1Highlight.enabled = false;
        player2Highlight.enabled = false;
        player1TurnImage.enabled = false;
        player2TurnImage.enabled = false;
        StartCoroutine(ShowHighlightsAfterDelay(0.3f));
        if (lastMovePanel != null) lastMovePanel.SetActive(false);
        UpdateScoreUI();

        // Only MasterClient keeps authoritative board logic; clients will react to RPCs
        // If this client is not MasterClient, board will be kept in sync via PV RPCs
    }

    private IEnumerator ShowHighlightsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        player1Highlight.enabled = (currentPlayerId == 1);
        player2Highlight.enabled = (currentPlayerId == 2);

        player1TurnImage.enabled = true;
        player2TurnImage.enabled = true;

        player1TurnImage.color = (currentPlayerId == 1) ? activeColor : new Color(1, 1, 1, 0);
        player2TurnImage.color = (currentPlayerId == 2) ? activeColor : new Color(1, 1, 1, 0);
    }

    void GenerateBoard()
    {
        board = new int[gridSize, maxBallsPerRod, gridSize];
        ballObjects = new GameObject[gridSize, maxBallsPerRod, gridSize];
        rods = new RodCellPhoton[gridSize, gridSize];

        Renderer baseRenderer = baseObject.GetComponent<Renderer>();
        Bounds baseBounds = baseRenderer.bounds;
        float width = baseBounds.size.x;
        float depth = baseBounds.size.z;

        float baseSpacingX = width / (gridSize + 1);
        float baseSpacingZ = depth / (gridSize + 1);

        float spacingX = baseSpacingX * rodSpacingMultiplier;
        float spacingZ = baseSpacingZ * rodSpacingMultiplier;

        float totalWidth = spacingX * (gridSize - 1);
        float totalDepth = spacingZ * (gridSize - 1);
        float startX = baseBounds.center.x - totalWidth / 2;
        float startZ = baseBounds.center.z - totalDepth / 2;

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                float xPos = startX + x * spacingX;
                float zPos = startZ + z * spacingZ;

                RodCellPhoton rod = Instantiate(rodPrefab, Vector3.zero, Quaternion.identity, rodParent);
                rod.Setup(this, x, z);
                rod.transform.position = new Vector3(xPos, rod.transform.position.y, zPos);
                rods[x, z] = rod;
            }
        }
    }

    void Update()
    {
        if (gameOver) return;
        if (isTurnLocked) return;

        // determine local player's id: MasterClient => 1, otherwise 2
        int localPlayerId = PhotonNetwork.IsMasterClient ? 1 : 2;

        if (Input.GetMouseButtonDown(0))
        {
            // Only allow clicking if it's this client's turn
            if (localPlayerId != currentPlayerId) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                RodCellPhoton rod = hit.collider.GetComponent<RodCellPhoton>();
                if (rod != null)
                {
                    // request placement on MasterClient (if you're MasterClient this still works)
                    isTurnLocked = true;
                    pv.RPC(nameof(RPC_RequestPlaceBall), RpcTarget.MasterClient, rod.gridX, rod.gridZ, localPlayerId);
                }
            }
        }

        UpdateTurnUI();
    }

    // --------------------------
    // Client -> Master: Request placement
    // Master validates then executes placement and broadcasts confirm
    // --------------------------
    [PunRPC]
    private void RPC_RequestPlaceBall(int x, int z, int requestingPlayerId, PhotonMessageInfo info)
    {
        // Only MasterClient should execute this method (RpcTarget.MasterClient ensures that)
        if (!PhotonNetwork.IsMasterClient)
            return;

        // validate: it's the requesting player's turn and there is space
        if (requestingPlayerId != currentPlayerId)
        {
            // invalid: ignore (could send an RPC back to client to unlock if needed)
            // But we still unlock the requesting client locally by sending RPC to them
            pv.RPC(nameof(RPC_RejectRequest), info.Sender, "Not your turn or invalid");
            return;
        }

        int y = GetFirstEmptyY(x, z);
        if (y == -1)
        {
            pv.RPC(nameof(RPC_RejectRequest), info.Sender, "Column full");
            return;
        }

        // OK -> instantiate the correct prefab across network
        string prefabName = (requestingPlayerId == 1) ? player1BallPrefabName : player2BallPrefabName;
        // PhotonNetwork.Instantiate will create networked object for everyone
        Vector3 spawnWorld = rods[x, z].GetSpawnPosition(y); // above target spawn
        GameObject netBall = PhotonNetwork.Instantiate(prefabName, spawnWorld, Quaternion.identity);
        PhotonView ballPV = netBall.GetComponent<PhotonView>();
        int viewId = (ballPV != null) ? ballPV.ViewID : 0;

        // Now confirm to all clients: the ball belongs to this grid coordinate
        // We'll send target Y index so clients can animate drop to final position
        pv.RPC(nameof(RPC_ConfirmPlaceBall), RpcTarget.AllBuffered, x, y, z, requestingPlayerId, viewId);

        // Update authoritative board
        board[x, y, z] = requestingPlayerId;
        ballObjects[x, y, z] = netBall;
        usedBeats++;
        remainingBeats = totalBeats - usedBeats;

        // Process scoring/cascades on MasterClient
        ProcessMatchesAfterPlacement(requestingPlayerId);
    }

    [PunRPC]
    private void RPC_RejectRequest(string reason, PhotonMessageInfo info)
    {
        // Client requested invalid action; unlock their UI locally
        isTurnLocked = false;
        Debug.LogWarning($"Placement rejected: {reason}");
    }

    // Called on ALL clients when MasterClient confirms placement (viewId references the networked ball)
    [PunRPC]
    private void RPC_ConfirmPlaceBall(int x, int y, int z, int playerId, int ballViewId)
    {
        // find the network instantiated object by view id
        GameObject ballObj = null;
        if (ballViewId != 0)
        {
            try
            {
                PhotonView found = PhotonView.Find(ballViewId);
                if (found != null) ballObj = found.gameObject;
            }
            catch
            {
                ballObj = null;
            }
        }

        // If for some reason the ball hasn't been created locally (edge), we simply create a placeholder locally (non-networked)
        if (ballObj == null)
        {
            // fallback local instantiate (visual only)
            string prefabName = (playerId == 1) ? player1BallPrefabName : player2BallPrefabName;
            // try to load from Resources (so keep same naming)
            GameObject prefab = Resources.Load<GameObject>(prefabName);
            if (prefab != null)
            {
                ballObj = Instantiate(prefab, rods[x, z].GetSpawnPosition(y), Quaternion.identity);
            }
            else
            {
                Debug.LogWarning("Ball prefab not found in Resources for fallback.");
            }
        }

        // Set local mirrored structures
        if (board == null) board = new int[gridSize, maxBallsPerRod, gridSize]; // safety
        board[x, y, z] = playerId;
        ballObjects[x, y, z] = ballObj;

        // Animate the drop to proper y position: compute world target pos and move smoothly
        float ballHeight = rods[x, z].GetBallHeight();
        Renderer rodRenderer = rods[x, z].GetComponent<Renderer>();
        float rodBottomY = rodRenderer.bounds.min.y;
        Vector3 targetWorld = new Vector3(
            rods[x, z].transform.position.x,
            rodBottomY + y * ballHeight + (ballHeight / 2f),
            rods[x, z].transform.position.z
        );

        // If networked ball existed, it's already at spawnWorld (above); animate to target
        if (ballObj != null)
        {
            RodCellPhoton.StartSmoothDropStatic(ballObj.transform, targetWorld);
        }

        // If this is not MasterClient, don't run scoring logic here; MasterClient will handle cascades
        // Unlock local input if this client is the one who made the move? We'll rely on Master to switch turns.
        // But to keep UI responsive, we keep isTurnLocked true until MasterClient sends next turn (handled later).
    }

    // Called by MasterClient after updating board/usedBeats when a placement occurred
    private void ProcessMatchesAfterPlacement(int placingPlayerId)
    {
        // Only MasterClient calls this
        if (!PhotonNetwork.IsMasterClient) return;

        // Check matches for placingPlayerId
        List<Vector3Int> wins = GetWinningPositions(placingPlayerId);
        if (wins != null && wins.Count > 0)
        {
            // award score
            lastScoredPlayerId = placingPlayerId;
            if (placingPlayerId == 1)
            {
                player1Score++;
                p1Score = player1Score;
            }
            else
            {
                player2Score++;
                p2Score = player2Score;
            }
            UpdateScoreUI();

            // Tell clients to blink & destroy these positions
            // We'll send list as groups of ints: x,y,z triples
            int[] flat = new int[wins.Count * 3];
            for (int i = 0; i < wins.Count; i++)
            {
                flat[i * 3] = wins[i].x;
                flat[i * 3 + 1] = wins[i].y;
                flat[i * 3 + 2] = wins[i].z;
            }
            pv.RPC(nameof(RPC_BlinkAndDestroy), RpcTarget.All, flat);

            // After destruction and gravity, MasterClient will continue to check cascades and broadcast moves.
            // We use coroutine to wait a bit then perform gravity on authoritative board and broadcast moves.
            StartCoroutine(MasterCascadeLoopAfterDelay(0.6f, placingPlayerId));
        }
        else
        {
            // no immediate match -> switch turn or end game
            if (usedBeats >= totalBeats)
            {
                pv.RPC(nameof(RPC_DeclareWinner), RpcTarget.All);
            }
            else
            {
                // switch turn and broadcast to all
                currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
                currentTurnPlayer = currentPlayerId;
                pv.RPC(nameof(RPC_SwitchTurn), RpcTarget.All, currentPlayerId);
            }
        }
    }

    // Master loop to handle cascades: apply gravity on authoritative board, compute moved balls and broadcast moves,
    // then check for new matches; repeat until no matches
    private IEnumerator MasterCascadeLoopAfterDelay(float delay, int scoredPlayer)
    {
        yield return new WaitForSeconds(delay);

        // apply gravity on authoritative board and compute moves
        bool movedSomething = ApplyGravityOnAuthoritativeAndBroadcastMoves();

        // small delay for clients to settle
        yield return new WaitForSeconds(0.25f);

        // check for new matches for the scored player
        List<Vector3Int> newWins = GetWinningPositions(scoredPlayer);
        if (newWins != null && newWins.Count > 0)
        {
            // award score for cascade
            if (scoredPlayer == 1) { player1Score++; p1Score = player1Score; }
            else { player2Score++; p2Score = player2Score; }
            UpdateScoreUI();

            int[] flat = new int[newWins.Count * 3];
            for (int i = 0; i < newWins.Count; i++)
            {
                flat[i * 3] = newWins[i].x;
                flat[i * 3 + 1] = newWins[i].y;
                flat[i * 3 + 2] = newWins[i].z;
            }
            pv.RPC(nameof(RPC_BlinkAndDestroy), RpcTarget.All, flat);

            // continue cascade loop
            StartCoroutine(MasterCascadeLoopAfterDelay(0.45f, scoredPlayer));
        }
        else
        {
            // no more cascades -> continue game
            if (usedBeats >= totalBeats)
            {
                pv.RPC(nameof(RPC_DeclareWinner), RpcTarget.All);
            }
            else
            {
                lastScoredPlayerId = 0;
                currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
                currentTurnPlayer = currentPlayerId;
                pv.RPC(nameof(RPC_SwitchTurn), RpcTarget.All, currentPlayerId);
            }
        }
    }

    // Apply gravity on authoritative board; broadcast ball movements as (viewId, targetWorldPosition)
    // Returns true if any ball moved.
    private bool ApplyGravityOnAuthoritativeAndBroadcastMoves()
    {
        if (!PhotonNetwork.IsMasterClient) return false;

        List<(int viewId, Vector3 target)> moves = new List<(int, Vector3)>();

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
                            // move in authoritative board
                            board[x, writeY, z] = board[x, readY, z];
                            board[x, readY, z] = 0;

                            // move GameObject mapping too
                            GameObject ball = ballObjects[x, readY, z];
                            ballObjects[x, writeY, z] = ball;
                            ballObjects[x, readY, z] = null;

                            // compute world target
                            float ballHeight = rods[x, z].GetBallHeight();
                            Renderer rodRenderer = rods[x, z].GetComponent<Renderer>();
                            float rodBottomY = rodRenderer.bounds.min.y;
                            Vector3 targetPos = new Vector3(
                                rods[x, z].transform.position.x,
                                rodBottomY + writeY * ballHeight + (ballHeight / 2f),
                                rods[x, z].transform.position.z
                            );

                            // if networked ball exists, get its view id to send move RPC
                            int viewId = 0;
                            if (ball != null)
                            {
                                PhotonView bPV = ball.GetComponent<PhotonView>();
                                if (bPV != null) viewId = bPV.ViewID;
                            }

                            moves.Add((viewId, targetPos));
                        }
                        writeY++;
                    }
                }
                // recalc rod counts - this affects next spawns
                rods[x, z].RecalculateBallCount(currentPlayerId, x, z, this);
            }
        }

        // Broadcast moves
        foreach (var m in moves)
        {
            // if viewId==0 it was a fallback local object; still broadcast by sending 0 and target position; client-side will teleport local object if found
            pv.RPC(nameof(RPC_MoveBallTo), RpcTarget.All, m.viewId, m.target.x, m.target.y, m.target.z);
        }

        return moves.Count > 0;
    }

    // -----------------------
    // RPCs that run on ALL clients
    // -----------------------

    [PunRPC]
    private void RPC_BlinkAndDestroy(int[] flatPositions)
    {
        // flatPositions length should be multiple of 3: x,y,z triples
        if (flatPositions == null || flatPositions.Length % 3 != 0) return;

        // create list locally for blink/destroy visuals
        List<Vector3Int> positions = new List<Vector3Int>();
        for (int i = 0; i < flatPositions.Length; i += 3)
        {
            positions.Add(new Vector3Int(flatPositions[i], flatPositions[i + 1], flatPositions[i + 2]));
        }

        // blink visuals then destroy local GameObjects and clear board mapping (local mirroring)
        StartCoroutine(BlinkAndDestroyLocal(positions));
    }

    private IEnumerator BlinkAndDestroyLocal(List<Vector3Int> positions)
    {
        yield return new WaitForSeconds(0.15f);

        float blinkInterval = 0.16f;
        int blinkCount = 2;
        List<Renderer> renderers = new List<Renderer>();

        foreach (var pos in positions)
        {
            if (IsInBounds(pos.x, pos.y, pos.z))
            {
                GameObject b = ballObjects[pos.x, pos.y, pos.z];
                if (b != null)
                {
                    Renderer r = b.GetComponent<Renderer>();
                    if (r != null) renderers.Add(r);
                }
            }
        }

        for (int i = 0; i < blinkCount; i++)
        {
            foreach (var r in renderers) if (r != null) r.enabled = false;
            yield return new WaitForSeconds(blinkInterval);
            foreach (var r in renderers) if (r != null) r.enabled = true;
            yield return new WaitForSeconds(blinkInterval);
        }

        // destroy local objects and clear mirror board
        foreach (var pos in positions)
        {
            if (!IsInBounds(pos.x, pos.y, pos.z)) continue;
            GameObject obj = ballObjects[pos.x, pos.y, pos.z];
            if (obj != null)
            {
                // If this is a networked object, destroy via Photon (MasterClient already removed authoritative mapping)
                PhotonView pvObj = obj.GetComponent<PhotonView>();
                if (pvObj != null && PhotonNetwork.IsMasterClient)
                {
                    // MasterClient destroys networked object so it is removed for everyone
                    PhotonNetwork.Destroy(obj);
                }
                else if (pvObj == null)
                {
                    // local-only fallback
                    Destroy(obj);
                }
            }
            // local mirror update
            if (board != null) board[pos.x, pos.y, pos.z] = 0;
            ballObjects[pos.x, pos.y, pos.z] = null;
        }
    }

    [PunRPC]
    private void RPC_MoveBallTo(int ballViewId, float tx, float ty, float tz)
    {
        Vector3 target = new Vector3(tx, ty, tz);

        GameObject ballObj = null;
        if (ballViewId != 0)
        {
            PhotonView found = PhotonView.Find(ballViewId);
            if (found != null) ballObj = found.gameObject;
        }

        // fallback: if view not found, try to find ball by proximity in the local mirror (not guaranteed)
        if (ballObj == null)
        {
            // Attempt to find a local non-networked ball near target x,z position
            // Not perfect; this is a fallback to keep visuals roughly in sync
            float threshold = 0.5f;
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))

            {
                if (go.name.Contains("Ball") || go.GetComponent<Renderer>() != null)
                {
                    if (Vector3.Distance(new Vector3(go.transform.position.x, 0, go.transform.position.z), new Vector3(target.x, 0, target.z)) < threshold)
                    {
                        ballObj = go;
                        break;
                    }
                }
            }
        }

        if (ballObj != null)
        {
            // use smooth drop helper
            RodCellPhoton.StartSmoothDropStatic(ballObj.transform, target);
        }
    }

    [PunRPC]
    private void RPC_SwitchTurn(int newTurn)
    {
        currentPlayerId = newTurn;
        currentTurnPlayer = newTurn;
        isTurnLocked = false;
        UpdateTurnUI();
    }
    private void StopAllFades()
    {
        if (player1FadeRoutine != null) StopCoroutine(player1FadeRoutine);
        if (player2FadeRoutine != null) StopCoroutine(player2FadeRoutine);
        if (player1TurnFadeRoutine != null) StopCoroutine(player1TurnFadeRoutine);
        if (player2TurnFadeRoutine != null) StopCoroutine(player2TurnFadeRoutine);
    }

    [PunRPC]
    private void RPC_DeclareWinner()
    {
        // MasterClient already decided; this displays results
        if (lastMovePanel != null) lastMovePanel.SetActive(false);

        gameOver = true;
        StopAllFades();

        player1Highlight.enabled = false;
        player2Highlight.enabled = false;
        player1TurnImage.enabled = false;
        player2TurnImage.enabled = false;

        if (player1WinImage != null) player1WinImage.gameObject.SetActive(false);
        if (player2WinImage != null) player2WinImage.gameObject.SetActive(false);
        if (tieImage != null) tieImage.gameObject.SetActive(false);

        if (player1Score > player2Score)
        {
            if (player1WinImage != null) player1WinImage.gameObject.SetActive(true);
            AudioManager.Instance.PlayWin();
        }
        else if (player2Score > player1Score)
        {
            if (player2WinImage != null) player2WinImage.gameObject.SetActive(true);
            AudioManager.Instance.PlayLose();
        }
        else if (tieImage != null)
        {
            tieImage.gameObject.SetActive(true);
            AudioManager.Instance.PlayTie();
        }

        isTurnLocked = true;
        AdManager.Instance.DisplayInterstitialWithLoading();
    }

    // -------------------------
    // Utility helpers (shared)
    // -------------------------
    private bool IsInBounds(int x, int y, int z)
    {
        return x >= 0 && x < gridSize && y >= 0 && y < maxBallsPerRod && z >= 0 && z < gridSize;
    }

    private int GetFirstEmptyY(int x, int z)
    {
        for (int y = 0; y < maxBallsPerRod; y++)
            if (board[x, y, z] == 0) return y;
        return -1;
    }

    private IEnumerator DeclareWinnerAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        pv.RPC(nameof(RPC_DeclareWinner), RpcTarget.All);
    }

    // Copy of earlier GetWinningPositions logic (works on authoritative board or local mirror)
    private List<Vector3Int> GetWinningPositions(int playerId)
    {
        if (board == null) return null;

        List<Vector3Int> allMatches = new List<Vector3Int>();
        int requiredLength = gridSize;

        Vector3Int[] directions = new Vector3Int[]
        {
            new Vector3Int(1,0,0),
            new Vector3Int(0,0,1),
            new Vector3Int(0,1,0),
            new Vector3Int(1,0,1),
            new Vector3Int(1,0,-1)
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

    // Update UI scoreboard
    private void UpdateScoreUI()
    {
        if (player1ScoreText != null) player1ScoreText.text = "" + player1Score;
        if (player2ScoreText != null) player2ScoreText.text = "" + player2Score;
    }

    // UI fade helpers (same as your local manager)
    private Coroutine p1TurnFade, p2TurnFade;
    private void UpdateTurnUI()
    {
        if (gameOver) return;
        if (currentPlayerId == 1)
        {
            player1Highlight.enabled = true;
            player2Highlight.enabled = false;
            StopTurnImageFades();
            player1TurnImage.enabled = true;
            player2TurnImage.enabled = true;
            p1TurnFade = StartCoroutine(FadeImage(player1TurnImage, activeColor));
            p2TurnFade = StartCoroutine(FadeImage(player2TurnImage, new Color(1, 1, 1, 0)));
        }
        else
        {
            player1Highlight.enabled = false;
            player2Highlight.enabled = true;
            StopTurnImageFades();
            player1TurnImage.enabled = true;
            player2TurnImage.enabled = true;
            p1TurnFade = StartCoroutine(FadeImage(player1TurnImage, new Color(1, 1, 1, 0)));
            p2TurnFade = StartCoroutine(FadeImage(player2TurnImage, activeColor));
        }
    }
    private void StopTurnImageFades()
    {
        if (p1TurnFade != null) StopCoroutine(p1TurnFade);
        if (p2TurnFade != null) StopCoroutine(p2TurnFade);
    }
    private IEnumerator FadeImage(Image img, Color targetColor)
    {
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
        if (targetColor.a <= 0.01f) img.enabled = false;
    }

    // Misc UI & exit handlers
    public void OnHomeButton() { SceneManager.LoadScene("MainMenu"); }
    public void OnRetryButton() { SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
    public void ShowLastMovePanelNow()
    {
        if (lastMovePanel == null) return;
        lastMovePanel.SetActive(true);
        if (lastMovePanelRoutine != null) StopCoroutine(lastMovePanelRoutine);
        lastMovePanelRoutine = StartCoroutine(HideLastMovePanelAfterDelay());
    }
    private IEnumerator HideLastMovePanelAfterDelay()
    {
        yield return new WaitForSeconds(lastMovePanelDuration);
        if (lastMovePanel != null) lastMovePanel.SetActive(false);
        lastMovePanelRoutine = null;
    }
    public void OpenExitPanel() { if (ExitPanel != null) ExitPanel.SetActive(true); }
    public void OnExitYes()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    public void OnExitNo() { if (ExitPanel != null) ExitPanel.SetActive(false); }
}
