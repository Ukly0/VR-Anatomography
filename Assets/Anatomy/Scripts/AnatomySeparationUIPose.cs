using DemoMedicine.Anatomy;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

public class AnatomySeparationUIPose : MonoBehaviour
{
    [SerializeField] private AnatomyExploder exploder;
    [SerializeField] private AnatomyBoneSocketController socketController;
    [SerializeField] private Transform target;
    [FormerlySerializedAs("separatedYOffset")]
    [SerializeField] private float separatedZOffset = 0.25f;
    [SerializeField] private float duration = 0.25f;
    [SerializeField] private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 originalLocalPosition;
    private Coroutine moveRoutine;
    private bool hasOriginalLocalPosition;
    private bool lastSeparatedState;
    private bool waitingForExploderSync;

    private void Awake()
    {
        EnsureTarget();
        CacheOriginalLocalPosition();
    }

    private void OnEnable()
    {
        EnsureTarget();
        CacheOriginalLocalPosition();
        ResolveSocketController();

        if (socketController != null)
        {
            socketController.ImmediateSeparationStateChanged += OnImmediateSeparationStateChanged;
        }

        if (exploder != null)
        {
            exploder.SeparationChanged.AddListener(OnSeparationChanged);
        }

        lastSeparatedState = exploder != null && exploder.IsSeparated;
        ApplyImmediate(lastSeparatedState);
    }

    private void OnDisable()
    {
        if (socketController != null)
        {
            socketController.ImmediateSeparationStateChanged -= OnImmediateSeparationStateChanged;
        }

        if (exploder != null)
        {
            exploder.SeparationChanged.RemoveListener(OnSeparationChanged);
        }
    }

    private void LateUpdate()
    {
        if (exploder == null)
        {
            return;
        }

        var isSeparated = exploder.IsSeparated;
        if (waitingForExploderSync)
        {
            if (isSeparated == lastSeparatedState)
            {
                waitingForExploderSync = false;
            }

            return;
        }

        if (isSeparated == lastSeparatedState)
        {
            return;
        }

        lastSeparatedState = isSeparated;
        MoveTo(GetTargetPosition(isSeparated));
    }

    private void OnSeparationChanged(bool separated)
    {
        waitingForExploderSync = false;

        if (separated == lastSeparatedState)
        {
            return;
        }

        lastSeparatedState = separated;
        MoveTo(GetTargetPosition(separated));
    }

    public void MoveForPendingToggle()
    {
        if (exploder == null)
        {
            return;
        }

        var nextSeparatedState = !exploder.IsSeparated;
        lastSeparatedState = nextSeparatedState;
        waitingForExploderSync = true;
        MoveTo(GetTargetPosition(nextSeparatedState));
    }

    private void OnImmediateSeparationStateChanged(bool separated)
    {
        lastSeparatedState = separated;
        waitingForExploderSync = true;
        MoveTo(GetTargetPosition(separated));
    }

    private void ApplyImmediate(bool separated)
    {
        if (target == null)
        {
            return;
        }

        target.localPosition = GetTargetPosition(separated);
    }

    private Vector3 GetTargetPosition(bool separated)
    {
        var targetPosition = originalLocalPosition;

        if (separated)
        {
            targetPosition.z += separatedZOffset;
        }

        return targetPosition;
    }

    private void MoveTo(Vector3 targetPosition)
    {
        if (target == null)
        {
            return;
        }

        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
        }

        if (duration <= 0f)
        {
            target.localPosition = targetPosition;
            return;
        }

        moveRoutine = StartCoroutine(MoveRoutine(targetPosition));
    }

    private IEnumerator MoveRoutine(Vector3 targetPosition)
    {
        var startPosition = target.localPosition;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var easedT = animationCurve != null ? animationCurve.Evaluate(t) : t;

            target.localPosition = Vector3.LerpUnclamped(startPosition, targetPosition, easedT);

            yield return null;
        }

        target.localPosition = targetPosition;
        moveRoutine = null;
    }

    private void EnsureTarget()
    {
        if (target == null)
        {
            target = transform;
        }
    }

    private void CacheOriginalLocalPosition()
    {
        if (target == null || hasOriginalLocalPosition)
        {
            return;
        }

        originalLocalPosition = target.localPosition;
        hasOriginalLocalPosition = true;
    }

    private void ResolveSocketController()
    {
        if (socketController != null || exploder == null)
        {
            return;
        }

        socketController = exploder.GetComponent<AnatomyBoneSocketController>();
    }
}
