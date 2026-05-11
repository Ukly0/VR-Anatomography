using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomyWholeSocketGhost : MonoBehaviour
    {
        private const string RuntimeGhostName = "SocketGhost";

        [SerializeField] private XRSocketInteractor socketInteractor;
        [SerializeField] private XRGrabInteractable matchingInteractable;
        [SerializeField] private Transform ghostRoot;
        [SerializeField] private float revealDistance = 0.15f;
        [SerializeField] private float hideDistance = 0.18f;
        [SerializeField] private float dockedHideDistance = 0.025f;
        [SerializeField] private bool requireInteractableSelected;
        [SerializeField] private Color ghostColor = new Color(0.45f, 0.48f, 0.52f, 0.35f);
        [SerializeField] private float ghostScale = 1.01f;

        private bool isVisible;
        private Material runtimeGhostMaterial;
        private Renderer[] ghostRenderers;

        public void Configure(
            XRSocketInteractor newSocketInteractor,
            XRGrabInteractable newMatchingInteractable,
            Transform newGhostRoot,
            float newRevealDistance)
        {
            socketInteractor = newSocketInteractor;
            matchingInteractable = newMatchingInteractable;
            ghostRoot = newGhostRoot;
            revealDistance = Mathf.Max(0.01f, newRevealDistance);
            hideDistance = Mathf.Max(revealDistance, revealDistance * 1.2f);
            dockedHideDistance = Mathf.Min(0.025f, revealDistance * 0.25f);
            requireInteractableSelected = false;
            CacheGhostRenderers();
            SetGhostVisible(false);
        }

        private void Awake()
        {
            EnsureGhostRoot();
            SetGhostVisible(false);
        }

        private void OnEnable()
        {
            EnsureGhostRoot();

            if (socketInteractor != null)
            {
                socketInteractor.selectEntered.AddListener(OnSocketSelectEntered);
                socketInteractor.selectExited.AddListener(OnSocketSelectExited);
            }

            SetGhostVisible(false);
        }

        private void OnDestroy()
        {
            if (runtimeGhostMaterial != null)
            {
                Destroy(runtimeGhostMaterial);
            }
        }

        private void OnDisable()
        {
            if (socketInteractor != null)
            {
                socketInteractor.selectEntered.RemoveListener(OnSocketSelectEntered);
                socketInteractor.selectExited.RemoveListener(OnSocketSelectExited);
            }

            SetGhostVisible(false);
        }

        private void Update()
        {
            if (!CanShowGhost())
            {
                SetGhostVisible(false);
                return;
            }

            var socketPosition = GetSocketAttachPosition();
            var attachDistance = Vector3.Distance(GetInteractableAttachPosition(), socketPosition);
            if (attachDistance <= dockedHideDistance)
            {
                SetGhostVisible(false);
                return;
            }

            var threshold = isVisible ? hideDistance : revealDistance;
            SetGhostVisible(GetDistanceToSocketTarget(socketPosition) <= threshold);
        }

        private void OnSocketSelectEntered(SelectEnterEventArgs args)
        {
            SetGhostVisible(false);
        }

        private void OnSocketSelectExited(SelectExitEventArgs args)
        {
            Update();
        }

        private bool CanShowGhost()
        {
            if (socketInteractor == null || matchingInteractable == null || ghostRoot == null)
            {
                return false;
            }

            if (requireInteractableSelected && !HasNonSocketSelection())
            {
                return false;
            }

            return true;
        }

        private bool HasNonSocketSelection()
        {
            foreach (var interactor in matchingInteractable.interactorsSelecting)
            {
                if (interactor is not XRSocketInteractor)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 GetInteractableAttachPosition()
        {
            return matchingInteractable.attachTransform != null
                ? matchingInteractable.attachTransform.position
                : matchingInteractable.transform.position;
        }

        private Vector3 GetSocketAttachPosition()
        {
            return socketInteractor.attachTransform != null
                ? socketInteractor.attachTransform.position
                : socketInteractor.transform.position;
        }

        private float GetDistanceToSocketTarget(Vector3 socketPosition)
        {
            var bestDistance = float.MaxValue;

            foreach (var collider in matchingInteractable.colliders)
            {
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                var closestPoint = collider.ClosestPoint(socketPosition);
                bestDistance = Mathf.Min(bestDistance, Vector3.Distance(closestPoint, socketPosition));
            }

            return bestDistance < float.MaxValue
                ? bestDistance
                : Vector3.Distance(GetInteractableAttachPosition(), socketPosition);
        }

        private void SetGhostVisible(bool visible)
        {
            isVisible = visible;

            if (ghostRoot == null)
            {
                return;
            }

            if (!ghostRoot.gameObject.activeSelf)
            {
                ghostRoot.gameObject.SetActive(true);
            }

            if (ghostRenderers == null || ghostRenderers.Length == 0)
            {
                CacheGhostRenderers();
            }

            foreach (var ghostRenderer in ghostRenderers)
            {
                if (ghostRenderer != null && ghostRenderer.enabled != visible)
                {
                    ghostRenderer.enabled = visible;
                }
            }
        }

        private void EnsureGhostRoot()
        {
            if (matchingInteractable == null)
            {
                return;
            }

            var sourceRoot = matchingInteractable.transform;
            var parent = socketInteractor != null ? socketInteractor.transform : transform;
            if (ghostRoot == null)
            {
                ghostRoot = parent.Find(RuntimeGhostName);
            }

            if (ghostRoot == null)
            {
                var ghostObject = new GameObject(RuntimeGhostName);
                ghostRoot = ghostObject.transform;
                ghostRoot.SetParent(parent, false);
                ghostRoot.localPosition = Vector3.zero;
                ghostRoot.localRotation = Quaternion.identity;
                ghostRoot.localScale = Vector3.one * ghostScale;
            }

            ClearGhostChildren(ghostRoot);

            var attachTransform = matchingInteractable.attachTransform;
            ghostRoot.localPosition = attachTransform != null ? -attachTransform.localPosition : Vector3.zero;
            ghostRoot.localRotation = attachTransform != null ? Quaternion.Inverse(attachTransform.localRotation) : Quaternion.identity;
            ghostRoot.localScale = Vector3.one * ghostScale;

            var ghostMaterial = GetOrCreateRuntimeGhostMaterial();
            for (var i = 0; i < sourceRoot.childCount; i++)
            {
                CloneGhostHierarchy(sourceRoot.GetChild(i), ghostRoot, ghostMaterial);
            }

            CacheGhostRenderers();
            SetGhostVisible(false);
        }

        private void CacheGhostRenderers()
        {
            ghostRenderers = ghostRoot != null
                ? ghostRoot.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
        }

        private static void ClearGhostChildren(Transform ghostRoot)
        {
            if (ghostRoot == null)
            {
                return;
            }

            for (var i = ghostRoot.childCount - 1; i >= 0; i--)
            {
                var child = ghostRoot.GetChild(i);
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static Transform CloneGhostHierarchy(Transform sourceTransform, Transform ghostParent, Material ghostMaterial)
        {
            if (sourceTransform == null || ghostParent == null)
            {
                return null;
            }

            var ghostNodeObject = new GameObject(sourceTransform.name);
            var ghostNode = ghostNodeObject.transform;
            ghostNode.SetParent(ghostParent, false);
            ghostNode.localPosition = sourceTransform.localPosition;
            ghostNode.localRotation = sourceTransform.localRotation;
            ghostNode.localScale = sourceTransform.localScale;

            var sourceMeshFilter = sourceTransform.GetComponent<MeshFilter>();
            var sourceRenderer = sourceTransform.GetComponent<MeshRenderer>();
            if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null && sourceRenderer != null)
            {
                var ghostMeshFilter = ghostNode.gameObject.AddComponent<MeshFilter>();
                ghostMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

                var ghostRenderer = ghostNode.gameObject.AddComponent<MeshRenderer>();
                var sourceMaterialCount = Mathf.Max(1, sourceRenderer.sharedMaterials.Length);
                var ghostMaterials = new Material[sourceMaterialCount];
                for (var i = 0; i < ghostMaterials.Length; i++)
                {
                    ghostMaterials[i] = ghostMaterial;
                }

                ghostRenderer.enabled = sourceRenderer.enabled;
                ghostRenderer.sharedMaterials = ghostMaterials;
                ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ghostRenderer.receiveShadows = false;
                ghostRenderer.lightProbeUsage = LightProbeUsage.Off;
                ghostRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            for (var i = 0; i < sourceTransform.childCount; i++)
            {
                CloneGhostHierarchy(sourceTransform.GetChild(i), ghostNode, ghostMaterial);
            }

            return ghostNode;
        }

        private Material GetOrCreateRuntimeGhostMaterial()
        {
            if (runtimeGhostMaterial != null)
            {
                return runtimeGhostMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");

            runtimeGhostMaterial = new Material(shader)
            {
                name = "SocketGhostRuntimeMaterial"
            };

            ConfigureRuntimeGhostMaterial(runtimeGhostMaterial, shader, ghostColor);
            return runtimeGhostMaterial;
        }

        private static void ConfigureRuntimeGhostMaterial(Material material, Shader shader, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.SetOverrideTag("RenderType", "Transparent");

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (shader != null && shader.name == "Universal Render Pipeline/Unlit")
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_AlphaClip", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (shader != null && shader.name == "Standard")
            {
                material.SetFloat("_Mode", 3f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
        }
    }
}
