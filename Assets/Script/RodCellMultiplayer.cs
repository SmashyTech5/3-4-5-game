using UnityEngine;

public class RodCellMultiplayer : MonoBehaviour
{
    private GameManagerMultiplayer gameManager;
    private int x, z;
    private bool interactable;
    private Collider col;
    private Renderer rend;

    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private Color lockedColor = Color.gray;

    void Awake()
    {
        col = GetComponent<Collider>();
        rend = GetComponentInChildren<Renderer>();
    }

    public void Setup(GameManagerMultiplayer gm, int gx, int gz)
    {
        gameManager = gm;
        x = gx;
        z = gz;
    }

    public void SetInteractable(bool on)
    {
        interactable = on;
        if (col != null) col.enabled = on;
        rend.material.color = on ? activeColor : lockedColor;
    }

    private void OnMouseDown()
    {
        if (!interactable || gameManager == null) return;
        gameManager.RequestPlaceBall(x, z);
    }
    public void SetHighlighted(bool on)
    {
        if (rend == null) rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;

        // yellow tint if highlighted, otherwise keep current interactable color
        if (on)
            rend.material.color = Color.yellow;
        else
            rend.material.color = interactable ? activeColor : lockedColor;
    }

}
