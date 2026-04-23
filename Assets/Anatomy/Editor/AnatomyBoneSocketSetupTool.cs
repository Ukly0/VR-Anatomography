using DemoMedicine.Anatomy;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.Events;
using System.Reflection;

namespace DemoMedicine.AnatomyEditor
{
    public static class AnatomyBoneSocketSetupTool
    {
        private static readonly FieldInfo SeparationChangedField =
            typeof(AnatomyExploder).GetField("separationChanged", BindingFlags.NonPublic | BindingFlags.Instance);

        [MenuItem("Tools/Anatomy/Setup Selected Skull Bone Sockets")]
        private static void SetupSelectedSkull()
        {
            var selectedGameObject = Selection.activeGameObject;

            if (selectedGameObject == null)
            {
                Debug.LogWarning("Select the Skull object before running the setup tool.");
                return;
            }

            var exploder = ResolveExploder(selectedGameObject);

            if (exploder == null)
            {
                Debug.LogWarning("Could not find an AnatomyExploder on the selected object, its parents, or its children.");
                return;
            }

            var skullRoot = exploder.gameObject;

            if (selectedGameObject != skullRoot)
            {
                var misplacedController = selectedGameObject.GetComponent<AnatomyBoneSocketController>();
                if (misplacedController != null)
                {
                    Undo.DestroyObjectImmediate(misplacedController);
                }
            }

            Undo.RegisterFullObjectHierarchyUndo(skullRoot, "Setup Skull Bone Sockets");

            var controller = skullRoot.GetComponent<AnatomyBoneSocketController>();

            if (controller == null)
            {
                controller = Undo.AddComponent<AnatomyBoneSocketController>(skullRoot);
            }

            EditorUtility.SetDirty(skullRoot);

            WireExploderEvent(exploder, controller);
            EnsureWorldSpaceCanvasRaycasters();
            controller.RebuildConfiguration();

            EditorSceneManager.MarkSceneDirty(skullRoot.scene);
            Selection.activeGameObject = skullRoot;
            Debug.Log($"Skull bone socket setup completed on '{skullRoot.name}'.", skullRoot);
        }

        [MenuItem("Tools/Anatomy/Setup Selected Skull Bone Sockets", true)]
        private static bool ValidateSetupSelectedSkull()
        {
            return Selection.activeGameObject != null &&
                ResolveExploder(Selection.activeGameObject) != null;
        }

        private static AnatomyExploder ResolveExploder(GameObject selectedGameObject)
        {
            var exploder = selectedGameObject.GetComponent<AnatomyExploder>();

            if (exploder != null)
            {
                return exploder;
            }

            exploder = selectedGameObject.GetComponentInParent<AnatomyExploder>();

            if (exploder != null)
            {
                return exploder;
            }

            return selectedGameObject.GetComponentInChildren<AnatomyExploder>(true);
        }

        private static void WireExploderEvent(AnatomyExploder exploder, AnatomyBoneSocketController controller)
        {
            if (SeparationChangedField == null)
            {
                Debug.LogWarning("Could not find separationChanged on AnatomyExploder.");
                return;
            }

            var separationChanged = SeparationChangedField.GetValue(exploder) as UnityEvent<bool>;

            if (separationChanged == null)
            {
                Debug.LogWarning("Could not read separationChanged from AnatomyExploder.");
                return;
            }

            UnityAction<bool> callback = controller.HandleExploderSeparationChanged;
            UnityEventTools.RemovePersistentListener(separationChanged, callback);
            UnityEventTools.AddPersistentListener(separationChanged, callback);
            EditorUtility.SetDirty(exploder);
        }

        private static void EnsureWorldSpaceCanvasRaycasters()
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var canvas in canvases)
            {
                if (canvas.renderMode != RenderMode.WorldSpace)
                {
                    continue;
                }

                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    Undo.AddComponent<TrackedDeviceGraphicRaycaster>(canvas.gameObject);
                    EditorUtility.SetDirty(canvas.gameObject);
                }

                if (canvas.GetComponent<GraphicRaycaster>() == null)
                {
                    Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
                    EditorUtility.SetDirty(canvas.gameObject);
                }
            }
        }
    }
}
