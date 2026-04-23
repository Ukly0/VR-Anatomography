using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomyExploder : MonoBehaviour
    {
        [SerializeField] private Transform partsRoot;
        [SerializeField] private Transform customCenter;
        [SerializeField] private CenterMode centerMode = CenterMode.BoundsCenter;
        [SerializeField] private bool includeInactiveParts = true;
        [SerializeField] private float separationDistance = 0.25f;
        [SerializeField] private float animationDuration = 0.75f;
        [SerializeField] private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private bool enableCollidersOnlyWhenSeparated;
        [SerializeField] private UnityEvent<bool> separationChanged;

        private readonly List<PartState> parts = new List<PartState>();
        private Coroutine animationRoutine;
        private bool isSeparated;

        public bool IsSeparated => isSeparated;

        private void Awake()
        {
            RebuildParts();
            SetPartCollidersEnabled(!enableCollidersOnlyWhenSeparated || isSeparated);
        }

        private void OnValidate()
        {
            separationDistance = Mathf.Max(0f, separationDistance);
            animationDuration = Mathf.Max(0.01f, animationDuration);
        }

        public void ToggleSeparated()
        {
            SetSeparated(!isSeparated);
        }

        public void Separate()
        {
            SetSeparated(true);
        }

        public void Assemble()
        {
            SetSeparated(false);
        }

        public void SetSeparated(bool separated)
        {
            if (parts.Count == 0)
            {
                RebuildParts();
            }

            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
            }

            isSeparated = separated;
            animationRoutine = StartCoroutine(AnimateParts(separated));
        }

        public void RebuildParts()
        {
            parts.Clear();

            var root = partsRoot != null ? partsRoot : transform;
            var anatomyParts = root.GetComponentsInChildren<AnatomyPart>(includeInactiveParts);
            var center = GetExplosionCenter(root, anatomyParts);

            foreach (var anatomyPart in anatomyParts)
            {
                var partTransform = anatomyPart.transform;
                var direction = GetPartCenter(partTransform) - center;

                if (direction.sqrMagnitude < 0.000001f)
                {
                    direction = partTransform.position - root.position;
                }

                if (direction.sqrMagnitude < 0.000001f)
                {
                    direction = root.up;
                }

                direction.Normalize();

                var localSpace = partTransform.parent != null ? partTransform.parent : root;

                parts.Add(new PartState(
                    partTransform,
                    partTransform.localPosition,
                    partTransform.localPosition + localSpace.InverseTransformVector(direction * separationDistance),
                    partTransform.GetComponentsInChildren<Collider>(true)));
            }
        }

        private IEnumerator AnimateParts(bool separated)
        {
            if (separated && enableCollidersOnlyWhenSeparated)
            {
                SetPartCollidersEnabled(true);
            }

            var elapsed = 0f;
            var startPositions = new Vector3[parts.Count];

            for (var i = 0; i < parts.Count; i++)
            {
                startPositions[i] = parts[i].Transform.localPosition;
            }

            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                var t = animationCurve.Evaluate(Mathf.Clamp01(elapsed / animationDuration));

                for (var i = 0; i < parts.Count; i++)
                {
                    var targetPosition = separated ? parts[i].SeparatedPosition : parts[i].OriginalPosition;
                    parts[i].Transform.localPosition = Vector3.LerpUnclamped(startPositions[i], targetPosition, t);
                }

                yield return null;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                parts[i].Transform.localPosition = separated ? parts[i].SeparatedPosition : parts[i].OriginalPosition;
            }

            if (!separated && enableCollidersOnlyWhenSeparated)
            {
                SetPartCollidersEnabled(false);
            }

            animationRoutine = null;
            separationChanged.Invoke(separated);
        }

        private Vector3 GetExplosionCenter(Transform root, AnatomyPart[] anatomyParts)
        {
            if (centerMode == CenterMode.CustomTransform && customCenter != null)
            {
                return customCenter.position;
            }

            if (centerMode == CenterMode.TransformPosition)
            {
                return root.position;
            }

            return GetBoundsCenter(anatomyParts, root.position);
        }

        private static Vector3 GetBoundsCenter(AnatomyPart[] anatomyParts, Vector3 fallback)
        {
            var initialized = false;
            var bounds = new Bounds(fallback, Vector3.zero);

            foreach (var anatomyPart in anatomyParts)
            {
                var renderers = anatomyPart.GetComponentsInChildren<Renderer>(true);

                foreach (var renderer in renderers)
                {
                    if (!initialized)
                    {
                        bounds = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            return initialized ? bounds.center : fallback;
        }

        private static Vector3 GetPartCenter(Transform partTransform)
        {
            var renderers = partTransform.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return partTransform.position;
            }

            var bounds = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.center;
        }

        private void SetPartCollidersEnabled(bool enabled)
        {
            foreach (var part in parts)
            {
                foreach (var collider in part.Colliders)
                {
                    collider.enabled = enabled;
                }
            }
        }

        private enum CenterMode
        {
            BoundsCenter,
            TransformPosition,
            CustomTransform
        }

        private readonly struct PartState
        {
            public readonly Transform Transform;
            public readonly Vector3 OriginalPosition;
            public readonly Vector3 SeparatedPosition;
            public readonly Collider[] Colliders;

            public PartState(
                Transform transform,
                Vector3 originalPosition,
                Vector3 separatedPosition,
                Collider[] colliders)
            {
                Transform = transform;
                OriginalPosition = originalPosition;
                SeparatedPosition = separatedPosition;
                Colliders = colliders;
            }
        }
    }
}
