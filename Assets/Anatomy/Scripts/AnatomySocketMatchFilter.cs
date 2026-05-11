using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomySocketMatchFilter : XRBaseTargetFilter, IXRSelectFilter
    {
        [SerializeField] private XRSocketInteractor socketInteractor;
        [SerializeField] private XRGrabInteractable matchingInteractable;

        public void Configure(XRSocketInteractor socket, XRGrabInteractable interactable)
        {
            socketInteractor = socket;
            matchingInteractable = interactable;
        }

        public override void Process(
            IXRInteractor interactor,
            List<IXRInteractable> targets,
            List<IXRInteractable> results)
        {
            results.Clear();

            if (socketInteractor != null && !ReferenceEquals(interactor, socketInteractor))
            {
                results.AddRange(targets);
                return;
            }

            foreach (var target in targets)
            {
                if (ReferenceEquals(target, matchingInteractable))
                {
                    results.Add(target);
                    return;
                }
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (socketInteractor != null && !ReferenceEquals(interactor, socketInteractor))
            {
                return true;
            }

            return matchingInteractable != null && ReferenceEquals(interactable, matchingInteractable);
        }
    }
}
