using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DemoMedicine.Anatomy
{
    [DisallowMultipleComponent]
    public sealed class AnatomyBoneSocketController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AnatomyExploder exploder;
        [SerializeField] private XRGrabInteractable wholeGrabInteractable;
        [SerializeField] private Rigidbody wholeRigidbody;
        [SerializeField] private Collider wholeCollider;
        [SerializeField] private XRSocketInteractor wholeSocketInteractor;
        [SerializeField] private XRInteractionManager interactionManager;
        [SerializeField] private Transform socketsRoot;

        [Header("Part Setup")]
        [SerializeField] private bool autoConfigureParts = true;
        [SerializeField] private bool autoAddHighlights = true;
        [SerializeField] private int interactionLayerStartBit = 8;
        [SerializeField] private float socketBoundsPadding = 1.15f;
        [SerializeField] private bool hideSocketsUntilSeparated = true;

        [SerializeField] private List<PartBinding> partBindings = new List<PartBinding>();

        private bool configured;
        private bool rootStateCached;
        private bool wholeGrabInitialEnabled;
        private bool wholeColliderInitialEnabled;
        private bool wholeRigidbodyInitialKinematic;
        private bool wholeRigidbodyInitialUseGravity;

        private void Awake()
        {
            EnsureConfigured();
            ApplyCurrentState();
        }

        private void Start()
        {
            EnsureConfigured();
            ApplyCurrentState();
        }

        private void OnValidate()
        {
            interactionLayerStartBit = Mathf.Clamp(interactionLayerStartBit, 0, 30);
            socketBoundsPadding = Mathf.Max(1f, socketBoundsPadding);
        }

        public void ToggleSeparated()
        {
            EnsureConfigured();

            if (exploder == null)
            {
                Debug.LogWarning("AnatomyBoneSocketController requires an AnatomyExploder reference.", this);
                return;
            }

            if (exploder.IsSeparated)
            {
                Assemble();
            }
            else
            {
                Separate();
            }
        }

        public void Separate()
        {
            EnsureConfigured();

            if (exploder == null || exploder.IsSeparated)
            {
                return;
            }

            exploder.Separate();
        }

        public void Assemble()
        {
            EnsureConfigured();

            if (exploder == null || !exploder.IsSeparated)
            {
                return;
            }

            SetPartModeActive(false);
            exploder.Assemble();
        }

        public void HandleExploderSeparationChanged(bool separated)
        {
            EnsureConfigured();

            if (separated)
            {
                RefreshSocketPoses();
            }

            SetPartModeActive(separated);
        }

        public void RebuildConfiguration()
        {
            configured = false;
            partBindings.Clear();
            EnsureConfigured();
            ApplyCurrentState();
        }

        private void EnsureConfigured()
        {
            if (configured)
            {
                return;
            }

            CacheRootReferences();
            EnsureSocketsRoot();

            if (autoConfigureParts)
            {
                BuildPartBindings();
            }

            configured = true;
        }

        private void CacheRootReferences()
        {
            if (exploder == null)
            {
                exploder = GetComponent<AnatomyExploder>();
            }

            if (wholeGrabInteractable == null)
            {
                wholeGrabInteractable = GetComponent<XRGrabInteractable>();

                if (wholeGrabInteractable == null)
                {
                    wholeGrabInteractable = GetComponentInParent<XRGrabInteractable>();
                }
            }

            if (wholeRigidbody == null)
            {
                wholeRigidbody = GetComponent<Rigidbody>();

                if (wholeRigidbody == null && wholeGrabInteractable != null)
                {
                    wholeRigidbody = wholeGrabInteractable.GetComponent<Rigidbody>();
                }
            }

            if (wholeCollider == null)
            {
                wholeCollider = GetComponent<Collider>();

                if (wholeCollider == null && wholeGrabInteractable != null)
                {
                    wholeCollider = wholeGrabInteractable.GetComponent<Collider>();
                }
            }

            if (wholeSocketInteractor == null)
            {
                wholeSocketInteractor = GetComponent<XRSocketInteractor>();

                if (wholeSocketInteractor == null)
                {
                    wholeSocketInteractor = GetComponentInParent<XRSocketInteractor>();
                }
            }

            if (interactionManager == null)
            {
                interactionManager = GetComponent<XRInteractionManager>();

                if (interactionManager == null && wholeGrabInteractable != null)
                {
                    interactionManager = wholeGrabInteractable.interactionManager;
                }

                if (interactionManager == null && wholeSocketInteractor != null)
                {
                    interactionManager = wholeSocketInteractor.interactionManager;
                }

                if (interactionManager == null)
                {
                    interactionManager = FindFirstObjectByType<XRInteractionManager>();
                }
            }

            if (!rootStateCached)
            {
                wholeGrabInitialEnabled = wholeGrabInteractable == null || wholeGrabInteractable.enabled;
                wholeColliderInitialEnabled = wholeCollider == null || wholeCollider.enabled;
                wholeRigidbodyInitialKinematic = wholeRigidbody != null && wholeRigidbody.isKinematic;
                wholeRigidbodyInitialUseGravity = wholeRigidbody != null && wholeRigidbody.useGravity;
                rootStateCached = true;
            }
        }

        private void EnsureSocketsRoot()
        {
            if (socketsRoot != null)
            {
                return;
            }

            var existingRoot = transform.Find("BoneSocketsRoot");

            if (existingRoot != null)
            {
                socketsRoot = existingRoot;
                return;
            }

            var socketsRootObject = new GameObject("BoneSocketsRoot");
            socketsRoot = socketsRootObject.transform;
            socketsRoot.SetParent(transform, false);
            socketsRoot.localPosition = Vector3.zero;
            socketsRoot.localRotation = Quaternion.identity;
            socketsRoot.localScale = Vector3.one;
        }

        private void BuildPartBindings()
        {
            partBindings.Clear();

            var anatomyParts = GetComponentsInChildren<AnatomyPart>(true);
            var maxParts = 32 - interactionLayerStartBit;

            if (anatomyParts.Length > maxParts)
            {
                Debug.LogWarning(
                    $"Only {maxParts} unique interaction layer bits are available from bit {interactionLayerStartBit}. " +
                    $"Extra parts will share the last available bit.",
                    this);
            }

            for (var index = 0; index < anatomyParts.Length; index++)
            {
                var anatomyPart = anatomyParts[index];

                if (anatomyPart == null || anatomyPart.transform == transform)
                {
                    continue;
                }

                partBindings.Add(CreateOrUpdateBinding(anatomyPart, index));
            }
        }

        private PartBinding CreateOrUpdateBinding(AnatomyPart anatomyPart, int index)
        {
            var partTransform = anatomyPart.transform;
            var localBounds = GetLocalRendererBounds(partTransform);
            var socketName = $"Socket_{partTransform.name}";
            var attachName = "GrabAttach";
            var socketAttachName = "SocketAttach";

            var partCollider = partTransform.GetComponent<BoxCollider>();

            if (partCollider == null)
            {
                partCollider = partTransform.gameObject.AddComponent<BoxCollider>();
            }

            partCollider.isTrigger = false;
            partCollider.center = localBounds.center;
            partCollider.size = GetSafeSize(localBounds.size);

            var partRigidbody = partTransform.GetComponent<Rigidbody>();

            if (partRigidbody == null)
            {
                partRigidbody = partTransform.gameObject.AddComponent<Rigidbody>();
            }

            partRigidbody.useGravity = false;
            partRigidbody.isKinematic = true;
            partRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var partGrab = partTransform.GetComponent<XRGrabInteractable>();

            if (partGrab == null)
            {
                partGrab = partTransform.gameObject.AddComponent<XRGrabInteractable>();
            }

            var interactionMask = GetMaskForIndex(index);
            partGrab.interactionManager = interactionManager;
            partGrab.interactionLayers = interactionMask;
            partGrab.movementType = XRBaseInteractable.MovementType.Kinematic;
            partGrab.attachEaseInTime = 0f;
            partGrab.throwOnDetach = false;
            partGrab.useDynamicAttach = true;
            partGrab.matchAttachPosition = true;
            partGrab.matchAttachRotation = true;
            partGrab.snapToColliderVolume = true;
            partGrab.retainTransformParent = true;

            if (!partGrab.colliders.Contains(partCollider))
            {
                partGrab.colliders.Clear();
                partGrab.colliders.Add(partCollider);
            }

            var grabAttach = EnsureChildTransform(partTransform, attachName);
            grabAttach.localPosition = localBounds.center;
            grabAttach.localRotation = Quaternion.identity;
            partGrab.attachTransform = grabAttach;

            AnatomyInteractableHighlight highlight = null;

            if (autoAddHighlights)
            {
                highlight = partTransform.GetComponent<AnatomyInteractableHighlight>();

                if (highlight == null)
                {
                    highlight = partTransform.gameObject.AddComponent<AnatomyInteractableHighlight>();
                }

                highlight.Configure(partTransform);
            }

            var socketTransform = EnsureChildTransform(socketsRoot, socketName);
            var socketGameObject = socketTransform.gameObject;
            var socketCollider = socketGameObject.GetComponent<BoxCollider>();

            if (socketCollider == null)
            {
                socketCollider = socketGameObject.AddComponent<BoxCollider>();
            }

            socketCollider.isTrigger = true;
            socketCollider.center = localBounds.center;
            socketCollider.size = GetSafeSize(localBounds.size * socketBoundsPadding);

            var socketInteractor = socketGameObject.GetComponent<XRSocketInteractor>();

            if (socketInteractor == null)
            {
                socketInteractor = socketGameObject.AddComponent<XRSocketInteractor>();
            }

            socketInteractor.interactionManager = interactionManager;
            socketInteractor.interactionLayers = interactionMask;
            socketInteractor.showInteractableHoverMeshes = false;
            socketInteractor.hoverSocketSnapping = true;
            socketInteractor.socketSnappingRadius = Mathf.Max(localBounds.extents.magnitude * 0.5f, 0.025f);
            socketInteractor.keepSelectedTargetValid = true;

            var socketAttach = EnsureChildTransform(socketTransform, socketAttachName);
            socketAttach.localPosition = grabAttach.localPosition;
            socketAttach.localRotation = grabAttach.localRotation;
            socketInteractor.attachTransform = socketAttach;

            return new PartBinding
            {
                anatomyPart = anatomyPart,
                collider = partCollider,
                rigidbody = partRigidbody,
                grabInteractable = partGrab,
                grabAttach = grabAttach,
                socketTransform = socketTransform,
                socketCollider = socketCollider,
                socketInteractor = socketInteractor,
                socketAttach = socketAttach,
                localBoundsCenter = localBounds.center,
                localBoundsSize = localBounds.size,
            };
        }

        private void ApplyCurrentState()
        {
            if (exploder == null)
            {
                SetPartModeActive(false);
                return;
            }

            if (exploder.IsSeparated)
            {
                RefreshSocketPoses();
            }

            SetPartModeActive(exploder.IsSeparated);
        }

        private void SetPartModeActive(bool separated)
        {
            if (wholeSocketInteractor != null)
            {
                wholeSocketInteractor.enabled = false;
            }

            if (wholeGrabInteractable != null)
            {
                wholeGrabInteractable.enabled = separated ? false : wholeGrabInitialEnabled;
            }

            if (wholeCollider != null)
            {
                wholeCollider.enabled = separated ? false : wholeColliderInitialEnabled;
            }

            if (wholeRigidbody != null)
            {
                wholeRigidbody.isKinematic = separated || wholeRigidbodyInitialKinematic;
                wholeRigidbody.useGravity = !separated && wholeRigidbodyInitialUseGravity;
            }

            foreach (var partBinding in partBindings)
            {
                if (partBinding == null)
                {
                    continue;
                }

                if (partBinding.grabInteractable != null)
                {
                    partBinding.grabInteractable.enabled = separated;
                }

                if (partBinding.collider != null)
                {
                    partBinding.collider.enabled = separated;
                }

                if (partBinding.rigidbody != null)
                {
                    partBinding.rigidbody.isKinematic = true;
                }

                if (partBinding.socketInteractor != null)
                {
                    partBinding.socketInteractor.enabled = separated;
                }

                if (partBinding.socketCollider != null)
                {
                    partBinding.socketCollider.enabled = separated;
                }

                if (partBinding.socketTransform != null)
                {
                    partBinding.socketTransform.gameObject.SetActive(separated || !hideSocketsUntilSeparated);
                }
            }
        }

        private void RefreshSocketPoses()
        {
            foreach (var partBinding in partBindings)
            {
                if (partBinding == null || partBinding.anatomyPart == null || partBinding.socketTransform == null)
                {
                    continue;
                }

                var partTransform = partBinding.anatomyPart.transform;
                partBinding.socketTransform.position = partTransform.position;
                partBinding.socketTransform.rotation = partTransform.rotation;
                partBinding.socketTransform.localScale = Vector3.one;
            }
        }

        private static Transform EnsureChildTransform(Transform parent, string childName)
        {
            var child = parent.Find(childName);

            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject(childName);
            child = childObject.transform;
            child.SetParent(parent, false);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        private static Bounds GetLocalRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.05f);
            }

            var inverseMatrix = root.worldToLocalMatrix;
            var initialized = false;
            var localBounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var renderer in renderers)
            {
                var worldBounds = renderer.bounds;
                var center = inverseMatrix.MultiplyPoint3x4(worldBounds.center);
                var extents = worldBounds.extents;

                var axisX = inverseMatrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
                var axisY = inverseMatrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
                var axisZ = inverseMatrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
                var localExtents = new Vector3(
                    Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                    Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                    Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

                var rendererBounds = new Bounds(center, localExtents * 2f);

                if (!initialized)
                {
                    localBounds = rendererBounds;
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(rendererBounds.min);
                    localBounds.Encapsulate(rendererBounds.max);
                }
            }

            return initialized ? localBounds : new Bounds(Vector3.zero, Vector3.one * 0.05f);
        }

        private static Vector3 GetSafeSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(size.x, 0.01f),
                Mathf.Max(size.y, 0.01f),
                Mathf.Max(size.z, 0.01f));
        }

        private InteractionLayerMask GetMaskForIndex(int index)
        {
            var bitIndex = Mathf.Min(interactionLayerStartBit + index, 31);
            var mask = new InteractionLayerMask();
            mask.value = 1 << bitIndex;
            return mask;
        }

        [Serializable]
        private sealed class PartBinding
        {
            public AnatomyPart anatomyPart;
            public Collider collider;
            public Rigidbody rigidbody;
            public XRGrabInteractable grabInteractable;
            public Transform grabAttach;
            public Transform socketTransform;
            public Collider socketCollider;
            public XRSocketInteractor socketInteractor;
            public Transform socketAttach;
            public Vector3 localBoundsCenter;
            public Vector3 localBoundsSize;
        }
    }
}
