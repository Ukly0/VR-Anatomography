using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomySocketMatchFilter : XRBaseTargetFilter, IXRSelectFilter
    {
        private enum HostRole
        {
            Socket,
            Interactable
        }

        [SerializeField] private XRSocketInteractor socketInteractor;
        [SerializeField] private XRGrabInteractable matchingInteractable;
        [SerializeField] private HostRole hostRole = HostRole.Socket;

        public void Configure(XRSocketInteractor socket, XRGrabInteractable interactable)
        {
            Configure(socket, interactable, HostRole.Socket);
        }

        public void Configure(XRSocketInteractor socket, XRGrabInteractable interactable, bool isInteractableHost)
        {
            Configure(socket, interactable, isInteractableHost ? HostRole.Interactable : HostRole.Socket);
        }

        private void Configure(XRSocketInteractor socket, XRGrabInteractable interactable, HostRole role)
        {
            socketInteractor = socket;
            matchingInteractable = interactable;
            hostRole = role;
            RegisterFilter();
        }

        private void OnEnable()
        {
            RegisterFilter();
        }

        private void OnDisable()
        {
            UnregisterRuntimeFilter();
        }

        private void OnValidate()
        {
            RegisterPersistentFilter();
        }

        private void RegisterFilter()
        {
            if (Application.isPlaying)
            {
                RegisterRuntimeFilter();
            }
            else
            {
                RegisterPersistentFilter();
            }
        }

        private void RegisterRuntimeFilter()
        {
            if (hostRole == HostRole.Socket)
            {
                if (socketInteractor == null)
                {
                    return;
                }

                socketInteractor.targetFilter = this;
                socketInteractor.selectFilters.Remove(this);
                socketInteractor.selectFilters.Add(this);
                return;
            }

            if (matchingInteractable == null)
            {
                return;
            }

            matchingInteractable.selectFilters.Remove(this);
            matchingInteractable.selectFilters.Add(this);
        }

        private void UnregisterRuntimeFilter()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (hostRole == HostRole.Socket)
            {
                if (socketInteractor != null && ReferenceEquals(socketInteractor.targetFilter, this))
                {
                    socketInteractor.targetFilter = null;
                }

                socketInteractor?.selectFilters.Remove(this);
                return;
            }

            matchingInteractable?.selectFilters.Remove(this);
        }

        private void RegisterPersistentFilter()
        {
            if (hostRole == HostRole.Socket)
            {
                if (socketInteractor == null)
                {
                    return;
                }

                socketInteractor.startingTargetFilter = this;
                socketInteractor.startingSelectFilters.Remove(this);
                socketInteractor.startingSelectFilters.Add(this);
                return;
            }

            if (matchingInteractable == null)
            {
                return;
            }

            matchingInteractable.startingSelectFilters.Remove(this);
            matchingInteractable.startingSelectFilters.Add(this);
        }

        public override void Process(
            IXRInteractor interactor,
            List<IXRInteractable> targets,
            List<IXRInteractable> results)
        {
            results.Clear();

            if (hostRole != HostRole.Socket)
            {
                results.AddRange(targets);
                return;
            }

            foreach (var target in targets)
            {
                if (ReferenceEquals(interactor, socketInteractor) && ReferenceEquals(target, matchingInteractable))
                {
                    results.Add(target);
                    return;
                }
            }
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (hostRole == HostRole.Socket)
            {
                return ReferenceEquals(interactor, socketInteractor) &&
                    ReferenceEquals(interactable, matchingInteractable);
            }

            if (interactor is not XRSocketInteractor)
            {
                return true;
            }

            return ReferenceEquals(interactor, socketInteractor) &&
                ReferenceEquals(interactable, matchingInteractable);
        }
    }
}
