using UnityEngine;
using DG.Tweening;
public class UIAnimation_Dotween : MonoBehaviour
{
    // Enum for selecting the type of animation
    public enum AnimationType
    {
        Rotation,
        Position,
        Scale
    }
    [SerializeField] private AnimationType animationType;
    [SerializeField] private Ease easeType = Ease.Linear;  // Ease type from DOTween
    [SerializeField] private float delay = 0f;  // Delay before the animation starts
    [SerializeField] private float animationTime = 1f;  // Duration of the animation
    [SerializeField] private Vector3 targetValue;  // Target value for the animation
    // Private variables to store the initial state
    private Vector3 initialPosition;
    private Vector3 initialRotation;
    private Vector3 initialScale;
    private RectTransform IRectTransform;
    private void OnEnable()
    {
        if (IRectTransform == null)
            IRectTransform=GetComponent<RectTransform>();
        // Store the initial state based on the animation type

        initialPosition = IRectTransform.anchoredPosition;
        initialRotation = transform.localEulerAngles;
        initialScale = transform.localScale;
        // Play the desired animation based on the selected type
        switch (animationType)
        {
            case AnimationType.Rotation:
                transform.DOLocalRotate(targetValue, animationTime)
                    .SetEase(easeType)
                    .SetDelay(delay)
                    .SetUpdate(true);
                break;
            case AnimationType.Position:
                IRectTransform.DOAnchorPos(targetValue, animationTime)
                    .SetEase(easeType)
                    .SetDelay(delay)
                    .SetUpdate(true);
                break;
            case AnimationType.Scale:
                transform.DOScale(targetValue, animationTime)
                    .SetEase(easeType)
                    .SetDelay(delay)
                    .SetUpdate(true);
                break;
        }
    }
    private void OnDisable()
    {
        // Reset to the initial state when the object is disabled
        switch (animationType)
        {
            case AnimationType.Rotation:
                transform.localEulerAngles = initialRotation;
                break;
            case AnimationType.Position:
                IRectTransform.DOAnchorPos(initialPosition,0);
                break;
            case AnimationType.Scale:
                transform.localScale = initialScale;
                break;
        }
    }
}