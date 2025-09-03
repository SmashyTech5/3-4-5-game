using UnityEngine;
using Photon.Pun;

public class OrbitCameraMultiplayer : MonoBehaviour
{
    public Transform target;
    public float distance = 5.0f;
    public float xSpeed = 100.0f;
    public float ySpeed = 100.0f;
    public float yMinLimit = 10f;
    public float yMaxLimit = 60f;

    private float x = 0.0f;
    private float y = 45.0f;

    void Start()
    {
        // Find my own player in the scene (unsorted for better performance)
        var views = FindObjectsByType<PhotonView>(FindObjectsSortMode.None);

        foreach (var view in views)
        {
            if (view.IsMine)
            {
                target = view.transform; // Attach camera to my own player
                break;
            }
        }

        if (target == null)
        {
            Debug.LogError("No local player found for camera to follow!");
            enabled = false;
            return;
        }

        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Mobile control
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Moved)
            {
                x += touch.deltaPosition.x * xSpeed * 0.02f;
                y -= touch.deltaPosition.y * ySpeed * 0.02f;
                y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
            }
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButton(0))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * 0.02f;
            y -= Input.GetAxis("Mouse Y") * ySpeed * 0.02f;
            y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
        }
#endif

        Quaternion rotation = Quaternion.Euler(y, x, 0);
        Vector3 position = rotation * new Vector3(0.0f, 0.0f, -distance) + target.position;

        transform.rotation = rotation;
        transform.position = position;
    }
}
