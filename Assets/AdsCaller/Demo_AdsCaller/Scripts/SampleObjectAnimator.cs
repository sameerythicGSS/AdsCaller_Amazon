using UnityEngine;
using UnityEngine.Events;
using System.Collections;

[DisallowMultipleComponent]
public class SampleObjectAnimator : MonoBehaviour
{
    public enum AnimationType
    {
        Scale,
        Position,
        Rotation
    }

    [Header("Animation Settings")]
    public AnimationType animationType = AnimationType.Scale;
    public float duration = 1f;
    public bool rewindOnDisable = false;
    public bool rewindAfterCompletion = false;
    public AnimationCurve easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Target Values")]
    public Vector3 endValue = Vector3.one;

    [Header("Events")]
    public UnityEvent OnEnabled;
    public UnityEvent OnAnimationComplete;
    public UnityEvent OnDisabled;

    private Vector3 _startValue;
    private Coroutine _animRoutine;

    void OnEnable()
    {
        OnEnabled?.Invoke();
        CacheStartValue();
        if (_animRoutine != null) StopCoroutine(_animRoutine);
        _animRoutine = StartCoroutine(AnimateObject());
    }

    void OnDisable()
    {
        OnDisabled?.Invoke();

        if (rewindOnDisable)
        {
            StopAllCoroutines();
            ApplyValue(_startValue);
        }
    }

    private void CacheStartValue()
    {
        switch (animationType)
        {
            case AnimationType.Scale:
                _startValue = transform.localScale;
                break;
            case AnimationType.Position:
                _startValue = transform.localPosition;
                break;
            case AnimationType.Rotation:
                _startValue = transform.localEulerAngles;
                break;
        }
    }

    private IEnumerator AnimateObject()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = easeCurve.Evaluate(elapsed / duration);
            ApplyValue(Vector3.LerpUnclamped(_startValue, endValue, t));
            yield return null;
        }

        ApplyValue(endValue);
        OnAnimationComplete?.Invoke();

        if (rewindAfterCompletion)
        {
            yield return StartCoroutine(Rewind());
        }
    }

    private IEnumerator Rewind()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = easeCurve.Evaluate(elapsed / duration);
            ApplyValue(Vector3.LerpUnclamped(endValue, _startValue, t));
            yield return null;
        }

        ApplyValue(_startValue);
    }

    private void ApplyValue(Vector3 value)
    {
        switch (animationType)
        {
            case AnimationType.Scale:
                transform.localScale = value;
                break;
            case AnimationType.Position:
                transform.localPosition = value;
                break;
            case AnimationType.Rotation:
                transform.localEulerAngles = value;
                break;
        }
    }

    // Optional: Draw gizmos so you can see the end point for position anims
    void OnDrawGizmosSelected()
    {
        if (animationType == AnimationType.Position)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.parent ? transform.parent.TransformPoint(endValue) : endValue, 0.1f);
        }
    }
}
