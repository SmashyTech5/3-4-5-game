
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
#if TMP_PRESENT
using TMPro;
#endif

public class MainMenu : MonoBehaviourPunCallbacks
{
    // state flags for cancelling/back behavior
    private bool isSearchingForRoom = false;    // true while trying to join/create a room
    private bool aiLoadRequested = false;       // true while AI load is pending (gives time to cancel)
    private Coroutine aiLoadCoroutine = null;
    public float aiLoadDelay = 7f;            // how long the loading panel waits before actually loading AI scene
    private bool multiplayerStarted = false;  // true only once game scene is loaded
    private bool coinsDeductedForMultiplayer = false; // 👈 NEW flag

    [Header("Coins UI")]
    public Text coinsText; // Assign in Inspector
    private int playerCoins;
    [Header("Scenes")]
    public string multiplayerSceneName = "MultiplayerMode";
    public string aiSceneName = "AiMode";

    [Header("Room")]
    public byte maxPlayersPerRoom = 2;

    [Header("UI - assign in inspector")]
    public Button aiButton;
    public Button onlineButton;

    // optional status text: either regular Text or TextMeshPro (assign one)
    public Text statusTextUI;
#if TMP_PRESENT
    public TMP_Text statusTextTMP;
#endif

    public GameObject loadingPanel; // optional "waiting" panel
    public GameObject mainMenuPanel;
    public GameObject notEnoughCoinsPopup; // assign in inspector
    public GameObject noInternetPopup; // assign in Inspector

    void Start()
    {
        if (!PlayerPrefs.HasKey("PlayerCoins"))
        {
            PlayerPrefs.SetInt("PlayerCoins", 1000); // first time bonus
            PlayerPrefs.Save();  // 👈 make sure it writes
        }
        playerCoins = PlayerPrefs.GetInt("PlayerCoins");
        UpdateCoinsUI();
        // DON'T add listeners here to avoid disturbing existing inspector bindings.
        if (loadingPanel != null) loadingPanel.SetActive(false);
        ClearStatus();
        multiplayerStarted = false;

    }
    void UpdateCoinsUI()
    {
        coinsText.text = "" + playerCoins;
    }
    #region Button handlers (assign these methods to the Button.OnClick in Inspector)
    // Assign aiButton -> OnAiModeButton
    public void OnAiModeButton()
    {
        if (playerCoins >= 100)
        {
            // Show loading UI but don't deduct coins yet
            if (loadingPanel != null) loadingPanel.SetActive(true);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

            aiLoadRequested = true;
            if (aiLoadCoroutine != null) StopCoroutine(aiLoadCoroutine);
            aiLoadCoroutine = StartCoroutine(LoadAiSceneDelayed());
        }
        else
        {
            if (notEnoughCoinsPopup != null) notEnoughCoinsPopup.SetActive(true);
            Debug.Log("Not enough coins!");
        }
    }


    private IEnumerator LoadAiSceneDelayed()
    {
        float remaining = aiLoadDelay;
        while (remaining > 0f)
        {
            if (!aiLoadRequested)
            {
                aiLoadCoroutine = null;
                yield break; // cancelled
            }

            SetStatus("Loading AI Mode... " + Mathf.CeilToInt(remaining));
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        // Only now deduct coins
        if (playerCoins >= 100)
        {
            playerCoins -= 100;
            PlayerPrefs.SetInt("PlayerCoins", playerCoins);
            PlayerPrefs.SetInt("PotCoins", 200); // 100 mine + 100 AI
            PlayerPrefs.Save();
            UpdateCoinsUI();
        }

        aiLoadCoroutine = null;
        aiLoadRequested = false;
        SceneManager.LoadScene(aiSceneName);
    }




    // Assign onlineButton -> OnMultiplayerButton
    public void OnMultiplayerButton()
    {
        // 🔹 First check if player has enough coins
        if (playerCoins < 100)
        {
            notEnoughCoinsPopup.SetActive(true);
            return;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Debug.Log("No internet!");
            if (noInternetPopup != null) noInternetPopup.SetActive(true);
            return;
        }

        // 🔹 Continue with normal Photon flow
        SetStatus("Connecting to server...");
        if (loadingPanel != null) loadingPanel.SetActive(true);
        isSearchingForRoom = true; // 👈 ADD THIS (important!)
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.JoinRandomRoom();
        else
            PhotonNetwork.ConnectUsingSettings();
    }

    #endregion

    #region Photon callbacks
    public override void OnConnectedToMaster()
    {
        SetStatus("Connected! Searching for room...");
        PhotonNetwork.JoinRandomRoom();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        SetStatus("No rooms found. Creating room...");
        RoomOptions opts = new RoomOptions { MaxPlayers = maxPlayersPerRoom };
        PhotonNetwork.CreateRoom(null, opts);
    }

    public override void OnJoinedRoom()
    {
        isSearchingForRoom = false; // 👈 done searching
        SetStatus($"Joined room. Waiting for player {PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayersPerRoom}...");
        CheckPlayersInRoom();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        SetStatus($"Player joined! ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayersPerRoom})");
        CheckPlayersInRoom();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        isSearchingForRoom = false; // 👈 reset on disconnect
        SetStatus("Disconnected: " + cause.ToString());
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }
    #endregion

    void CheckPlayersInRoom()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        if (PhotonNetwork.CurrentRoom.PlayerCount >= maxPlayersPerRoom)
        {
            SetStatus("Both players ready! Starting game...");

            // Deduct 100 coins when multiplayer starts (only once)
            if (!coinsDeductedForMultiplayer)
            {
                playerCoins -= 100;
                coinsDeductedForMultiplayer = true;
                PlayerPrefs.SetInt("PlayerCoins", playerCoins);
                PlayerPrefs.SetInt("PotCoins", 200);
                PlayerPrefs.Save();
                UpdateCoinsUI();
            }

            multiplayerStarted = true;
            PhotonNetwork.LoadLevel(multiplayerSceneName);
        }
        else
        {
            SetStatus($"Waiting for player {PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayersPerRoom}...");
        }
    }



    #region Helpers
    void SetStatus(string s)
    {
#if TMP_PRESENT
        if (statusTextTMP != null) statusTextTMP.text = s;
        else
#endif
        if (statusTextUI != null) statusTextUI.text = s;
        else Debug.Log("[MainMenuSafe] " + s);
    }

    void ClearStatus()
    {
#if TMP_PRESENT
        if (statusTextTMP != null) statusTextTMP.text = "";
        else
#endif
        if (statusTextUI != null) statusTextUI.text = "";
    }
    #endregion


    public void OnRetryInternetButton()
    {
        noInternetPopup.SetActive(false); // hide popup
        OnMultiplayerButton(); // try again
    }

    public void OnWatchAdForCoinsButton()
    {
        // 💰 Add 100 coins instantly for testing
        playerCoins += 100;
        PlayerPrefs.SetInt("PlayerCoins", playerCoins);
        PlayerPrefs.Save();
        UpdateCoinsUI();

        if (notEnoughCoinsPopup != null)
            notEnoughCoinsPopup.SetActive(false);

        Debug.Log("Test: Player rewarded with 100 coins!");
        /* if (Application.internetReachability == NetworkReachability.NotReachable)
         {
             Debug.LogWarning("No internet connection. Cannot show ad.");

             // Close the "Not Enough Coins" popup if it's open
             if (notEnoughCoinsPopup != null)
                 notEnoughCoinsPopup.SetActive(false);

             // Show the "No Internet" popup
             if (noInternetPopup != null)
                 noInternetPopup.SetActive(true);

             return;
         }

         // Player has internet → try to show rewarded ad
         AdManager.Instance.ShowRewardedAdvertisement(() =>
         {
             playerCoins += 50;
             PlayerPrefs.SetInt("PlayerCoins", playerCoins);
             PlayerPrefs.Save();
             UpdateCoinsUI();

             if (notEnoughCoinsPopup != null)
                 notEnoughCoinsPopup.SetActive(false);

             Debug.Log("Player rewarded with 50 coins!");
         },
         () =>
         {
             Debug.LogWarning("Rewarded ad not ready yet!");
         });*/
    }

    public void OnBackButton()
    {
        // ✅ Case A: Cancel AI load
        if (aiLoadRequested)
        {
            aiLoadRequested = false;
            if (aiLoadCoroutine != null)
            {
                StopCoroutine(aiLoadCoroutine);
                aiLoadCoroutine = null;
            }

            // Restore UI
            if (loadingPanel != null) loadingPanel.SetActive(false);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
            ClearStatus();

            Debug.Log("AI load cancelled.");
            return;
        }

        // ✅ Case B: Cancel Multiplayer Search / Room
        if (isSearchingForRoom)
        {
            isSearchingForRoom = false;

            // Restore UI immediately
            if (loadingPanel != null) loadingPanel.SetActive(false);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
            ClearStatus();

            // Refund coins ONLY if they were deducted already
            if (!multiplayerStarted && coinsDeductedForMultiplayer)
            {
                playerCoins += 100;
                coinsDeductedForMultiplayer = false; // reset flag

                PlayerPrefs.SetInt("PlayerCoins", playerCoins);
                PlayerPrefs.SetInt("PotCoins", 0);
                PlayerPrefs.Save();
                UpdateCoinsUI();

                Debug.Log("Multiplayer cancelled before start → coins refunded.");
            }

            // If already inside a room → leave it
            if (PhotonNetwork.InRoom)
            {
                PhotonNetwork.LeaveRoom();
                Debug.Log("Leaving Photon room due to Back pressed.");
            }
            else if (PhotonNetwork.IsConnected)
            {
                PhotonNetwork.Disconnect();
                Debug.Log("Disconnecting from Photon due to Back pressed.");
            }

            return;
        }

        // ✅ Case C: Default (not loading anything) → just restore UI
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        ClearStatus();

        Debug.Log("Back → default main menu restore.");
    }




}
