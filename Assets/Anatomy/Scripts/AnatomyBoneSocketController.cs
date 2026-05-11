using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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

        [Header("Socket Ghosts")]
        [SerializeField] private bool showSocketGhosts = true;
        [SerializeField] private Color socketGhostColor = new Color(0.82f, 0.84f, 0.86f, 0.2f);
        [SerializeField] private float socketRevealDistance = 0.12f;
        [SerializeField] private float socketGhostHideDistance = 0.025f;
        [SerializeField] private float socketGhostReleaseMargin = 0.02f;
        [SerializeField] private float socketGhostScale = 1.01f;

        [Header("Reassembly")]
        [SerializeField] private AnimationCurve reassemblyCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 2.2f, 2.2f),
            new Keyframe(1f, 1f, 0f, 0f));

        [SerializeField] private List<PartBinding> partBindings = new List<PartBinding>();

        private bool configured;
        private bool rootStateCached;
        private bool wholeGrabInitialEnabled;
        private bool wholeColliderInitialEnabled;
        private bool wholeRigidbodyInitialKinematic;
        private bool wholeRigidbodyInitialUseGravity;
        private Coroutine reassemblyRoutine;
        private bool exploderSubscribed;
        private bool interactorMasksExpanded;
        private Material socketGhostMaterial;

        public event Action<bool> ImmediateSeparationStateChanged;

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

        private void Update()
        {
            if (!configured || exploder == null || !exploder.IsSeparated)
            {
                return;
            }

            RefreshAllSocketPresentation();
        }

        private void OnEnable()
        {
            EnsureConfigured();
            EnsureExploderSubscription();
        }

        private void OnDisable()
        {
            RemoveExploderSubscription();
        }

        private void OnValidate()
        {
            interactionLayerStartBit = Mathf.Clamp(interactionLayerStartBit, 0, 30);
            socketBoundsPadding = Mathf.Max(1f, socketBoundsPadding);
            socketRevealDistance = Mathf.Max(0.01f, socketRevealDistance);
            socketGhostHideDistance = Mathf.Clamp(socketGhostHideDistance, 0f, socketRevealDistance);
            socketGhostReleaseMargin = Mathf.Max(0f, socketGhostReleaseMargin);
            socketGhostScale = Mathf.Max(1f, socketGhostScale);

            if (reassemblyCurve == null || reassemblyCurve.length == 0)
            {
                reassemblyCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 2.2f, 2.2f),
                    new Keyframe(1f, 1f, 0f, 0f));
            }
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

            StopReassemblyRoutine();
            CancelWholeSelection();
            SetWholeInteractionActive(false);
            SetPartInteractionActive(false);
            ImmediateSeparationStateChanged?.Invoke(true);
            exploder.Separate();
        }

        public void Assemble()
        {
            EnsureConfigured();

            if (exploder == null || !exploder.IsSeparated)
            {
                return;
            }

            StopReassemblyRoutine();
            ReleasePartSelections();
            SetWholeInteractionActive(false);
            SetPartInteractionActive(false);
            PreparePartsForReassembly();
            ImmediateSeparationStateChanged?.Invoke(false);
            reassemblyRoutine = StartCoroutine(AnimateReassemblyToOriginalPose());
        }

        public void HandleExploderSeparationChanged(bool separated)
        {
            EnsureConfigured();

            if (separated)
            {
                RefreshSocketPoses();
                SetWholeInteractionActive(false);
                SetPartInteractionActive(true);
                return;
            }

            StopReassemblyRoutine();
            FinalizeOriginalPose();
            SetPartInteractionActive(false);
            SetWholeInteractionActive(true);
        }

        public void ReassembleAllPartsToOriginalPose()
        {
            Assemble();
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
            EnsureExploderSubscription();
            EnsureSocketsRoot();

            if (autoConfigureParts)
            {
                BuildPartBindings();
            }

            EnsureInteractorMasks();

            configured = true;
        }

        private void EnsureExploderSubscription()
        {
            if (exploder == null || exploderSubscribed)
            {
                return;
            }

            exploder.SeparationChanged.AddListener(HandleExploderSeparationChanged);
            exploderSubscribed = true;
        }

        private void RemoveExploderSubscription()
        {
            if (exploder == null || !exploderSubscribed)
            {
                return;
            }

            exploder.SeparationChanged.RemoveListener(HandleExploderSeparationChanged);
            exploderSubscribed = false;
        }

        private void EnsureInteractorMasks()
        {
            if (interactorMasksExpanded || partBindings.Count == 0)
            {
                return;
            }

            var partsMask = new InteractionLayerMask();
            partsMask.value = 1;

            foreach (var partBinding in partBindings)
            {
                if (partBinding?.grabInteractable == null)
                {
                    continue;
                }

                partsMask.value |= partBinding.grabInteractable.interactionLayers.value;
            }

            var interactors = FindObjectsByType<XRBaseInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var interactor in interactors)
            {
                if (interactor == null || interactor is XRSocketInteractor)
                {
                    continue;
                }

                interactor.interactionLayers = new InteractionLayerMask
                {
                    value = interactor.interactionLayers.value | partsMask.value
                };
            }

            interactorMasksExpanded = true;
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
            partGrab.selectEntered.RemoveListener(OnPartSelectEntered);
            partGrab.selectExited.RemoveListener(OnPartSelectExited);
            partGrab.selectEntered.AddListener(OnPartSelectEntered);
            partGrab.selectExited.AddListener(OnPartSelectExited);

            if (autoAddHighlights)
            {
                var highlight = partTransform.GetComponent<AnatomyInteractableHighlight>();

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
            socketInteractor.selectEntered.RemoveListener(OnSocketSelectEntered);
            socketInteractor.selectExited.RemoveListener(OnSocketSelectExited);
            socketInteractor.selectEntered.AddListener(OnSocketSelectEntered);
            socketInteractor.selectExited.AddListener(OnSocketSelectExited);
            var socketMatchFilter = ConfigureSocketMatchFilter(socketGameObject, socketInteractor, partGrab);

            var socketGhostRoot = EnsureSocketGhost(partTransform, socketTransform);
            if (socketGhostRoot != null)
            {
                socketGhostRoot.gameObject.SetActive(false);
            }

            return new PartBinding
            {
                anatomyPart = anatomyPart,
                originalParent = partTransform.parent,
                originalLocalPosition = partTransform.localPosition,
                originalLocalRotation = partTransform.localRotation,
                collider = partCollider,
                rigidbody = partRigidbody,
                grabInteractable = partGrab,
                grabAttach = grabAttach,
                socketTransform = socketTransform,
                socketCollider = socketCollider,
                socketInteractor = socketInteractor,
                socketMatchFilter = socketMatchFilter,
                socketAttach = socketAttach,
                socketGhostRoot = socketGhostRoot,
                localBoundsCenter = localBounds.center,
                localBoundsSize = localBounds.size,
            };
        }

        private static AnatomySocketMatchFilter ConfigureSocketMatchFilter(
            GameObject socketGameObject,
            XRSocketInteractor socketInteractor,
            XRGrabInteractable matchingInteractable)
        {
            var socketMatchFilter = socketGameObject.GetComponent<AnatomySocketMatchFilter>();

            if (socketMatchFilter == null)
            {
                socketMatchFilter = socketGameObject.AddComponent<AnatomySocketMatchFilter>();
            }

            socketMatchFilter.Configure(socketInteractor, matchingInteractable);

            if (socketInteractor != null)
            {
                socketInteractor.targetFilter = socketMatchFilter;
                socketInteractor.selectFilters.Remove(socketMatchFilter);
                socketInteractor.selectFilters.Add(socketMatchFilter);
            }

            return socketMatchFilter;
        }

        private void ApplyCurrentState()
        {
            if (exploder == null)
            {
                SetWholeInteractionActive(true);
                SetPartInteractionActive(false);
                return;
            }

            if (exploder.IsSeparated)
            {
                RefreshSocketPoses();
                SetWholeInteractionActive(false);
                SetPartInteractionActive(true);
                return;
            }

            FinalizeOriginalPose();
            SetPartInteractionActive(false);
            SetWholeInteractionActive(true);
        }

        private void SetWholeInteractionActive(bool active)
        {
            if (wholeSocketInteractor != null)
            {
                wholeSocketInteractor.enabled = false;
            }

            if (wholeGrabInteractable != null)
            {
                wholeGrabInteractable.enabled = active && wholeGrabInitialEnabled;
            }

            if (wholeCollider != null)
            {
                wholeCollider.enabled = active && wholeColliderInitialEnabled;
            }

            if (wholeRigidbody != null)
            {
                wholeRigidbody.isKinematic = !active || wholeRigidbodyInitialKinematic;
                wholeRigidbody.useGravity = active && wholeRigidbodyInitialUseGravity;
                wholeRigidbody.velocity = Vector3.zero;
                wholeRigidbody.angularVelocity = Vector3.zero;
            }
        }

        private void SetPartInteractionActive(bool active)
        {
            foreach (var partBinding in partBindings)
            {
                if (partBinding == null)
                {
                    continue;
                }

                if (partBinding.grabInteractable != null)
                {
                    partBinding.grabInteractable.enabled = active;
                }

                if (partBinding.collider != null)
                {
                    partBinding.collider.enabled = active;
                }

                if (partBinding.rigidbody != null)
                {
                    partBinding.rigidbody.isKinematic = true;
                    partBinding.rigidbody.useGravity = false;
                    partBinding.rigidbody.velocity = Vector3.zero;
                    partBinding.rigidbody.angularVelocity = Vector3.zero;
                }

                if (partBinding.socketInteractor != null)
                {
                    partBinding.socketInteractor.enabled = active;
                    partBinding.socketInteractor.socketActive = false;
                }
                UpdatePartSocketPresentation(partBinding);
            }
        }

        private void CancelWholeSelection()
        {
            if (interactionManager == null || wholeGrabInteractable == null || !wholeGrabInteractable.isSelected)
            {
                return;
            }

            interactionManager.CancelInteractableSelection((IXRSelectInteractable)wholeGrabInteractable);
        }

        private void ReleasePartSelections()
        {
            if (interactionManager == null)
            {
                return;
            }

            foreach (var partBinding in partBindings)
            {
                if (partBinding == null)
                {
                    continue;
                }

                if (partBinding.grabInteractable != null && partBinding.grabInteractable.isSelected)
                {
                    interactionManager.CancelInteractableSelection((IXRSelectInteractable)partBinding.grabInteractable);
                }

                if (partBinding.socketInteractor != null && partBinding.socketInteractor.hasSelection)
                {
                    interactionManager.CancelInteractorSelection((IXRSelectInteractor)partBinding.socketInteractor);
                }
            }
        }

        private void PreparePartsForReassembly()
        {
            foreach (var partBinding in partBindings)
            {
                if (partBinding == null || partBinding.anatomyPart == null)
                {
                    continue;
                }

                var partTransform = partBinding.anatomyPart.transform;

                if (partBinding.rigidbody != null)
                {
                    partBinding.rigidbody.isKinematic = true;
                    partBinding.rigidbody.useGravity = false;
                    partBinding.rigidbody.velocity = Vector3.zero;
                    partBinding.rigidbody.angularVelocity = Vector3.zero;
                }
            }
        }

        private IEnumerator AnimateReassemblyToOriginalPose()
        {
            var duration = exploder != null ? Mathf.Max(0.01f, exploder.AnimationDuration) : 0.5f;
            var elapsed = 0f;
            var startPositions = new Vector3[partBindings.Count];
            var startRotations = new Quaternion[partBindings.Count];
            var targetPositions = new Vector3[partBindings.Count];
            var targetRotations = new Quaternion[partBindings.Count];

            for (var i = 0; i < partBindings.Count; i++)
            {
                if (partBindings[i] != null && partBindings[i].anatomyPart != null)
                {
                    var partTransform = partBindings[i].anatomyPart.transform;
                    startPositions[i] = partTransform.position;
                    startRotations[i] = partTransform.rotation;

                    var parentTransform = partBindings[i].originalParent != null ? partBindings[i].originalParent : transform;
                    targetPositions[i] = parentTransform.TransformPoint(partBindings[i].originalLocalPosition);
                    targetRotations[i] = parentTransform.rotation * partBindings[i].originalLocalRotation;
                }
                else
                {
                    startPositions[i] = Vector3.zero;
                    startRotations[i] = Quaternion.identity;
                    targetPositions[i] = Vector3.zero;
                    targetRotations[i] = Quaternion.identity;
                }
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = reassemblyCurve.Evaluate(Mathf.Clamp01(elapsed / duration));

                for (var i = 0; i < partBindings.Count; i++)
                {
                    var partBinding = partBindings[i];
                    if (partBinding == null || partBinding.anatomyPart == null)
                    {
                        continue;
                    }

                    partBinding.anatomyPart.transform.position =
                        Vector3.LerpUnclamped(startPositions[i], targetPositions[i], t);
                    partBinding.anatomyPart.transform.rotation =
                        Quaternion.SlerpUnclamped(startRotations[i], targetRotations[i], t);
                }

                yield return null;
            }

            FinalizeOriginalPose();
            if (exploder != null)
            {
                exploder.SetSeparatedImmediate(false);
            }

            reassemblyRoutine = null;
        }

        private void FinalizeOriginalPose()
        {
            foreach (var partBinding in partBindings)
            {
                if (partBinding == null || partBinding.anatomyPart == null)
                {
                    continue;
                }

                var partTransform = partBinding.anatomyPart.transform;

                if (partBinding.originalParent != null && partTransform.parent != partBinding.originalParent)
                {
                    partTransform.SetParent(partBinding.originalParent, true);
                }

                partTransform.localPosition = partBinding.originalLocalPosition;
                partTransform.localRotation = partBinding.originalLocalRotation;
            }
        }

        private void StopReassemblyRoutine()
        {
            if (reassemblyRoutine == null)
            {
                return;
            }

            StopCoroutine(reassemblyRoutine);
            reassemblyRoutine = null;
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

                if (partBinding.socketInteractor != null)
                {
                    partBinding.socketInteractor.socketActive = false;
                }

                partBinding.hasLeftSocketZone = false;

                UpdatePartSocketPresentation(partBinding);
            }
        }

        private void OnPartSelectEntered(SelectEnterEventArgs args)
        {
            var partBinding = FindBinding(args.interactableObject);
            if (partBinding != null)
            {
                partBinding.hasBeenGrabbed = true;
            }

            if (partBinding?.socketInteractor != null)
            {
                partBinding.socketInteractor.socketActive = false;
            }

            UpdatePartSocketPresentation(partBinding);
        }

        private void OnPartSelectExited(SelectExitEventArgs args)
        {
            if (exploder == null || !exploder.IsSeparated)
            {
                return;
            }

            var partBinding = FindBinding(args.interactableObject);

            if (partBinding?.socketInteractor != null && partBinding.socketInteractor.enabled)
            {
                partBinding.socketInteractor.socketActive = CanAllowSocket(partBinding);
            }

            UpdatePartSocketPresentation(partBinding);
        }

        private void OnSocketSelectEntered(SelectEnterEventArgs args)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            UpdatePartSocketPresentation(FindBinding(args.interactorObject));
        }

        private void OnSocketSelectExited(SelectExitEventArgs args)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            UpdatePartSocketPresentation(FindBinding(args.interactorObject));
        }

        private PartBinding FindBinding(IXRSelectInteractable interactable)
        {
            if (interactable == null)
            {
                return null;
            }

            foreach (var partBinding in partBindings)
            {
                if (partBinding?.grabInteractable == interactable)
                {
                    return partBinding;
                }
            }

            return null;
        }

        private PartBinding FindBinding(IXRSelectInteractor interactor)
        {
            if (interactor == null)
            {
                return null;
            }

            foreach (var partBinding in partBindings)
            {
                if (partBinding?.socketInteractor == interactor)
                {
                    return partBinding;
                }
            }

            return null;
        }

        private bool IsPartNearAssembly(PartBinding partBinding)
        {
            if (partBinding?.anatomyPart == null || partBinding.socketAttach == null)
            {
                return false;
            }

            return GetDistanceToSocketTarget(partBinding) <= socketRevealDistance;
        }

        private static Vector3 GetPartWorldCenter(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return root.position;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.center;
        }

        private void RefreshAllSocketPresentation()
        {
            foreach (var partBinding in partBindings)
            {
                UpdatePartSocketPresentation(partBinding);
            }
        }

        private float GetDistanceToSocketTarget(PartBinding partBinding)
        {
            if (partBinding?.anatomyPart == null)
            {
                return float.MaxValue;
            }

            var targetPosition = partBinding.socketAttach != null
                ? partBinding.socketAttach.position
                : partBinding.socketTransform != null
                    ? partBinding.socketTransform.position
                    : partBinding.anatomyPart.transform.position;

            if (TryGetPartWorldBounds(partBinding.anatomyPart.transform, out var bounds))
            {
                var closestPoint = bounds.ClosestPoint(targetPosition);
                return Vector3.Distance(closestPoint, targetPosition);
            }

            return Vector3.Distance(partBinding.anatomyPart.transform.position, targetPosition);
        }

        private static bool TryGetPartWorldBounds(Transform root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private bool CanRevealSocket(PartBinding partBinding)
        {
            return
                showSocketGhosts &&
                exploder != null &&
                exploder.IsSeparated &&
                partBinding?.socketTransform != null &&
                partBinding.socketInteractor != null &&
                partBinding.socketInteractor.enabled &&
                partBinding.hasBeenGrabbed &&
                IsPartNearAssembly(partBinding);
        }

        private bool CanAllowSocket(PartBinding partBinding)
        {
            return
                exploder != null &&
                exploder.IsSeparated &&
                partBinding?.socketTransform != null &&
                partBinding.socketInteractor != null &&
                partBinding.socketInteractor.enabled &&
                partBinding.hasBeenGrabbed &&
                IsPartNearAssembly(partBinding);
        }

        private void UpdatePartSocketPresentation(PartBinding partBinding)
        {
            if (partBinding == null)
            {
                return;
            }

            var isSelected = partBinding.grabInteractable != null && partBinding.grabInteractable.isSelected;
            var distanceToSocket = GetDistanceToSocketTarget(partBinding);
            var keepSocketVisible = partBinding.socketInteractor != null && partBinding.socketInteractor.hasSelection;
            var canRevealNow = isSelected && CanRevealSocket(partBinding);
            var revealThreshold = partBinding.isGhostVisible
                ? socketRevealDistance + socketGhostReleaseMargin
                : socketRevealDistance;

            var shouldReveal =
                canRevealNow &&
                distanceToSocket <= revealThreshold &&
                !keepSocketVisible;
            var shouldAllowSocket = !isSelected && CanAllowSocket(partBinding);

            var socketVisible = keepSocketVisible || shouldReveal || shouldAllowSocket || !hideSocketsUntilSeparated;

            if (partBinding.socketTransform != null)
            {
                partBinding.socketTransform.gameObject.SetActive(socketVisible);
            }

            if (partBinding.socketCollider != null)
            {
                partBinding.socketCollider.enabled = socketVisible && exploder != null && exploder.IsSeparated;
            }

            if (partBinding.socketInteractor != null && !partBinding.socketInteractor.hasSelection)
            {
                partBinding.socketInteractor.socketActive = shouldAllowSocket;
            }

            if (partBinding.socketGhostRoot != null)
            {
                partBinding.socketGhostRoot.gameObject.SetActive(shouldReveal);
            }

            partBinding.isGhostVisible = shouldReveal;
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

        private Transform EnsureSocketGhost(Transform sourceRoot, Transform socketTransform)
        {
            if (!showSocketGhosts || sourceRoot == null || socketTransform == null)
            {
                return null;
            }

            var ghostRoot = EnsureChildTransform(socketTransform, "SocketGhost");
            ghostRoot.localPosition = Vector3.zero;
            ghostRoot.localRotation = Quaternion.identity;
            ghostRoot.localScale = Vector3.one * socketGhostScale;

            if (ghostRoot.childCount == 0)
            {
                foreach (var sourceRenderer in sourceRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var sourceTransform = sourceRenderer.transform;
                    var ghostTransform = EnsureGhostPath(sourceRoot, sourceTransform, ghostRoot);
                    ghostTransform.localPosition = sourceTransform.localPosition;
                    ghostTransform.localRotation = sourceTransform.localRotation;
                    ghostTransform.localScale = sourceTransform.localScale;

                    var sourceMeshFilter = sourceTransform.GetComponent<MeshFilter>();
                    if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    var ghostMeshFilter = ghostTransform.GetComponent<MeshFilter>();
                    if (ghostMeshFilter == null)
                    {
                        ghostMeshFilter = ghostTransform.gameObject.AddComponent<MeshFilter>();
                    }

                    ghostMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

                    var ghostRenderer = ghostTransform.GetComponent<MeshRenderer>();
                    if (ghostRenderer == null)
                    {
                        ghostRenderer = ghostTransform.gameObject.AddComponent<MeshRenderer>();
                    }

                    var sharedGhostMaterial = GetSocketGhostMaterial();
                    var sourceMaterialCount = Mathf.Max(1, sourceRenderer.sharedMaterials.Length);
                    var ghostMaterials = new Material[sourceMaterialCount];

                    for (var i = 0; i < sourceMaterialCount; i++)
                    {
                        ghostMaterials[i] = sharedGhostMaterial;
                    }

                    ghostRenderer.sharedMaterials = ghostMaterials;
                    ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    ghostRenderer.receiveShadows = false;
                    ghostRenderer.lightProbeUsage = LightProbeUsage.Off;
                    ghostRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }

            return ghostRoot;
        }

        private Transform EnsureGhostPath(Transform sourceRoot, Transform sourceTransform, Transform ghostRoot)
        {
            if (sourceTransform == sourceRoot)
            {
                return ghostRoot;
            }

            var hierarchy = new Stack<Transform>();
            var current = sourceTransform;

            while (current != null && current != sourceRoot)
            {
                hierarchy.Push(current);
                current = current.parent;
            }

            var ghostParent = ghostRoot;

            while (hierarchy.Count > 0)
            {
                var sourceNode = hierarchy.Pop();
                var ghostNode = ghostParent.Find(sourceNode.name);

                if (ghostNode == null)
                {
                    ghostNode = new GameObject(sourceNode.name).transform;
                    ghostNode.SetParent(ghostParent, false);
                }

                ghostNode.localPosition = sourceNode.localPosition;
                ghostNode.localRotation = sourceNode.localRotation;
                ghostNode.localScale = sourceNode.localScale;
                ghostParent = ghostNode;
            }

            return ghostParent;
        }

        private Material GetSocketGhostMaterial()
        {
            if (socketGhostMaterial != null)
            {
                return socketGhostMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");

            socketGhostMaterial = new Material(shader)
            {
                name = "SocketGhostRuntimeMaterial"
            };

            if (socketGhostMaterial.HasProperty("_BaseColor"))
            {
                socketGhostMaterial.SetColor("_BaseColor", socketGhostColor);
            }

            if (socketGhostMaterial.HasProperty("_Color"))
            {
                socketGhostMaterial.SetColor("_Color", socketGhostColor);
            }

            if (shader != null && shader.name == "Universal Render Pipeline/Unlit")
            {
                socketGhostMaterial.SetFloat("_Surface", 1f);
                socketGhostMaterial.SetFloat("_Blend", 0f);
                socketGhostMaterial.SetFloat("_AlphaClip", 0f);
                socketGhostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                socketGhostMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                socketGhostMaterial.SetFloat("_ZWrite", 0f);
                socketGhostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                socketGhostMaterial.renderQueue = (int)RenderQueue.Transparent;
            }

            return socketGhostMaterial;
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
            public Transform originalParent;
            public Vector3 originalLocalPosition;
            public Quaternion originalLocalRotation;
            public Collider collider;
            public Rigidbody rigidbody;
            public XRGrabInteractable grabInteractable;
            public Transform grabAttach;
            public Transform socketTransform;
            public Collider socketCollider;
            public XRSocketInteractor socketInteractor;
            public AnatomySocketMatchFilter socketMatchFilter;
            public Transform socketAttach;
            public Transform socketGhostRoot;
            public bool hasBeenGrabbed;
            public bool hasLeftSocketZone;
            public bool isGhostVisible;
            public Vector3 localBoundsCenter;
            public Vector3 localBoundsSize;
        }
    }
}
