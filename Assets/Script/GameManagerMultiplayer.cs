using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;
using TMPro; // if using TextMeshPro

[RequireComponent(typeof(PhotonView))]
public class GameManagerMultiplayer : MonoBehaviourPunCallbacks
{
    [Header("Turn UI")]
    // highlight indicators ("YOUR TURN", "OPPONENT TURN")
    public GameObject youTurnImage;
    public GameObject opponentTurnImage;
    [Header("Round Warnings")]
    public GameObject lastMovePanel; // assign in Inspector
     private bool lastTwoTurnsShown = false;                // prevents double-show per round
   // public UnityEngine.UI.Text lastMoveText = null;       // optional: assign in Inspector to show round-specific text

    // top fade images
    public UnityEngine.UI.Image youTopImage;
    public UnityEngine.UI.Image opponentTopImage;

    private Coroutine youFadeRoutine;
    private Coroutine opponentFadeRoutine;

    private int localPlayerId;
    private int opponentPlayerId;

    [Header("Game Over UI")]
    public GameObject youWinImage;
    public GameObject youLoseImage;
    public GameObject tieImage;
    public GameObject exitPanel; // Assign in Inspector

    [Header("Grid Setup")]
    public RodCellMultiplayer rodPrefab;
    public Transform rodParent;
    public GameObject baseObject;
    public GameObject player1BallPrefab;
    public GameObject player2BallPrefab;

    [Header("Round Settings")]
    public int[] roundSizes = new int[] { 3, 4, 5 };
    private int currentRoundIndex = 0;

    private int gridSize;
    private int maxBallsPerRod;
    private int totalBeats;
    private int usedBeats;

    [Header("Score Tracking")]
    [SerializeField] private int player1Score;
    [SerializeField] private int player2Score;
    [SerializeField] private int totalPlayer1Score;
    [SerializeField] private int totalPlayer2Score;

    // board and visual objects (local copies on each client)
    private int[,,] board;
    private GameObject[,,] ballObjects;
    private RodCellMultiplayer[,] rods;

    private int currentPlayerId = 1; // authoritative turn (1 or 2)
    private bool isTurnLocked = false;
    private bool gameEnded = false;
    // track who scored last (used to attribute cascades correctly)
    private int lastScoredPlayerId = -1;
    [Header("Score UI")]
    public TextMeshProUGUI playerScoreText;
    public TextMeshProUGUI opponentScoreText;
    // ---------- new fields for centered active-subgrid ----------
    private int maxGridSize;                  // = largest entry in roundSizes (e.g. 5)
    private int activeGridSize;               // current round active width (3,4,5)
    private int activeStartIndex;             // inclusive start index of active subgrid
    private int activeEndIndex;               // inclusive end index
    private int maxBallsPerRodGlobal;         // full height (largest round size), arrays Y dimension
    private int currentRoundHeight;           // allowed stacking height for the current round
                                             // helper for clarity
 private int RoundCap(int size) => size * size * size;


    PhotonView pv;

    void Awake()
    {
        pv = GetComponent<PhotonView>();
    }

    void Start()
    {
        SetupLocalIds();
        // ✅ Show banner ad at multiplayer game start
        AdManager.Instance.DisplayBanner();

        // compute global maxima once
        maxGridSize = roundSizes[roundSizes.Length - 1];
        maxBallsPerRodGlobal = roundSizes[roundSizes.Length - 1]; // largest height we will ever need

        // ✅ Allocate arrays ONCE for the whole game
        board = new int[maxGridSize, maxBallsPerRodGlobal, maxGridSize];
        ballObjects = new GameObject[maxGridSize, maxBallsPerRodGlobal, maxGridSize];
        rods = new RodCellMultiplayer[maxGridSize, maxGridSize];

        // If we are already in a room and master and there are 2 players, tell everyone to start.
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount >= 2)
        {
            pv.RPC(nameof(RPC_StartRound), RpcTarget.AllBuffered, currentRoundIndex);
        }
        else if (!PhotonNetwork.InRoom)
        {
            // offline testing fallback - do a direct StartRound locally
            StartRound();
        }
        else
        {
            // Wait for master to send RPC_StartRound when room fills (MainMenu already waits for 2 players).
            Debug.Log("Waiting for RPC_StartRound from Master...");
        }
    }


    // If a player enters while we're in the scene: master triggers start when room fills
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount >= 2)
        {
            pv.RPC(nameof(RPC_StartRound), RpcTarget.AllBuffered, currentRoundIndex);
        }
    }

    public override void OnJoinedRoom()
    {
        SetupLocalIds();
        // If joined while scene loaded and master is already in place, start if master says so (or master will broadcast).
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount >= 2)
        {
            pv.RPC(nameof(RPC_StartRound), RpcTarget.AllBuffered, currentRoundIndex);
        }
    }

    [PunRPC]
    void RPC_StartRound(int roundIndex)
    {
        currentRoundIndex = roundIndex;
        Debug.Log($"[RPC] StartRound called for round {roundIndex}");
        StartRound();
        UpdateScoreUI();

    }

    // ---------- Client -> Master: request a placement ----------
    // Called by RodCellMultiplayer when a player clicks a rod.
    public void RequestPlaceBall(int x, int z)
    {
        if (!PhotonNetwork.InRoom)
        {
            // Offline fallback — place locally (useful for single-player tests)
            TryPlaceLocal(x, z, 1);
            return;
        }

        // Send the request to master (include actor number so master knows who requested)
        pv.RPC(nameof(RPC_RequestPlaceBall), RpcTarget.MasterClient, x, z, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    [PunRPC]
    void RPC_RequestPlaceBall(int x, int z, int requesterActorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        int requestingPlayerId = (requesterActorNumber == PhotonNetwork.MasterClient.ActorNumber) ? 1 : 2;

        // Validate turn / lock
        if (isTurnLocked || requestingPlayerId != currentPlayerId)
        {
            Debug.Log($"Rejecting request (not your turn or locked). requesterId={requestingPlayerId} turn={currentPlayerId} locked={isTurnLocked}");
            return;
        }

        // Validate that (x,z) is within the current active subgrid
        if (x < activeStartIndex || x > activeEndIndex || z < activeStartIndex || z > activeEndIndex)
        {
            Debug.Log($"Rejecting request: cell ({x},{z}) is outside active area ({activeStartIndex}-{activeEndIndex})");
            return;
        }

        // Find first empty Y on rod but only up to currentRoundHeight (not global max)
        int count = 0;
        for (int y = 0; y < currentRoundHeight; y++)
        {
            if (ballObjects[x, y, z] != null) count++;
            else break;
        }
        if (count >= currentRoundHeight)
        {
            Debug.Log("Rod full for this round - rejecting move.");
            return;
        }

        // Accept: update authoritative board using global board array
        board[x, count, z] = requestingPlayerId;
        usedBeats++;

        // Tell everyone to spawn the visual ball (buffered)
        pv.RPC(nameof(RPC_SpawnVisualBall), RpcTarget.AllBuffered, requestingPlayerId, x, count, z);

        // Start server-side flow (master will check matches & broadcast destroys)
        StartCoroutine(ServerAfterPlaceRoutine(requestingPlayerId, x, count, z));
    }


    [PunRPC]
    void RPC_SpawnVisualBall(int playerId, int x, int y, int z)
    {
        // Spawn ball visuals on every client
        GameObject prefab = (playerId == 1) ? player1BallPrefab : player2BallPrefab;
        if (prefab == null)
        {
            Debug.LogError("Missing ball prefab for player " + playerId);
            return;
        }

        // Safety checks: arrays must exist (StartRound must have run)
        if (rods == null || rods.Length == 0)
        {
            Debug.LogError("RPC_SpawnVisualBall: rods not initialized. Make sure RPC_StartRound was called.");
            return;
        }

        float ballHeight = prefab.GetComponent<Renderer>().bounds.size.y;
        float rodBaseY = rods[x, z].GetComponent<Renderer>().bounds.min.y;

        Vector3 targetPos = new Vector3(
            rods[x, z].transform.position.x,
            rodBaseY + (ballHeight / 2f) + y * ballHeight,
            rods[x, z].transform.position.z
        );
        Vector3 spawnPos = targetPos + Vector3.up * 5f;

        GameObject ball = Instantiate(prefab, spawnPos, Quaternion.identity, rodParent);
        ballObjects[x, y, z] = ball;

        // Also update local board copy so clients mirror master's board
        board[x, y, z] = playerId;

        // play drop animation locally
        StartCoroutine(DropBall(ball, targetPos));
    }

    /* [PunRPC]
     void RPC_BlinkAndDestroy(int[] flatPositions)
     {
         // flatPositions: [x,y,z, x,y,z, ...]
         List<Vector3Int> positions = new List<Vector3Int>();
         for (int i = 0; i < flatPositions.Length; i += 3)
             positions.Add(new Vector3Int(flatPositions[i], flatPositions[i + 1], flatPositions[i + 2]));

         // Run local blink & destroy (this includes DropBallsDownSmooth)
         StartCoroutine(BlinkAndDestroy_Local(positions));
     }*/

    [PunRPC]
    void RPC_BlinkAndDestroy(int[] flatPositions)
    {
        // flatPositions: [x,y,z, x,y,z, ...]
        List<Vector3Int> positions = new List<Vector3Int>();
        for (int i = 0; i < flatPositions.Length; i += 3)
            positions.Add(new Vector3Int(flatPositions[i], flatPositions[i + 1], flatPositions[i + 2]));

        // Instead of blinking/destroying, just leave beads where they are
        // (We still need to keep them in the board array, nothing removed)
        Debug.Log("Match detected at: " + positions.Count + " beads. They remain in place.");
    }

    [PunRPC]
    void RPC_SetTurn(int playerId)
    {
        currentPlayerId = playerId;
        isTurnLocked = false;
        Debug.Log("[RPC] SetTurn -> " + playerId);

        UpdateTurnUI(playerId);
    }


    [PunRPC]
    void RPC_UpdateScores(int p1, int p2)
    {
        player1Score = p1;
        player2Score = p2;
        UpdateScoreUI();
    }

    [PunRPC]
    void RPC_GameOver(string result)
    {
        if (gameEnded) return; // ✅ don’t process twice
        gameEnded = true;
        Debug.Log("[RPC] GameOver: " + result);

        // Hide turn UI
        if (youTurnImage) youTurnImage.SetActive(false);
        if (opponentTurnImage) opponentTurnImage.SetActive(false);
        if (youTopImage) SetImageAlpha(youTopImage, 0f);
        if (opponentTopImage) SetImageAlpha(opponentTopImage, 0f);

        // Hide all end-game images first
        if (youWinImage) youWinImage.SetActive(false);
        if (youLoseImage) youLoseImage.SetActive(false);
        if (tieImage) tieImage.SetActive(false);

        int pot = PlayerPrefs.GetInt("PotCoins", 0);
        int coins = PlayerPrefs.GetInt("PlayerCoins", 0);

        if (result.Contains("Tie"))
        {
            if (tieImage) tieImage.SetActive(true);

            // Refund both players
            coins += pot / 2;
            PlayerPrefs.SetInt("PlayerCoins", coins);
        }
        else
        {
            bool localIsWinner = false;

            if (localPlayerId == 1 && result.Contains("Player 1 Wins"))
                localIsWinner = true;
            else if (localPlayerId == 2 && result.Contains("Player 2 Wins"))
                localIsWinner = true;

            if (localIsWinner)
            {
                if (youWinImage) youWinImage.SetActive(true);

                // Winner gets full pot
                coins += pot;
                PlayerPrefs.SetInt("PlayerCoins", coins);
            }
            else
            {
                if (youLoseImage) youLoseImage.SetActive(true);
                // Loser gets nothing
            }
        }

        PlayerPrefs.Save();

        // ✅ Show interstitial ad after game ends
        AdManager.Instance.DisplayInterstitialWithLoading();

        // ✅ Optionally hide banner now
        AdManager.Instance.ConcealBanner();
    }



    // ---------- Master server coroutine to finalize placement, handle matches & cascades ----------
    // ---------- Master server coroutine to finalize placement, handle matches & cascades ----------
    /*  IEnumerator ServerAfterPlaceRoutine(int placingPlayerId, int placedX, int placedY, int placedZ)
      {
          isTurnLocked = true;

          // Small delay so everyone sees the drop animation
          yield return new WaitForSeconds(0.25f);

          HashSet<Vector3Int> destroyedThisTurn = new HashSet<Vector3Int>();

          while (true)
          {
              var matchedPositions = GetWinningPositions(placingPlayerId)
                  ?.FindAll(p => !destroyedThisTurn.Contains(p));

              if (matchedPositions == null || matchedPositions.Count == 0)
                  break;

              foreach (var p in matchedPositions)
                  destroyedThisTurn.Add(p);

              // Increase score
              if (placingPlayerId == 1) player1Score++;
              else player2Score++;

              // 🔥 Remember who actually scored (for cascades)
              lastScoredPlayerId = placingPlayerId;

              // Send positions to blink/destroy on all clients
              List<int> flat = new List<int>();
              foreach (var p in matchedPositions)
              {
                  flat.Add(p.x);
                  flat.Add(p.y);
                  flat.Add(p.z);
              }
              pv.RPC(nameof(RPC_BlinkAndDestroy), RpcTarget.AllBuffered, flat.ToArray());

              // Wait for blink + drop animations before checking again
              yield return new WaitForSeconds(0.7f);
          }

          // Send updated scores to all clients
          pv.RPC(nameof(RPC_UpdateScores), RpcTarget.AllBuffered, player1Score, player2Score);

          // BEFORE switching turn: if the round now has exactly 2 moves remaining,
          if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
          {
              lastTwoTurnsShown = true;
              pv.RPC(nameof(RPC_ShowLastMovePanel), RpcTarget.AllBuffered, currentRoundIndex);
          }

          // Switch turns
          currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
          isTurnLocked = false;
          pv.RPC(nameof(RPC_SetTurn), RpcTarget.All, currentPlayerId);

          // ✅ Reset scorer tracking only after cascades fully processed + turn switched
          lastScoredPlayerId = -1;

          // End round if all beads used
          if (usedBeats >= totalBeats)
          {
              StartCoroutine(EndRoundRoutine_Server());
          }
      }
  */

    IEnumerator ServerAfterPlaceRoutine(int placingPlayerId, int placedX, int placedY, int placedZ)
    {
        isTurnLocked = true;

        // Small delay so everyone sees the drop animation (optional)
        yield return new WaitForSeconds(0.25f);

        Vector3Int placedPos = new Vector3Int(placedX, placedY, placedZ);

        // Check all matches for this player
        var matchedPositions = GetWinningPositions(placingPlayerId);

        if (matchedPositions != null && matchedPositions.Contains(placedPos))
        {
            // ✅ Only score if the placed bead is actually inside a winning line
            if (placingPlayerId == 1) player1Score++;
            else player2Score++;

            lastScoredPlayerId = placingPlayerId;

            Debug.Log($"Player {placingPlayerId} scored at {placedPos}! P1={player1Score}, P2={player2Score}");
        }

        // Send updated scores to all clients
        pv.RPC(nameof(RPC_UpdateScores), RpcTarget.AllBuffered, player1Score, player2Score);

        // BEFORE switching turn: if the round now has exactly 2 moves remaining
        if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
        {
            lastTwoTurnsShown = true;
            pv.RPC(nameof(RPC_ShowLastMovePanel), RpcTarget.AllBuffered, currentRoundIndex);
        }

        // Switch turns
        currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
        isTurnLocked = false;
        pv.RPC(nameof(RPC_SetTurn), RpcTarget.All, currentPlayerId);

        // Reset scorer tracking
        lastScoredPlayerId = -1;

        // End round if all beads used
        if (usedBeats >= totalBeats)
        {
            StartCoroutine(EndRoundRoutine_Server());
        }
    }



    IEnumerator EndRoundRoutine_Server()
    {
        yield return new WaitForSeconds(0.5f);

        totalPlayer1Score += player1Score;
        totalPlayer2Score += player2Score;

        currentRoundIndex++;
        if (currentRoundIndex < roundSizes.Length)
        {
            // Clear buffered RPCs (avoid old spawns replaying for late joiners)
            PhotonNetwork.RemoveRPCs(pv);
            // Hide last-move panel on all clients (defensive)
            pv.RPC(nameof(RPC_HideLastMovePanel), RpcTarget.AllBuffered);
            // Tell all clients to start the next round (⚡ beads stay, move counts carry over)
            pv.RPC(nameof(RPC_StartRound), RpcTarget.AllBuffered, currentRoundIndex);
        }
        else
        {
            string result;
            if (totalPlayer1Score > totalPlayer2Score) result = $"Player 1 Wins! {totalPlayer1Score} - {totalPlayer2Score}";
            else if (totalPlayer2Score > totalPlayer1Score) result = $"Player 2 Wins! {totalPlayer2Score} - {totalPlayer1Score}";
            else result = $"It's a Tie! {totalPlayer1Score} - {totalPlayer2Score}";
            pv.RPC(nameof(RPC_GameOver), RpcTarget.All, result);
        }
    }


    // ---------- Local visual/cascade routine executed on every client via RPC_BlinkAndDestroy ----------
    /*  IEnumerator BlinkAndDestroy_Local(List<Vector3Int> positions)
      {
          // Step 1: Blink only the matched positions
          float blinkDuration = 0.2f;
          for (int i = 0; i < 3; i++)
          {
              foreach (var pos in positions)
                  if (IsValidPos(pos) && ballObjects[pos.x, pos.y, pos.z] != null)
                      ballObjects[pos.x, pos.y, pos.z].SetActive(false);

              yield return new WaitForSeconds(blinkDuration);

              foreach (var pos in positions)
                  if (IsValidPos(pos) && ballObjects[pos.x, pos.y, pos.z] != null)
                      ballObjects[pos.x, pos.y, pos.z].SetActive(true);

              yield return new WaitForSeconds(blinkDuration);
          }

          // Step 2: Destroy ONLY matched balls
          foreach (var pos in positions)
          {
              if (IsValidPos(pos) && ballObjects[pos.x, pos.y, pos.z] != null)
              {
                  Destroy(ballObjects[pos.x, pos.y, pos.z]);
                  ballObjects[pos.x, pos.y, pos.z] = null;
                  board[pos.x, pos.y, pos.z] = 0;
              }
          }

          // Step 3: Wait a short delay before dropping
          yield return new WaitForSeconds(0.05f);

          // Step 4: Now drop the remaining balls smoothly
          yield return StartCoroutine(DropBallsDownSmooth());

          // 🔥 NEW: Ask master to recheck after gravity (cascade)
          if (PhotonNetwork.IsMasterClient)
          {
              StartCoroutine(CheckCascadesAfterGravity());
          }
      }*/
    IEnumerator BlinkAndDestroy_Local(List<Vector3Int> positions)
    {
        // Do nothing, beads stay as they are.
        yield break;
    }

    private IEnumerator CheckCascadesAfterGravity()
    {
        yield return new WaitForSeconds(0.2f); // let visuals settle

        // Always use the player who actually scored
        int scoringPlayer = (lastScoredPlayerId != -1) ? lastScoredPlayerId : currentPlayerId;
        var newMatches = GetWinningPositions(scoringPlayer);

        while (newMatches != null && newMatches.Count > 0)
        {
            // Flatten positions
            List<int> flat = new List<int>();
            foreach (var p in newMatches)
            {
                flat.Add(p.x);
                flat.Add(p.y);
                flat.Add(p.z);
            }

            // Increase score FOR the scoring player
            if (scoringPlayer == 1) player1Score++;
            else player2Score++;

            // Broadcast to all clients to blink/destroy these positions
            pv.RPC(nameof(RPC_BlinkAndDestroy), RpcTarget.AllBuffered, flat.ToArray());
            pv.RPC(nameof(RPC_UpdateScores), RpcTarget.AllBuffered, player1Score, player2Score);

            // Wait until blink+drop finishes before re-checking
            yield return new WaitForSeconds(0.7f);

            // Re-check board again for the same scoring player
            newMatches = GetWinningPositions(scoringPlayer);
        }

       
    }





    bool IsValidPos(Vector3Int p)
    {
        return p.x >= 0 && p.x < gridSize && p.y >= 0 && p.y < maxBallsPerRod && p.z >= 0 && p.z < gridSize;
    }

    // ---------- Local fallback for offline testing ----------
    bool TryPlaceLocal(int x, int z, int playerId)
    {
        if (isTurnLocked) return false;
        isTurnLocked = true;

        int count = 0;
        for (int y = 0; y < maxBallsPerRod; y++)
        {
            if (ballObjects[x, y, z] != null) count++;
            else break;
        }
        if (count >= maxBallsPerRod)
        {
            isTurnLocked = false;
            return false;
        }

        GameObject prefab = (playerId == 1) ? player1BallPrefab : player2BallPrefab;
        float ballHeight = prefab.GetComponent<Renderer>().bounds.size.y;
        float rodBaseY = rods[x, z].GetComponent<Renderer>().bounds.min.y;

        Vector3 targetPos = new Vector3(
            rods[x, z].transform.position.x,
            rodBaseY + (ballHeight / 2f) + count * ballHeight,
            rods[x, z].transform.position.z
        );
        Vector3 spawnPos = targetPos + Vector3.up * 5f;

        GameObject ball = Instantiate(prefab, spawnPos, Quaternion.identity, rodParent);
        ballObjects[x, count, z] = ball;
        board[x, count, z] = playerId;
        usedBeats++;
        // Offline: show local popup if this placement makes remaining == 2 and not already shown
        if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
        {
            lastTwoTurnsShown = true;
            ShowLastMovePanelLocal(currentRoundIndex);
        }
        StartCoroutine(DropAndMatchRoutine(ball, targetPos, playerId));
        return true;
    }

    // ---------- Your existing placement/match code (adapted to allow passing placingPlayerId for offline fallback) ----------
    IEnumerator DropAndMatchRoutine(GameObject ball, Vector3 targetPos, int placingPlayerId)
    {
        yield return StartCoroutine(DropBall(ball, targetPos));
        yield return new WaitForSeconds(0.1f);

        var matchedPositions = GetWinningPositions(placingPlayerId);
        if (matchedPositions != null && matchedPositions.Count > 0)
        {
            if (placingPlayerId == 1) player1Score++;
            else player2Score++;

            yield return StartCoroutine(BlinkAndDestroy_Local(matchedPositions));
        }

        // If the round now has exactly 2 moves left (remaining == 2), show popup (offline)
        if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
        {
            lastTwoTurnsShown = true;
            ShowLastMovePanelLocal(currentRoundIndex);
        }

        if (usedBeats >= totalBeats)
        {
            yield return StartCoroutine(EndRoundRoutine());
            yield break;
        }

        currentPlayerId = (currentPlayerId == 1) ? 2 : 1;
        isTurnLocked = false;
    }


    IEnumerator DropBall(GameObject ball, Vector3 targetPos)
    {
        float speed = 10f;
        while (Vector3.Distance(ball.transform.position, targetPos) > 0.01f)
        {
            ball.transform.position = Vector3.MoveTowards(ball.transform.position, targetPos, speed * Time.deltaTime);
            yield return null;
        }
        ball.transform.position = targetPos;
    }

    IEnumerator DropBallsDownSmooth()
    {
        float dropDelay = 0.05f;

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                int writeY = 0;
                for (int readY = 0; readY < maxBallsPerRod; readY++)
                {
                    if (ballObjects[x, readY, z] != null)
                    {
                        if (writeY != readY)
                        {
                            GameObject ball = ballObjects[x, readY, z];
                            ballObjects[x, writeY, z] = ball;
                            ballObjects[x, readY, z] = null;

                            // move board value down
                            board[x, writeY, z] = board[x, readY, z];
                            board[x, readY, z] = 0;

                            // compute target position and smoothly drop
                            float ballHeight = ball.GetComponent<Renderer>().bounds.size.y;
                            float rodBaseY = rods[x, z].GetComponent<Renderer>().bounds.min.y;
                            Vector3 targetPos = new Vector3(
                                rods[x, z].transform.position.x,
                                rodBaseY + (ballHeight / 2f) + writeY * ballHeight,
                                rods[x, z].transform.position.z
                            );

                            yield return new WaitForSeconds(dropDelay);
                            yield return StartCoroutine(DropBall(ball, targetPos));
                        }
                        writeY++;
                    }
                }
            }
        }
    }



    private List<Vector3Int> GetWinningPositions(int playerId)
    {
        List<Vector3Int> allMatches = new List<Vector3Int>();
        int requiredLength = currentRoundHeight; // number of beads in a winning line for this round

        Vector3Int[] directions = new Vector3Int[]
        {
        new Vector3Int(1, 0, 0),   // X
        new Vector3Int(0, 0, 1),   // Z
        new Vector3Int(0, 1, 0),   // Y
        new Vector3Int(1, 0, 1),   // Diagonal XZ down-right
        new Vector3Int(1, 0, -1)   // Diagonal XZ down-left
        };

        // Limits: we only check start positions that are within the active subgrid and Y within [0,currentRoundHeight-1]
        int minX = activeStartIndex;
        int maxX = activeEndIndex;
        int minZ = activeStartIndex;
        int maxZ = activeEndIndex;
        int minY = 0;
        int maxY = currentRoundHeight - 1;

        foreach (var dir in directions)
        {
            int startXMin = minX;
            int startXMax = maxX - (dir.x == 1 ? (requiredLength - 1) : 0);
            int startZMin = minZ;
            int startZMax = maxZ - (dir.z == 1 ? (requiredLength - 1) : (dir.z == -1 ? 0 : 0));
            int startYMin = minY;
            int startYMax = maxY - (dir.y == 1 ? (requiredLength - 1) : 0);

            for (int sx = startXMin; sx <= startXMax; sx++)
            {
                for (int sy = startYMin; sy <= startYMax; sy++)
                {
                    for (int sz = startZMin; sz <= startZMax; sz++)
                    {
                        List<Vector3Int> match = new List<Vector3Int>();
                        bool ok = true;
                        for (int step = 0; step < requiredLength; step++)
                        {
                            int nx = sx + dir.x * step;
                            int ny = sy + dir.y * step;
                            int nz = sz + dir.z * step;

                            if (nx < minX || nx > maxX || nz < minZ || nz > maxZ || ny < minY || ny > maxY)
                            {
                                ok = false; break;
                            }

                            if (board[nx, ny, nz] != playerId)
                            {
                                ok = false; break;
                            }
                            match.Add(new Vector3Int(nx, ny, nz));
                        }
                        if (ok && match.Count == requiredLength)
                            allMatches.AddRange(match);
                    }
                }
            }
        }

        return allMatches.Count > 0 ? allMatches : null;
    }




    IEnumerator EndRoundRoutine()
    {
        yield return new WaitForSeconds(0.5f);

        totalPlayer1Score += player1Score;
        totalPlayer2Score += player2Score;

        // Hide the last move panel at round end (works both online/offline)
        if (lastMovePanel != null)
            lastMovePanel.SetActive(false);

        currentRoundIndex++;
        if (currentRoundIndex < roundSizes.Length)
        {
            if (PhotonNetwork.InRoom)
            {
                if (PhotonNetwork.IsMasterClient)
                    pv.RPC(nameof(RPC_StartRound), RpcTarget.AllBuffered, currentRoundIndex);
            }
            else
            {
                StartRound(); // offline
            }
        }
        else
        {
            DeclareWinner();
        }
    }


    void DeclareWinner()
    {
        string result;
        if (totalPlayer1Score > totalPlayer2Score)
            result = $"Player 1 Wins! {totalPlayer1Score} - {totalPlayer2Score}";
        else if (totalPlayer2Score > totalPlayer1Score)
            result = $"Player 2 Wins! {totalPlayer2Score} - {totalPlayer1Score}";
        else
            result = $"It's a Tie! {totalPlayer1Score} - {totalPlayer2Score}";

        Debug.Log(result);

        if (PhotonNetwork.InRoom)
        {
            // 🔥 Ensure winner is shown to everyone
            pv.RPC(nameof(RPC_GameOver), RpcTarget.All, result);
        }
        else
        {
            // Offline fallback: directly call UI
            RPC_GameOver(result);
        }
    }

    void StartRound()
    {
        // ensure any previous UI state is cleared
        if (lastMovePanel != null)
            lastMovePanel.SetActive(false);

        // reset per-round flag
        lastTwoTurnsShown = false;

        // current active grid size (3,4,5…)
        activeGridSize = roundSizes[currentRoundIndex];
        currentRoundHeight = roundSizes[currentRoundIndex];

        // compute centered active start/end indices
        int margin = (maxGridSize - activeGridSize) / 2;
        activeStartIndex = margin;
        activeEndIndex = activeStartIndex + activeGridSize - 1;

        // full arrays already allocated in Start()
        gridSize = maxGridSize;
        maxBallsPerRod = maxBallsPerRodGlobal;

        // ===== Set the per-round target (size^3). =====
        // IMPORTANT: do NOT reset usedBeats here — it carries over across rounds.
        totalBeats = activeGridSize * activeGridSize * activeGridSize;

        // ===== Recompute usedBeats from the authoritative board =====
        usedBeats = 0;
        for (int x = 0; x < maxGridSize; x++)
        {
            for (int y = 0; y < maxBallsPerRod; y++)
            {
                for (int z = 0; z < maxGridSize; z++)
                {
                    if (board[x, y, z] != 0) usedBeats++;
                }
            }
        }

        // If we are already at "last two turns", show the popup
        if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
        {
            lastTwoTurnsShown = true;
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
                pv.RPC(nameof(RPC_ShowLastMovePanel), RpcTarget.AllBuffered, currentRoundIndex);
            else
                ShowLastMovePanelLocal(currentRoundIndex);
        }

        // If we've already reached (or exceeded) the new round's cap, end it immediately.
        if (usedBeats >= totalBeats)
        {
            if (PhotonNetwork.InRoom)
            {
                if (PhotonNetwork.IsMasterClient)
                    StartCoroutine(EndRoundRoutine_Server());
                // otherwise wait for master to handle it
            }
            else
            {
                StartCoroutine(EndRoundRoutine());
            }
            return;
        }

        if (!baseObject) return;
        Renderer baseRenderer = baseObject.GetComponent<Renderer>();
        if (!baseRenderer) return;

        Bounds baseBounds = baseRenderer.bounds;
        float spacingX = baseBounds.size.x / (maxGridSize + 1);
        float spacingZ = baseBounds.size.z / (maxGridSize + 1);
        float startX = baseBounds.min.x + spacingX;
        float startZ = baseBounds.min.z + spacingZ;

        // rod placement Y
        float baseTopY = baseBounds.max.y;
        Renderer rodRenderer = rodPrefab.GetComponent<Renderer>();
        float rodHeight = (rodRenderer != null) ? rodRenderer.bounds.size.y : 1f;

        float pivotOffsetY = 0f;
        if (rodRenderer != null)
        {
            float pivotToBottom = rodRenderer.bounds.min.y - rodPrefab.transform.position.y;
            pivotOffsetY = Mathf.Abs(pivotToBottom) < 0.001f ? 0f : rodHeight / 2f;
        }
        else
        {
            pivotOffsetY = rodHeight / 2f;
        }

        float rodY = baseTopY + pivotOffsetY;

        // rod scaling for current round
        float ballHeight = player1BallPrefab.GetComponent<Renderer>().bounds.size.y;
        float desiredRodHeight = currentRoundHeight * ballHeight;

        // Spawn or reuse rods
        for (int x = 0; x < maxGridSize; x++)
        {
            for (int z = 0; z < maxGridSize; z++)
            {
                if (rods[x, z] == null)
                {
                    Vector3 pos = new Vector3(startX + x * spacingX, rodY, startZ + z * spacingZ);
                    RodCellMultiplayer rod = Instantiate(rodPrefab, pos, Quaternion.identity, rodParent);
                    rods[x, z] = rod;
                }

                // Scale rod to match current round height
                float originalRodHeight = rods[x, z].GetComponent<Renderer>().bounds.size.y;
                if (originalRodHeight > 0)
                {
                    Vector3 scale = rods[x, z].transform.localScale;
                    scale.y *= desiredRodHeight / originalRodHeight;
                    rods[x, z].transform.localScale = scale;
                }

                rods[x, z].Setup(this, x, z);

                // Normal lock/unlock logic
                bool activeCell = (x >= activeStartIndex && x <= activeEndIndex &&
                                   z >= activeStartIndex && z <= activeEndIndex);

                if (activeCell)
                {
                    rods[x, z].gameObject.SetActive(true);
                    rods[x, z].SetInteractable(true);
                    rods[x, z].SetHighlighted(true);
                }
                else
                {
                    rods[x, z].gameObject.SetActive(true);
                    rods[x, z].SetInteractable(false);
                    rods[x, z].SetHighlighted(false);
                }
            }
        }

        currentPlayerId = 1;
        isTurnLocked = false;
        UpdateTurnUI(currentPlayerId);
        UpdateScoreUI();
    }


    void ClearBoard()
    {
        if (rodParent == null) return;
        for (int i = rodParent.childCount - 1; i >= 0; i--)
            Destroy(rodParent.GetChild(i).gameObject);
    }

    void SetupLocalIds()
    {
        if (PhotonNetwork.InRoom)
            localPlayerId = (PhotonNetwork.LocalPlayer.ActorNumber == PhotonNetwork.MasterClient.ActorNumber) ? 1 : 2;
        else
            localPlayerId = 1; // offline test

        opponentPlayerId = (localPlayerId == 1) ? 2 : 1;

        // Start with UI hidden
        if (youTurnImage) youTurnImage.SetActive(false);
        if (opponentTurnImage) opponentTurnImage.SetActive(false);
        if (youTopImage) SetImageAlpha(youTopImage, 0f);
        if (opponentTopImage) SetImageAlpha(opponentTopImage, 0f);
    }

    void UpdateTurnUI(int activePlayerId)
    {
        bool isMyTurn = (activePlayerId == localPlayerId);

        // Show bottom indicators
        if (youTurnImage) youTurnImage.SetActive(isMyTurn);
        if (opponentTurnImage) opponentTurnImage.SetActive(!isMyTurn);

        // Handle top images (fade in/out depending on whose turn)
        if (isMyTurn)
        {
            if (opponentFadeRoutine != null) StopCoroutine(opponentFadeRoutine);
            if (youFadeRoutine != null) StopCoroutine(youFadeRoutine);

            // fade YOU in, fade opponent out
            youFadeRoutine = StartCoroutine(FadeTo(youTopImage, 1f, 0.4f));
            opponentFadeRoutine = StartCoroutine(FadeTo(opponentTopImage, 0f, 0.4f));
        }
        else
        {
            if (youFadeRoutine != null) StopCoroutine(youFadeRoutine);
            if (opponentFadeRoutine != null) StopCoroutine(opponentFadeRoutine);

            // fade Opponent in, fade YOU out
            opponentFadeRoutine = StartCoroutine(FadeTo(opponentTopImage, 1f, 0.4f));
            youFadeRoutine = StartCoroutine(FadeTo(youTopImage, 0f, 0.4f));
        }
    }

    IEnumerator FadeTo(UnityEngine.UI.Image img, float targetAlpha, float duration)
    {
        if (!img) yield break;

        float startAlpha = img.color.a;
        float t = 0f;
        var baseColor = img.color;

        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(startAlpha, targetAlpha, t / duration);
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            yield return null;
        }

        img.color = new Color(baseColor.r, baseColor.g, baseColor.b, targetAlpha);
    }

    void SetImageAlpha(UnityEngine.UI.Image img, float a)
    {
        if (!img) return;
        var c = img.color;
        img.color = new Color(c.r, c.g, c.b, a);
    }

    IEnumerator FadeInThenOut(UnityEngine.UI.Image img, float duration, float hold)
    {
        if (!img) yield break;
        var baseColor = img.color;

        // fade in
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(0f, 1f, t / duration);
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            yield return null;
        }
        img.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

        yield return new WaitForSeconds(hold);

        // fade out
        t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / duration);
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            yield return null;
        }
        img.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
    }
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (gameEnded) return; // ✅ don’t override a finished match
        Debug.Log("Opponent left the room, ending game as tie...");
        pv.RPC(nameof(RPC_GameOver), RpcTarget.All, "It's a Tie!");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (gameEnded) return; // ✅ don’t override a finished match
        Debug.Log("Disconnected: " + cause.ToString());
        pv.RPC(nameof(RPC_GameOver), RpcTarget.All, "It's a Tie!");
    }



    public void OnHomeButton()
    {
        // Go back to Main Menu
        AdManager.Instance.ConcealBanner(); // ✅ hide banner when leaving game
        SceneManager.LoadScene("MainMenu");
    }


    // Called when Exit button is clicked
    public void OnExitButton()
    {
        if (exitPanel != null)
            exitPanel.SetActive(true); // Show confirm panel
    }

    // Called when "No" is clicked
    public void OnExitCancel()
    {
        if (exitPanel != null)
            exitPanel.SetActive(false); // Hide panel
    }

    // Called when "Yes" is clicked
    public void OnExitConfirmed()
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom(); // This will trigger OnPlayerLeftRoom on the other side
        }
        else
        {
            // Offline fallback
            SceneManager.LoadScene("MainMenu");
        }
    }

    // Handle after leaving room locally
    public override void OnLeftRoom()
    {
        AdManager.Instance.ConcealBanner();
        SceneManager.LoadScene("MainMenu");
    }




    [PunRPC]
    void RPC_ShowLastMovePanel(int roundIndex)
    {
        // mark locally (defensive)
        lastTwoTurnsShown = true;
        ShowLastMovePopup();
    }

    [PunRPC]
    void RPC_HideLastMovePanel()
    {
        HideLastMovePanelLocal();
    }
    void ShowLastMovePanelLocal(int roundIndex)
    {
        if (!lastTwoTurnsShown && usedBeats == totalBeats - 2)
        {
            lastTwoTurnsShown = true;
            ShowLastMovePopup(); // auto-hide popup
        }
    }

    void HideLastMovePanelLocal()
    {
        if (lastMovePanel == null) return;
        lastMovePanel.SetActive(false);
    }
    void ShowLastMovePopup()
    {
        if (lastMovePanel != null)
            StartCoroutine(ShowLastMovePopupRoutine());
    }
    IEnumerator ShowLastMovePopupRoutine()
    {
        lastMovePanel.SetActive(true);
        yield return new WaitForSeconds(2f); // show for 2 seconds (adjust as you like)
        lastMovePanel.SetActive(false);
    }
    private void UpdateScoreUI()
    {
        if (playerScoreText == null || opponentScoreText == null)
            return;

        if (localPlayerId == 1)
        {
            // I am Player 1 → right side is me
            opponentScoreText.text = player1Score.ToString(); // my side (right)
            playerScoreText.text = player2Score.ToString();   // opponent side (left)
        }
        else
        {
            // I am Player 2 → right side is me
            opponentScoreText.text = player2Score.ToString(); // my side (right)
            playerScoreText.text = player1Score.ToString();   // opponent side (left)
        }
    }


}
