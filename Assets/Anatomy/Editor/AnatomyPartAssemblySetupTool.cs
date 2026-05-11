using System;
using System.Collections.Generic;
using System.IO;
using DemoMedicine.Anatomy;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace DemoMedicine.AnatomyEditor
{
    public static class AnatomyPartAssemblySetupTool
    {
        private const string SkeletalPartsFolder = "Assets/Anatomy/Prefabs/Parts/Skeletal/";
        private const string AnatomyRootName = "-- Anatomy --";
        private const string SocketsRootName = "-- Sockets --";
        private const string SocketGhostName = "SocketGhost";
        private const string SocketGhostMaterialPath = "Assets/Anatomy/Materials/MAT_SocketGhost.mat";
        private const int FirstWholeInteractionLayerBit = 2;
        private const int FallbackWholeInteractionLayerBit = 2;
        private const int SharedPartInteractionLayerBit = 8;
        private const float MillimetersToMeters = 0.001f;
        private const float BoundsPadding = 1.08f;
        private const float SocketBoundsPadding = 1.2f;
        private const float WholeSocketGhostScale = 1.01f;
        private static readonly Color SocketGhostColor = new Color(0.45f, 0.48f, 0.52f, 0.35f);
        private static readonly Vector2 DefaultButtonSize = new Vector2(79.6f, 30f);
        private static readonly Vector3 DefaultVisualRootLocalPosition = new Vector3(0f, -1.53f, 0.029999971f);
        private static readonly Vector3 DefaultVisualRootEuler = new Vector3(-90f, 180f, 0f);

        [MenuItem("Tools/Anatomy/Create Assembly From Selected Parts Folder")]
        private static void CreateAssemblyFromSelectedPartsFolder()
        {
            if (!TryGetSelectedPartsFolder(out var folderPath, out var prefabAssets))
            {
                Debug.LogWarning(
                    $"Select a folder under {SkeletalPartsFolder} that contains AnatomyPart prefabs before running this tool.");
                return;
            }

            var folderName = Path.GetFileName(folderPath);
            var objectName = ToSafeObjectName(folderName);
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Create {objectName} assembly");

            var anatomyRoot = FindAnatomyRoot();
            var assembly = CreateGameObject($"{objectName}Assambly", anatomyRoot);
            var grabHandle = CreateGameObject($"{objectName}GrabHandle", assembly.transform);
            var grabAttach = CreateGameObject("GrabAttach", grabHandle.transform);
            var visualRoot = CreateGameObject("VisualRoot", grabHandle.transform);
            visualRoot.transform.localPosition = DefaultVisualRootLocalPosition;
            visualRoot.transform.localRotation = Quaternion.Euler(DefaultVisualRootEuler);
            visualRoot.transform.localScale = Vector3.one * MillimetersToMeters;

            foreach (var prefabAsset in prefabAssets)
            {
                var partInstance = PrefabUtility.InstantiatePrefab(prefabAsset, visualRoot.transform) as GameObject;
                if (partInstance == null)
                {
                    UnityEngine.Object.DestroyImmediate(assembly);
                    Debug.LogError($"Could not instantiate part prefab: {AssetDatabase.GetAssetPath(prefabAsset)}");
                    return;
                }

                Undo.RegisterCreatedObjectUndo(partInstance, $"Create {prefabAsset.name} part instance");
            }

            if (!TryGetLocalRendererBounds(grabHandle.transform, visualRoot.transform, out var localBounds))
            {
                localBounds = new Bounds(Vector3.zero, Vector3.one * 0.05f);
            }

            grabAttach.transform.localPosition = localBounds.center;
            grabAttach.transform.localRotation = Quaternion.identity;

            var interactionMask = AllocateWholeAssemblyInteractionMask();
            var grabCollider = ConfigureGrabHandle(grabHandle, grabAttach.transform, visualRoot.transform, localBounds, interactionMask);
            var socketInteractor = ConfigureSocket(
                objectName,
                grabHandle.transform,
                grabAttach.transform,
                visualRoot.transform,
                localBounds,
                interactionMask,
                grabCollider);
            var canvas = ConfigureVisualSeparation(visualRoot, grabHandle, grabAttach.transform, grabCollider, folderPath);
            ConfigureWholeSocketDockedVisibility(socketInteractor, canvas.gameObject);
            ExpandSceneInteractorMasks(interactionMask);

            EditorSceneManager.MarkSceneDirty(assembly.scene);
            Selection.activeGameObject = assembly;
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log(
                $"Created '{assembly.name}' from {prefabAssets.Length} prefabs in '{folderPath}'. GrabAttach and socket were placed at the visible renderer bounds center. Whole assembly interaction layer bit: {GetSingleLayerBit(interactionMask)}.",
                assembly);
        }

        [MenuItem("Tools/Anatomy/Create Assembly From Selected Parts Folder", true)]
        private static bool ValidateCreateAssemblyFromSelectedPartsFolder()
        {
            return TryGetSelectedPartsFolder(out _, out _);
        }

        private static bool TryGetSelectedPartsFolder(out string folderPath, out GameObject[] prefabAssets)
        {
            folderPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            prefabAssets = Array.Empty<GameObject>();

            if (string.IsNullOrWhiteSpace(folderPath) ||
                !AssetDatabase.IsValidFolder(folderPath) ||
                !folderPath.StartsWith(SkeletalPartsFolder, StringComparison.Ordinal))
            {
                return false;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            var prefabPaths = new List<string>();

            foreach (var guid in guids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/'), folderPath, StringComparison.Ordinal))
                {
                    continue;
                }

                prefabPaths.Add(prefabPath);
            }

            prefabPaths.Sort(StringComparer.OrdinalIgnoreCase);

            var parts = new List<GameObject>();
            foreach (var prefabPath in prefabPaths)
            {
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset != null && prefabAsset.GetComponent<AnatomyPart>() != null)
                {
                    parts.Add(prefabAsset);
                }
            }

            prefabAssets = parts.ToArray();
            return prefabAssets.Length > 0;
        }

        private static GameObject CreateGameObject(string name, Transform parent)
        {
            var gameObject = new GameObject(parent == null ? GetUniqueRootName(name) : name);
            Undo.RegisterCreatedObjectUndo(gameObject, $"Create {name}");

            if (parent != null)
            {
                Undo.SetTransformParent(gameObject.transform, parent, $"Parent {name}");
                gameObject.transform.localPosition = Vector3.zero;
                gameObject.transform.localRotation = Quaternion.identity;
                gameObject.transform.localScale = Vector3.one;
            }

            return gameObject;
        }

        private static Canvas CreateCanvas(
            Transform visualRoot,
            AnatomyExploder exploder,
            AnatomyBoneSocketController socketController,
            Bounds visualBounds)
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");
            Undo.SetTransformParent(canvasObject.transform, visualRoot, "Parent Canvas");
            canvasObject.transform.localScale = Vector3.one;
            canvasObject.layer = LayerMask.NameToLayer("UI");

            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.zero;
            canvasRect.sizeDelta = new Vector2(200f, 100f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.localPosition = GetCanvasLocalPosition(visualBounds);
            canvasRect.localRotation = Quaternion.Euler(DefaultVisualRootEuler);
            canvasRect.localScale = Vector3.one;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            canvasScaler.referencePixelsPerUnit = 100f;
            canvasScaler.dynamicPixelsPerUnit = 1f;

            var uiPose = Undo.AddComponent<AnatomySeparationUIPose>(canvasObject);
            ConfigureUiPose(uiPose, exploder, socketController, canvasRect);

            Undo.AddComponent<GraphicRaycaster>(canvasObject);
            Undo.AddComponent<TrackedDeviceGraphicRaycaster>(canvasObject);
            CreateButton(canvasRect, uiPose, exploder, socketController);
            return canvas;
        }

        private static void CreateButton(
            RectTransform canvasRect,
            AnatomySeparationUIPose uiPose,
            AnatomyExploder exploder,
            AnatomyBoneSocketController socketController)
        {
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, "Create Button");
            Undo.SetTransformParent(buttonObject.transform, canvasRect, "Parent Button");
            buttonObject.layer = LayerMask.NameToLayer("UI");

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.localPosition = Vector3.zero;
            buttonRect.localRotation = Quaternion.identity;
            buttonRect.localScale = Vector3.one;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = DefaultButtonSize;
            buttonRect.pivot = new Vector2(0.5f, 0.5f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.78f, 0.81f, 0.87f, 0.65f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(labelObject, "Create Button Label");
            Undo.SetTransformParent(labelObject.transform, buttonRect, "Parent Button Label");
            labelObject.layer = LayerMask.NameToLayer("UI");

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.localPosition = Vector3.zero;
            labelRect.localRotation = Quaternion.identity;
            labelRect.localScale = Vector3.one;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            labelRect.pivot = new Vector2(0.5f, 0.5f);

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = 18f;
            label.color = new Color(0.08f, 0.1f, 0.12f, 1f);
            label.text = "Explore";

            var toggleLabel = Undo.AddComponent<AnatomySeparationToggleLabel>(labelObject);
            ConfigureToggleLabel(toggleLabel, exploder, label, "Explore", "Rebuild");

            UnityEventTools.AddPersistentListener(button.onClick, uiPose.MoveForPendingToggle);
            UnityEventTools.AddPersistentListener(button.onClick, toggleLabel.RefreshForPendingToggle);
            UnityEventTools.AddPersistentListener(button.onClick, socketController.ToggleSeparated);
        }

        private static BoxCollider ConfigureGrabHandle(
            GameObject grabHandle,
            Transform grabAttach,
            Transform visualRoot,
            Bounds localBounds,
            InteractionLayerMask interactionMask)
        {
            var collider = Undo.AddComponent<BoxCollider>(grabHandle);
            collider.isTrigger = false;
            collider.center = localBounds.center;
            collider.size = GetSafeSize(localBounds.size * BoundsPadding);

            var rigidbody = Undo.AddComponent<Rigidbody>(grabHandle);
            rigidbody.mass = 0.0000001f;
            rigidbody.drag = 8f;
            rigidbody.angularDrag = 8f;
            rigidbody.useGravity = false;
            rigidbody.isKinematic = false;

            var grabInteractable = Undo.AddComponent<XRGrabInteractable>(grabHandle);
            grabInteractable.interactionLayers = interactionMask;
            grabInteractable.movementType = XRBaseInteractable.MovementType.Kinematic;
            grabInteractable.attachTransform = grabAttach;
            grabInteractable.attachEaseInTime = 0f;
            grabInteractable.throwOnDetach = false;
            grabInteractable.useDynamicAttach = true;
            grabInteractable.matchAttachPosition = true;
            grabInteractable.matchAttachRotation = true;
            grabInteractable.snapToColliderVolume = true;
            grabInteractable.retainTransformParent = true;
            grabInteractable.colliders.Clear();
            grabInteractable.colliders.Add(collider);

            var highlight = Undo.AddComponent<AnatomyInteractableHighlight>(grabHandle);
            highlight.Configure(visualRoot);
            return collider;
        }

        private static XRSocketInteractor ConfigureSocket(
            string objectName,
            Transform grabHandle,
            Transform grabAttach,
            Transform visualRoot,
            Bounds localBounds,
            InteractionLayerMask interactionMask,
            Collider matchingCollider)
        {
            var socketRoot = EnsureSocketsRoot();
            var socketObject = CreateGameObject($"Socket_{objectName}", socketRoot);
            var socketAttach = CreateGameObject("SocketAttach", socketObject.transform);
            socketObject.transform.position = grabAttach.position;
            socketObject.transform.rotation = grabHandle.rotation;
            socketObject.transform.localScale = Vector3.one;
            socketAttach.transform.localPosition = Vector3.zero;
            socketAttach.transform.localRotation = Quaternion.identity;

            var socketCollider = Undo.AddComponent<BoxCollider>(socketObject);
            socketCollider.isTrigger = true;
            socketCollider.center = Vector3.zero;
            socketCollider.size = GetSafeSize(localBounds.size * SocketBoundsPadding);

            var socketInteractor = Undo.AddComponent<XRSocketInteractor>(socketObject);
            socketInteractor.interactionLayers = interactionMask;
            socketInteractor.attachTransform = socketAttach.transform;
            socketInteractor.showInteractableHoverMeshes = false;
            socketInteractor.hoverSocketSnapping = true;
            socketInteractor.socketSnappingRadius = Mathf.Max(localBounds.extents.magnitude * 0.5f, 0.025f);
            socketInteractor.keepSelectedTargetValid = true;

            var matchingInteractable = matchingCollider.GetComponent<XRGrabInteractable>();
            socketInteractor.startingSelectedInteractable = matchingInteractable;
            var socketMatchFilter = Undo.AddComponent<AnatomySocketMatchFilter>(socketObject);
            socketMatchFilter.Configure(socketInteractor, matchingInteractable);
            socketInteractor.startingTargetFilter = socketMatchFilter;
            socketInteractor.startingSelectFilters.Remove(socketMatchFilter);
            socketInteractor.startingSelectFilters.Add(socketMatchFilter);

            var grabMatchFilter = Undo.AddComponent<AnatomySocketMatchFilter>(matchingInteractable.gameObject);
            grabMatchFilter.Configure(socketInteractor, matchingInteractable, true);
            matchingInteractable.startingSelectFilters.Remove(grabMatchFilter);
            matchingInteractable.startingSelectFilters.Add(grabMatchFilter);

            ConfigureWholeSocketGhost(
                socketObject,
                socketInteractor,
                matchingInteractable,
                visualRoot,
                grabAttach,
                Mathf.Max(socketInteractor.socketSnappingRadius, 0.25f));
            return socketInteractor;
        }

        private static Canvas ConfigureVisualSeparation(
            GameObject visualRoot,
            GameObject grabHandle,
            Transform grabAttach,
            Collider wholeCollider,
            string folderPath)
        {
            var exploder = Undo.AddComponent<AnatomyExploder>(visualRoot);
            var socketController = Undo.AddComponent<AnatomyBoneSocketController>(visualRoot);
            var grabInteractable = grabHandle.GetComponent<XRGrabInteractable>();
            var rigidbody = grabHandle.GetComponent<Rigidbody>();

            ConfigureExploder(exploder, grabAttach, folderPath);
            ConfigureSocketController(socketController, exploder, grabInteractable, rigidbody, wholeCollider);
            UnityEventTools.AddPersistentListener(exploder.SeparationChanged, socketController.HandleExploderSeparationChanged);

            if (TryGetLocalRendererBounds(visualRoot.transform, visualRoot.transform, out var visualBounds))
            {
                return CreateCanvas(visualRoot.transform, exploder, socketController, visualBounds);
            }
            else
            {
                return CreateCanvas(visualRoot.transform, exploder, socketController, new Bounds(Vector3.zero, Vector3.one * 100f));
            }
        }

        private static void ConfigureExploder(AnatomyExploder exploder, Transform customCenter, string folderPath)
        {
            var serializedObject = new SerializedObject(exploder);
            serializedObject.FindProperty("customCenter").objectReferenceValue = customCenter;
            serializedObject.FindProperty("centerMode").enumValueIndex = 0;
            serializedObject.FindProperty("includeInactiveParts").boolValue = false;
            serializedObject.FindProperty("separationDistance").floatValue = IsVertebraeFolder(folderPath) ? 0.02f : 0.06f;
            serializedObject.FindProperty("animationDuration").floatValue = 0.5f;
            serializedObject.FindProperty("enableCollidersOnlyWhenSeparated").boolValue = false;

            if (IsVertebraeFolder(folderPath))
            {
                serializedObject.FindProperty("separationMode").enumValueIndex = 1;
                serializedObject.FindProperty("localSeparationDirection").vector3Value = Vector3.forward;
            }
            else
            {
                serializedObject.FindProperty("separationMode").enumValueIndex = 0;
                serializedObject.FindProperty("localSeparationDirection").vector3Value = Vector3.right;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSocketController(
            AnatomyBoneSocketController socketController,
            AnatomyExploder exploder,
            XRGrabInteractable grabInteractable,
            Rigidbody rigidbody,
            Collider wholeCollider)
        {
            var serializedObject = new SerializedObject(socketController);
            serializedObject.FindProperty("exploder").objectReferenceValue = exploder;
            serializedObject.FindProperty("wholeGrabInteractable").objectReferenceValue = grabInteractable;
            serializedObject.FindProperty("wholeRigidbody").objectReferenceValue = rigidbody;
            serializedObject.FindProperty("wholeCollider").objectReferenceValue = wholeCollider;
            serializedObject.FindProperty("autoConfigureParts").boolValue = true;
            serializedObject.FindProperty("autoAddHighlights").boolValue = true;
            serializedObject.FindProperty("interactionLayerMode").enumValueIndex = 0;
            serializedObject.FindProperty("sharedInteractionLayerBit").intValue = SharedPartInteractionLayerBit;
            serializedObject.FindProperty("hideSocketsUntilSeparated").boolValue = true;
            serializedObject.FindProperty("showSocketGhosts").boolValue = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureUiPose(
            AnatomySeparationUIPose uiPose,
            AnatomyExploder exploder,
            AnatomyBoneSocketController socketController,
            RectTransform target)
        {
            var serializedObject = new SerializedObject(uiPose);
            serializedObject.FindProperty("exploder").objectReferenceValue = exploder;
            serializedObject.FindProperty("socketController").objectReferenceValue = socketController;
            serializedObject.FindProperty("target").objectReferenceValue = target;
            serializedObject.FindProperty("separatedZOffset").floatValue = 50f;
            serializedObject.FindProperty("duration").floatValue = 0.25f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureToggleLabel(
            AnatomySeparationToggleLabel toggleLabel,
            AnatomyExploder exploder,
            TMP_Text label,
            string assembledText,
            string separatedText)
        {
            var serializedObject = new SerializedObject(toggleLabel);
            serializedObject.FindProperty("exploder").objectReferenceValue = exploder;
            serializedObject.FindProperty("label").objectReferenceValue = label;
            serializedObject.FindProperty("assembledText").stringValue = assembledText;
            serializedObject.FindProperty("separatedText").stringValue = separatedText;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform EnsureSocketsRoot()
        {
            var existingRoot = GameObject.Find(SocketsRootName);

            if (existingRoot != null)
            {
                return existingRoot.transform;
            }

            return CreateGameObject(SocketsRootName, FindAnatomyRoot()).transform;
        }

        private static Transform FindAnatomyRoot()
        {
            var anatomyRoot = GameObject.Find(AnatomyRootName);
            return anatomyRoot != null ? anatomyRoot.transform : null;
        }

        private static void ConfigureWholeSocketDockedVisibility(
            XRSocketInteractor socketInteractor,
            GameObject canvasObject)
        {
            if (socketInteractor == null)
            {
                return;
            }

            if (canvasObject != null)
            {
                UnityEventTools.AddBoolPersistentListener(socketInteractor.selectEntered, canvasObject.SetActive, false);
                UnityEventTools.AddBoolPersistentListener(socketInteractor.selectExited, canvasObject.SetActive, true);
            }
        }

        private static void ExpandSceneInteractorMasks(InteractionLayerMask interactionMask)
        {
            var interactors = UnityEngine.Object.FindObjectsByType<XRDirectInteractor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (var interactor in interactors)
            {
                if (interactor == null)
                {
                    continue;
                }

                interactor.interactionLayers = new InteractionLayerMask
                {
                    value = interactor.interactionLayers.value | interactionMask.value
                };
                EditorUtility.SetDirty(interactor);
            }
        }

        private static InteractionLayerMask AllocateWholeAssemblyInteractionMask()
        {
            var usedBits = CollectUsedInteractionLayerBits();

            usedBits.Add(0);
            usedBits.Add(SharedPartInteractionLayerBit);

            for (var bitIndex = FirstWholeInteractionLayerBit; bitIndex < 32; bitIndex++)
            {
                if (!usedBits.Contains(bitIndex))
                {
                    return CreateInteractionLayerMask(bitIndex);
                }
            }

            Debug.LogWarning(
                $"No free XR interaction layer bit was found for the whole assembly. " +
                $"Using bit {FallbackWholeInteractionLayerBit}; AnatomySocketMatchFilter will still restrict the socket to its generated GrabHandle.");
            return CreateInteractionLayerMask(FallbackWholeInteractionLayerBit);
        }

        private static HashSet<int> CollectUsedInteractionLayerBits()
        {
            var usedBits = new HashSet<int>();
            var interactables = UnityEngine.Object.FindObjectsByType<XRBaseInteractable>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (var interactable in interactables)
            {
                AddMaskBits(usedBits, interactable.interactionLayers);
            }

            var socketInteractors = UnityEngine.Object.FindObjectsByType<XRSocketInteractor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (var socketInteractor in socketInteractors)
            {
                AddMaskBits(usedBits, socketInteractor.interactionLayers);
            }

            return usedBits;
        }

        private static void AddMaskBits(HashSet<int> usedBits, InteractionLayerMask mask)
        {
            for (var bitIndex = 0; bitIndex < 32; bitIndex++)
            {
                if ((mask.value & (1 << bitIndex)) != 0)
                {
                    usedBits.Add(bitIndex);
                }
            }
        }

        private static int GetSingleLayerBit(InteractionLayerMask mask)
        {
            for (var bitIndex = 0; bitIndex < 32; bitIndex++)
            {
                if ((mask.value & (1 << bitIndex)) != 0)
                {
                    return bitIndex;
                }
            }

            return -1;
        }

        private static void ConfigureWholeSocketGhost(
            GameObject socketObject,
            XRSocketInteractor socketInteractor,
            XRGrabInteractable matchingInteractable,
            Transform visualRoot,
            Transform grabAttach,
            float revealDistance)
        {
            var socketGhostObject = CreateWholeSocketGhost(visualRoot, socketObject.transform, grabAttach);
            if (socketGhostObject == null)
            {
                return;
            }

            var socketGhost = Undo.AddComponent<AnatomyWholeSocketGhost>(socketObject);
            socketGhost.Configure(socketInteractor, matchingInteractable, socketGhostObject.transform, revealDistance);
        }

        private static GameObject CreateWholeSocketGhost(
            Transform visualRoot,
            Transform socketTransform,
            Transform grabAttach)
        {
            if (visualRoot == null || socketTransform == null || grabAttach == null)
            {
                return null;
            }

            var existingGhost = socketTransform.Find(SocketGhostName);
            var ghostRoot = existingGhost != null ? existingGhost.gameObject : CreateGameObject(SocketGhostName, socketTransform);
            ghostRoot.transform.localPosition = visualRoot.localPosition - grabAttach.localPosition;
            ghostRoot.transform.localRotation = visualRoot.localRotation;
            ghostRoot.transform.localScale = visualRoot.localScale * WholeSocketGhostScale;
            ClearGhostChildren(ghostRoot.transform);

            var ghostMaterial = GetOrCreateSocketGhostMaterial();
            for (var i = 0; i < visualRoot.childCount; i++)
            {
                CloneGhostHierarchy(visualRoot.GetChild(i), ghostRoot.transform, ghostMaterial);
            }

            ghostRoot.SetActive(false);
            return ghostRoot;
        }

        private static Transform CloneGhostHierarchy(
            Transform sourceTransform,
            Transform ghostParent,
            Material ghostMaterial)
        {
            if (sourceTransform == null || ghostParent == null)
            {
                return null;
            }

            var ghostNode = CreateGameObject(sourceTransform.name, ghostParent).transform;
            ghostNode.localPosition = sourceTransform.localPosition;
            ghostNode.localRotation = sourceTransform.localRotation;
            ghostNode.localScale = sourceTransform.localScale;

            var sourceMeshFilter = sourceTransform.GetComponent<MeshFilter>();
            var sourceRenderer = sourceTransform.GetComponent<MeshRenderer>();
            if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null && sourceRenderer != null)
            {
                var ghostMeshFilter = Undo.AddComponent<MeshFilter>(ghostNode.gameObject);
                ghostMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

                var ghostRenderer = Undo.AddComponent<MeshRenderer>(ghostNode.gameObject);
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

        private static void ClearGhostChildren(Transform ghostRoot)
        {
            if (ghostRoot == null)
            {
                return;
            }

            for (var i = ghostRoot.childCount - 1; i >= 0; i--)
            {
                var child = ghostRoot.GetChild(i);
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static Material GetOrCreateSocketGhostMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SocketGhostMaterialPath);

            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");

            material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(SocketGhostMaterialPath)
            };

            ConfigureSocketGhostMaterial(material, shader);

            var materialFolder = Path.GetDirectoryName(SocketGhostMaterialPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(materialFolder) && !AssetDatabase.IsValidFolder(materialFolder))
            {
                AssetDatabase.CreateFolder("Assets/Anatomy", "Materials");
            }

            AssetDatabase.CreateAsset(material, SocketGhostMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void ConfigureSocketGhostMaterial(Material material, Shader shader)
        {
            if (material == null)
            {
                return;
            }

            material.SetOverrideTag("RenderType", "Transparent");

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", SocketGhostColor);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", SocketGhostColor);
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

        private static bool TryGetLocalRendererBounds(Transform localRoot, Transform rendererRoot, out Bounds bounds)
        {
            var renderers = rendererRoot.GetComponentsInChildren<Renderer>(true);
            bounds = new Bounds(Vector3.zero, Vector3.zero);

            if (renderers.Length == 0)
            {
                return false;
            }

            var inverseMatrix = localRoot.worldToLocalMatrix;
            var initialized = false;

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
                    bounds = rendererBounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds.min);
                    bounds.Encapsulate(rendererBounds.max);
                }
            }

            return initialized;
        }

        private static Vector3 GetCanvasLocalPosition(Bounds visualBounds)
        {
            var forwardOffset = Mathf.Max(visualBounds.size.z * 0.08f, 80f);
            return new Vector3(
                visualBounds.center.x,
                visualBounds.center.y,
                visualBounds.max.z + forwardOffset);
        }

        private static InteractionLayerMask CreateInteractionLayerMask(int bitIndex)
        {
            var mask = new InteractionLayerMask();
            mask.value = 1 << Mathf.Clamp(bitIndex, 0, 31);
            return mask;
        }

        private static Vector3 GetSafeSize(Vector3 size)
        {
            return new Vector3(
                Mathf.Max(size.x, 0.01f),
                Mathf.Max(size.y, 0.01f),
                Mathf.Max(size.z, 0.01f));
        }

        private static bool IsVertebraeFolder(string folderPath)
        {
            return folderPath.IndexOf("vertebra", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ToSafeObjectName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "AnatomyPart"
                : value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        }

        private static string GetUniqueRootName(string baseName)
        {
            if (GameObject.Find(baseName) == null)
            {
                return baseName;
            }

            var index = 1;
            string candidate;

            do
            {
                candidate = $"{baseName} ({index})";
                index++;
            }
            while (GameObject.Find(candidate) != null);

            return candidate;
        }
    }
}
