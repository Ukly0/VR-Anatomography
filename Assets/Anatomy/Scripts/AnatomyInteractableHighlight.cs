using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomyInteractableHighlight : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Color hoverColor = new Color(0.55f, 0.8f, 1f, 1f);
        [SerializeField] private Color selectedColor = new Color(1f, 0.75f, 0.35f, 1f);
        [SerializeField] private bool ignoreSocketSelection = true;

        private readonly List<RendererState> rendererStates = new List<RendererState>();
        private readonly HashSet<IXRHoverInteractor> hoverInteractors = new HashSet<IXRHoverInteractor>();
        private readonly HashSet<IXRSelectInteractor> selectInteractors = new HashSet<IXRSelectInteractor>();
        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interactable;
        private bool isHovered;
        private bool isSelected;

        public void Configure(Transform newVisualRoot)
        {
            visualRoot = newVisualRoot;
        }

        private void Awake()
        {
            if (!EnsureInitialized())
            {
                return;
            }
        }

        private void OnEnable()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
        }

        private void OnDisable()
        {
            if (interactable == null)
            {
                return;
            }

            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.selectExited.RemoveListener(OnSelectExited);

            hoverInteractors.Clear();
            selectInteractors.Clear();
            RefreshInteractionState();
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (ShouldIgnoreSocketInteractor(args.interactorObject))
            {
                return;
            }

            hoverInteractors.Add(args.interactorObject);
            RefreshInteractionState();
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (ShouldIgnoreSocketInteractor(args.interactorObject))
            {
                return;
            }

            hoverInteractors.Remove(args.interactorObject);
            RefreshInteractionState();
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (ShouldIgnoreSocketInteractor(args.interactorObject))
            {
                return;
            }

            selectInteractors.Add(args.interactorObject);
            RefreshInteractionState();
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (ShouldIgnoreSocketInteractor(args.interactorObject))
            {
                return;
            }

            selectInteractors.Remove(args.interactorObject);
            RefreshInteractionState();
        }

        private void RefreshInteractionState()
        {
            isHovered = hoverInteractors.Count > 0;
            isSelected = selectInteractors.Count > 0;
            RefreshVisuals();
        }

        private bool ShouldIgnoreSocketInteractor(IXRHoverInteractor interactorObject)
        {
            return ignoreSocketSelection && interactorObject is XRSocketInteractor;
        }

        private bool ShouldIgnoreSocketInteractor(IXRSelectInteractor interactorObject)
        {
            return ignoreSocketSelection && interactorObject is XRSocketInteractor;
        }

        private void RefreshVisuals()
        {
            var tintColor = isSelected ? selectedColor : isHovered ? hoverColor : Color.clear;

            foreach (var state in rendererStates)
            {
                var materials = state.Renderer.materials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var targetColor = tintColor == Color.clear
                        ? state.OriginalColors[i]
                        : Color.Lerp(state.OriginalColors[i], tintColor, isSelected ? 0.65f : 0.4f);

                    WriteColor(materials[i], targetColor);
                }
            }
        }

        private bool EnsureInitialized()
        {
            if (interactable != null && rendererStates.Count > 0)
            {
                return true;
            }

            interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
            if (interactable == null)
            {
                Debug.LogWarning($"{nameof(AnatomyInteractableHighlight)} needs an XRBaseInteractable on {name}.", this);
                enabled = false;
                return false;
            }

            rendererStates.Clear();

            var root = visualRoot != null ? visualRoot : transform;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.materials;
                var colors = new Color[materials.Length];

                for (var i = 0; i < materials.Length; i++)
                {
                    colors[i] = ReadColor(materials[i]);
                }

                rendererStates.Add(new RendererState(renderer, colors));
            }

            return true;
        }

        private static Color ReadColor(Material material)
        {
            if (material.HasProperty("_BaseColor"))
            {
                return material.GetColor("_BaseColor");
            }

            if (material.HasProperty("_Color"))
            {
                return material.color;
            }

            return Color.white;
        }

        private static void WriteColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = color;
            }
        }

        private readonly struct RendererState
        {
            public RendererState(Renderer renderer, Color[] originalColors)
            {
                Renderer = renderer;
                OriginalColors = originalColors;
            }

            public Renderer Renderer { get; }
            public Color[] OriginalColors { get; }
        }
    }
}
