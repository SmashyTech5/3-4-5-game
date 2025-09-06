using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Multiplayer + AI-compatible manager.
/// Handles 5x5 grid, gravity, cascade, turn system, and scoring.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class GameManagerPhoton : MonoBehaviourPunCallbacks
{
    [Header("Grid")]
    public int gridSize = 5;
    public int maxBallsPerRod = 5;
    public RodCellPhoton rodPrefab;
    public Transform rodParent;
    public GameObject baseObject;

    [Header("Ball Prefabs (must be in Resources)")]
    public GameObject player1BallPrefab;
    public GameObject player2BallPrefab;

    [Header("UI")]
    public GameObject youTurnImage;
    public GameObject opponentTurnImage;
    public TMP_Text playerScoreText;
    public TMP_Text opponentScoreText;
    public GameObject lastMovePanel;
    public float lastMovePanelDuration = 2f;

    [Header("Animations")]
    public float dropSpeed = 10f;
    public float blinkInterval = 0.18f;
    public int blinkCount = 2;

    // authoritative board + visuals
    public int[,,] board;
    private GameObject[,,] ballObjects;        // local mapping of positions -> visual GameObjects (set on all clients)
    private RodCellPhoton[,] rods;

    private PhotonView pv;

    // turns & scores
    private int currentPlayerId = 1;
    private int localPlayerId = 1;
    private int opponentPlayerId = 2;
    private bool isTurnLocked = false;

    private int usedBeats = 0;
    private int totalBeats = 0;

    private int totalPlayer1Score = 0;
    private int totalPlayer2Score = 0;

    private bool lastTwoTurnsShown = false;
    private Coroutine lastMovePanelRoutine;

    void Awake()
    {
        pv = GetComponent<PhotonView>();
    }

    void Start()
    {
        board = new int[gridSize, maxBallsPerRod, gridSize];
        ballObjects = new GameObject[gridSize, maxBallsPerRod, gridSize];
        rods = new RodCellPhoton[gridSize, gridSize];

        totalBeats = gridSize * gridSize * maxBallsPerRod;

        SetupLocalIds();
        GenerateBoard();

        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            currentPlayerId = 1;
            pv.RPC(nameof(RPC_SetTurn), RpcTarget.AllBuffered, currentPlayerId);
        }
        else if (!PhotonNetwork.InRoom)
        {
            currentPlayerId = 1;
            RPC_SetTurn(currentPlayerId);
        }

        UpdateScoreUI();
        if (lastMovePanel) lastMovePanel.SetActive(false);
    }

    void SetupLocalIds()
    {
        if (PhotonNetwork.InRoom)
        {
            localPlayerId = (PhotonNetwork.LocalPlayer.ActorNumber == PhotonNetwork.MasterClient.ActorNumber) ? 1 : 2;
            opponentPlayerId = (localPlayerId == 1) ? 2 : 1;
        }
        else
        {
            localPlayerId = 1;
            opponentPlayerId = 2;
        }

        if (youTurnImage) youTurnImage.SetActive(false);
        if (opponentTurnImage) opponentTurnImage.SetActive(false);
    }

    void GenerateBoard()
    {
        Vector3 start = Vector3.zero;
        float spacingX = 1.2f, spacingZ = 1.2f;

        if (baseObject != null)
        {
            var r = baseObject.GetComponent<Renderer>();
            if (r != null)
            {
                Bounds b = r.bounds;
                spacingX = b.size.x / (gridSize + 1);
                spacingZ = b.size.z / (gridSize + 1);
                float startX = b.center.x - (spacingX * (gridSize - 1)) / 2f;
                float startZ = b.center.z - (spacingZ * (gridSize - 1)) / 2f;
                start = new Vector3(startX, b.max.y, startZ);
            }
        }
        else
        {
            float totalWidth = spacingX * (gridSize - 1);
            float totalDepth = spacingZ * (gridSize - 1);
            start = new Vector3(-totalWidth / 2f, 0.5f, -totalDepth / 2f);
        }

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                Vector3 pos = start + new Vector3(x * spacingX, 0f, z * spacingZ);
                RodCellPhoton rod = Instantiate(rodPrefab, pos, Quaternion.identity, rodParent);
                rods[x, z] = rod;
                if (rod != null) rod.Setup(this, x, z);
            }
        }
    }

    void Update()
    {
        if (isTurnLocked || localPlayerId != currentPlayerId) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                RodCellPhoton rod = hit.collider.GetComponent<RodCellPhoton>();
                if (rod != null)
                {
                    int rx = rod.gridX;
                    int rz = rod.gridZ;
                    isTurnLocked = true;

                    if (PhotonNetwork.InRoom)
                        pv.RPC(nameof(RPC_RequestPlaceBall), RpcTarget.MasterClient, rx, rz, PhotonNetwork.LocalPlayer.ActorNumber);
                    else
                        TryPlaceLocal(rx, rz, localPlayerId);
                }
            }
        }
    }
   

    // --------------------------
    // PLACE BALL (network/local)
    // --------------------------

    // Client -> Master: request placement
    [PunRPC]
    void RPC_RequestPlaceBall(int x, int z, int requesterActorNumber)
    {
        if (!PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) return;

        int requestingPlayerId = (requesterActorNumber == PhotonNetwork.MasterClient.ActorNumber) ? 1 : 2;

        if (requestingPlayerId != currentPlayerId)
        {
            // invalid turn - ignore and optionally unlock that client
            return;
        }

        int y = GetFirstEmptyY(x, z);
        if (y == -1 || y >= maxBallsPerRod) return;

        // Authoritative update of board & usedBeats on Master
        board[x, y, z] = requestingPlayerId;
        usedBeats++;

        // Master instantiates the networked prefab so every client gets a Photon-instantiated object.
        // Important: prefabs MUST be in Resources and prefab.name must match file name.
        GameObject prefabRef = (requestingPlayerId == 1) ? player1BallPrefab : player2BallPrefab;
        if (prefabRef == null)
        {
            Debug.LogError("Ball prefab missing for player " + requestingPlayerId);
            return;
        }

        // spawn a bit above so it can drop
        Vector3 targetPos = rods[x, z].GetSpawnPosition(y);
        Vector3 spawnPos = targetPos + Vector3.up * 3f;

        // Instantiate on Master — this will create Photon objects on all clients.
        GameObject netBall = PhotonNetwork.Instantiate(prefabRef.name, spawnPos, Quaternion.identity);
        PhotonView netPV = netBall.GetComponent<PhotonView>();

        // Register the networked object's view ID on every client so they can map it to the grid slot.
        if (netPV != null)
        {
            pv.RPC(nameof(RPC_RegisterNetworkedBall), RpcTarget.AllBuffered, netPV.ViewID, x, y, z);
        }
        else
        {
            Debug.LogWarning("Spawned ball missing PhotonView.");
        }

        // Start master-side server flow (cascades, scoring, switching)
        StartCoroutine(ServerAfterPlaceRoutine(requestingPlayerId, x, y, z));
    }

    // Called on all clients to map an already-network-instantiated object (found by viewID)
    [PunRPC]
    void RPC_RegisterNetworkedBall(int viewID, int x, int y, int z)
    {
        PhotonView found = PhotonView.Find(viewID);
        if (found == null)
        {
            Debug.LogWarning($"RPC_RegisterNetworkedBall: PhotonView {viewID} not found yet. Will try later.");
            // If it's not found immediately (rare), schedule a short retry.
            StartCoroutine(RegisterRetry(viewID, x, y, z, 0.15f));
            return;
        }

        GameObject ballObj = found.gameObject;
        if (ballObj == null) return;

        ballObjects[x, y, z] = ballObj;

        // ensure local board mirrors master (for clients this keeps the arrays in sync)
        board[x, y, z] = board[x, y, z]; // no-op but kept for clarity

        // Smooth drop on every client
        Vector3 targetPos = rods[x, z].GetSpawnPosition(y);
        RodCellPhoton.StartSmoothDropStatic(ballObj.transform, targetPos);
    }

    IEnumerator RegisterRetry(int viewID, int x, int y, int z, float delay)
    {
        yield return new WaitForSeconds(delay);
        PhotonView found = PhotonView.Find(viewID);
        if (found != null)
        {
            ballObjects[x, y, z] = found.gameObject;
            Vector3 targetPos = rods[x, z].GetSpawnPosition(y);
            RodCellPhoton.StartSmoothDropStatic(found.transform, targetPos);
        }
    }

    // --------------------------
    // SERVER FLOW (Master)
    // --------------------------
    IEnumerator ServerAfterPlaceRoutine(int placingPlayerId, int placedX, int placedY, int placedZ)
    {
        if (!PhotonNetwork.IsMasterClient && PhotonNetwork.InRoom) yield break;

        // small delay for visuals to start dropping
        yield return new WaitForSeconds(0.3f);

        // Master checks cascades in a loop until none remain.
        while (true)
        {
            List<Vector3Int> matches = GetWinningPositionsForPlayer(placingPlayerId);
            if (matches == null || matches.Count == 0) break;

            // Award score for each matched bead
            if (placingPlayerId == 1) totalPlayer1Score += matches.Count;
            else totalPlayer2Score += matches.Count;

            // Update authoritative board: clear those positions
            List<int> flat = new List<int>();
            foreach (var p in matches)
            {
                board[p.x, p.y, p.z] = 0;
                flat.Add(p.x); flat.Add(p.y); flat.Add(p.z);
            }

            // Tell all clients to blink & destroy those positions and then run local gravity
            pv.RPC(nameof(RPC_BroadcastDestroyAndDrop), RpcTarget.AllBuffered, flat.ToArray());

            // Update scores to clients
            pv.RPC(nameof(RPC_UpdateScores), RpcTarget.AllBuffered, totalPlayer1Score, totalPlayer2Score);

            // small wait to allow blink+drop animations to run on clients
            yield return new WaitForSeconds(0.65f);
        }

        // Show last move panel if needed (master decides)
        if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
        {
            lastTwoTurnsShown = true;
            pv.RPC(nameof(RPC_ShowLastMovePanel), RpcTarget.AllBuffered);
        }

        // Switch turns and broadcast
        currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
        pv.RPC(nameof(RPC_SetTurn), RpcTarget.AllBuffered, currentPlayerId);

        // End-game check
        if (usedBeats >= totalBeats)
        {
            yield return new WaitForSeconds(0.25f);
            string result;
            if (totalPlayer1Score > totalPlayer2Score) result = $"Player 1 Wins! {totalPlayer1Score} - {totalPlayer2Score}";
            else if (totalPlayer2Score > totalPlayer1Score) result = $"Player 2 Wins! {totalPlayer2Score} - {totalPlayer1Score}";
            else result = $"It's a Tie! {totalPlayer1Score} - {totalPlayer2Score}";

            pv.RPC(nameof(RPC_GameOver), RpcTarget.All, result);
        }
    }

    // RPC broadcast: Every client blinks/destroys the given positions then runs DropBallsDownSmooth locally.
    [PunRPC]
    void RPC_BroadcastDestroyAndDrop(int[] flatPositions)
    {
        // parse positions
        List<Vector3Int> positions = new List<Vector3Int>();
        for (int i = 0; i + 2 < flatPositions.Length; i += 3)
            positions.Add(new Vector3Int(flatPositions[i], flatPositions[i + 1], flatPositions[i + 2]));

        StartCoroutine(BlinkDestroyAndDropRoutine(positions));
    }

    IEnumerator BlinkDestroyAndDropRoutine(List<Vector3Int> positions)
    {
        // blink
        for (int b = 0; b < blinkCount; b++)
        {
            foreach (var p in positions)
            {
                if (IsValidPos(p) && ballObjects[p.x, p.y, p.z] != null)
                    ballObjects[p.x, p.y, p.z].SetActive(false);
            }
            yield return new WaitForSeconds(blinkInterval);
            foreach (var p in positions)
            {
                if (IsValidPos(p) && ballObjects[p.x, p.y, p.z] != null)
                    ballObjects[p.x, p.y, p.z].SetActive(true);
            }
            yield return new WaitForSeconds(blinkInterval);
        }

        // destroy locals and clear board (clients mirror master)
        foreach (var p in positions)
        {
            if (!IsValidPos(p)) continue;
            if (ballObjects[p.x, p.y, p.z] != null)
            {
                Destroy(ballObjects[p.x, p.y, p.z]);
                ballObjects[p.x, p.y, p.z] = null;
            }
            board[p.x, p.y, p.z] = 0;
            if (PhotonNetwork.InRoom)
                pv.RPC(nameof(RPC_SetBallPosition), RpcTarget.AllBuffered, p.x, p.y, p.z, 0);

        }

        // small delay then drop remaining beads smoothly
        yield return new WaitForSeconds(0.05f);
        yield return StartCoroutine(DropBallsDownSmooth());
    }

    // --------------------------
    // GRAVITY / DROPPING
    // --------------------------
    IEnumerator DropBallsDownSmooth()
    {
        float dropDelay = 0.03f;

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
                            int playerId = board[x, readY, z];

                            // move visual object mapping locally
                            GameObject ball = ballObjects[x, readY, z];
                            ballObjects[x, writeY, z] = ball;
                            ballObjects[x, readY, z] = null;

                            // update authoritative board
                            board[x, writeY, z] = playerId;
                            board[x, readY, z] = 0;

                            // 🔥 tell everyone to update their boards
                            if (PhotonNetwork.InRoom)
                                pv.RPC(nameof(RPC_SetBallPosition), RpcTarget.AllBuffered, x, writeY, z, playerId);

                            // compute target pos for this rod and start smooth drop
                            if (ball != null)
                            {
                                Vector3 targetPos = rods[x, z].GetSpawnPosition(writeY);
                                RodCellPhoton.StartSmoothDropStatic(ball.transform, targetPos);
                                yield return new WaitForSeconds(dropDelay);
                            }
                        }
                        writeY++;
                    }
                }
            }
        }
    }


    // --------------------------
    // WINNING / SCORING HELPERS
    // --------------------------
    // This variant returns any matched positions for the given player (Master uses this).
    List<Vector3Int> GetWinningPositionsForPlayer(int playerId)
    {
        int requiredLength = gridSize; // for full-line wins on 5x5; adjust if you want different
        List<Vector3Int> results = new List<Vector3Int>();

        // simple directional set (covers straight lines and plane diagonals and 3D diagonals)
        Vector3Int[] dirs = new Vector3Int[]
        {
            new Vector3Int(1,0,0), new Vector3Int(0,1,0), new Vector3Int(0,0,1),
            new Vector3Int(1,1,0), new Vector3Int(1,0,1), new Vector3Int(0,1,1),
            new Vector3Int(1,1,1), new Vector3Int(1,1,-1), new Vector3Int(1,-1,1), new Vector3Int(-1,1,1)
        };

        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < maxBallsPerRod; y++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    if (board[x, y, z] != playerId) continue;

                    foreach (var dir in dirs)
                    {
                        List<Vector3Int> temp = new List<Vector3Int> { new Vector3Int(x, y, z) };
                        int nx = x, ny = y, nz = z;
                        for (int k = 1; k < requiredLength; k++)
                        {
                            nx += dir.x; ny += dir.y; nz += dir.z;
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= gridSize || ny >= maxBallsPerRod || nz >= gridSize)
                                break;
                            if (board[nx, ny, nz] == playerId)
                                temp.Add(new Vector3Int(nx, ny, nz));
                            else
                                break;
                        }
                        if (temp.Count >= requiredLength)
                            results.AddRange(temp);
                    }
                }
            }
        }

        return results.Count > 0 ? results : null;
    }

    int GetFirstEmptyY(int x, int z)
    {
        for (int y = 0; y < maxBallsPerRod; y++)
        {
            if (board[x, y, z] == 0)
                return y;
        }
        return -1; // rod full
    }


    bool IsValidPos(Vector3Int p)
    {
        return p.x >= 0 && p.x < gridSize && p.y >= 0 && p.y < maxBallsPerRod && p.z >= 0 && p.z < gridSize;
    }

    // --------------------------
    // UI / RPC helpers
    // --------------------------
    [PunRPC]
   
    void RPC_SetTurn(int pid)
    {
        currentPlayerId = pid;

        // Unlock input if it's my turn, lock if it's not
        isTurnLocked = (localPlayerId != currentPlayerId);

        if (youTurnImage) youTurnImage.SetActive(localPlayerId == currentPlayerId);
        if (opponentTurnImage) opponentTurnImage.SetActive(localPlayerId != currentPlayerId);
    }

    [PunRPC]
    void RPC_UpdateScores(int p1, int p2)
    {
        totalPlayer1Score = p1;
        totalPlayer2Score = p2;
        UpdateScoreUI();
    }

    [PunRPC]
    void RPC_ShowLastMovePanel()
    {
        if (lastTwoTurnsShown) return;
        lastTwoTurnsShown = true;
        if (lastMovePanel != null)
        {
            if (lastMovePanelRoutine != null) StopCoroutine(lastMovePanelRoutine);
            lastMovePanelRoutine = StartCoroutine(ShowLastMovePanelRoutine());
        }
    }

    IEnumerator ShowLastMovePanelRoutine()
    {
        lastMovePanel.SetActive(true);
        yield return new WaitForSeconds(lastMovePanelDuration);
        lastMovePanel.SetActive(false);
        lastMovePanelRoutine = null;
    }

    [PunRPC]
    void RPC_GameOver(string result)
    {
        Debug.Log("[GameOver] " + result);
        if (youTurnImage) youTurnImage.SetActive(false);
        if (opponentTurnImage) opponentTurnImage.SetActive(false);
    }

    void UpdateScoreUI()
    {
        if (playerScoreText) playerScoreText.text = $"P1: {totalPlayer1Score}";
        if (opponentScoreText) opponentScoreText.text = $"P2: {totalPlayer2Score}";
    }

    // Public getter used by RodCellPhoton
    public int GetBoardValue(int x, int y, int z) => board[x, y, z];

    // When player leaves mid-game
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (PhotonNetwork.InRoom)
            pv.RPC(nameof(RPC_GameOver), RpcTarget.All, "Opponent left — it's a Tie!");
    }
    // Local-only placement (used when not in Photon room)
    void TryPlaceLocal(int x, int z, int playerId)
    {
        int y = GetFirstEmptyY(x, z);
        if (y == -1 || y >= maxBallsPerRod)
        {
            isTurnLocked = false;
            return;
        }

        // Update board
        board[x, y, z] = playerId;
        usedBeats++;

        // Spawn visual bead
        GameObject prefabRef = (playerId == 1) ? player1BallPrefab : player2BallPrefab;
        if (prefabRef != null)
        {
            Vector3 targetPos = rods[x, z].GetSpawnPosition(y);
            Vector3 spawnPos = targetPos + Vector3.up * 3f;

            GameObject ball = Instantiate(prefabRef, spawnPos, Quaternion.identity, rodParent);
            ballObjects[x, y, z] = ball;

            // Smooth drop
            RodCellPhoton.StartSmoothDropStatic(ball.transform, targetPos);
        }

        // Run cascade & turn-switch locally
        StartCoroutine(LocalAfterPlaceRoutine(playerId, x, y, z));
    }

    // Local cascade / scoring routine (mirrors server flow)
    IEnumerator LocalAfterPlaceRoutine(int placingPlayerId, int placedX, int placedY, int placedZ)
    {
        yield return new WaitForSeconds(0.3f);

        while (true)
        {
            List<Vector3Int> matches = GetWinningPositionsForPlayer(placingPlayerId);
            if (matches == null || matches.Count == 0) break;

            if (placingPlayerId == 1) totalPlayer1Score += matches.Count;
            else totalPlayer2Score += matches.Count;

            foreach (var p in matches)
            {
                board[p.x, p.y, p.z] = 0;
                if (ballObjects[p.x, p.y, p.z] != null)
                {
                    Destroy(ballObjects[p.x, p.y, p.z]);
                    ballObjects[p.x, p.y, p.z] = null;
                }
            }

            UpdateScoreUI();
            yield return StartCoroutine(DropBallsDownSmooth());
        }

        // Switch turn
        currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
        RPC_SetTurn(currentPlayerId);

        if (usedBeats >= totalBeats)
        {
            string result;
            if (totalPlayer1Score > totalPlayer2Score) result = $"Player 1 Wins! {totalPlayer1Score} - {totalPlayer2Score}";
            else if (totalPlayer2Score > totalPlayer1Score) result = $"Player 2 Wins! {totalPlayer2Score} - {totalPlayer1Score}";
            else result = $"It's a Tie! {totalPlayer1Score} - {totalPlayer2Score}";
            RPC_GameOver(result);
        }
    }
    [PunRPC]
  
    void RPC_SetBallPosition(int x, int y, int z, int playerId)
    {
        board[x, y, z] = playerId;

        if (playerId == 0)
        {
            if (ballObjects[x, y, z] != null)
            {
                Destroy(ballObjects[x, y, z]);
                ballObjects[x, y, z] = null;
            }
            return;
        }

        // Try to find if we already have a ball above this slot that should move down
        GameObject ball = ballObjects[x, y, z];
        if (ball == null)
        {
            // search upwards for misplaced ball
            for (int searchY = y + 1; searchY < maxBallsPerRod; searchY++)
            {
                if (ballObjects[x, searchY, z] != null)
                {
                    ball = ballObjects[x, searchY, z];
                    ballObjects[x, searchY, z] = null;
                    ballObjects[x, y, z] = ball;
                    break;
                }
            }
        }

        if (ball != null)
        {
            // move the visual bead down
            Vector3 targetPos = rods[x, z].GetSpawnPosition(y);
            RodCellPhoton.StartSmoothDropStatic(ball.transform, targetPos);
        }
        else
        {
            // In case no object exists (e.g., sync mismatch), instantiate fallback
            GameObject prefabRef = (playerId == 1) ? player1BallPrefab : player2BallPrefab;
            if (prefabRef != null)
            {
                Vector3 targetPos = rods[x, z].GetSpawnPosition(y);
                GameObject newBall = Instantiate(prefabRef, targetPos + Vector3.up * 2f, Quaternion.identity, rodParent);
                ballObjects[x, y, z] = newBall;
                RodCellPhoton.StartSmoothDropStatic(newBall.transform, targetPos);
            }
        }
    }


}
