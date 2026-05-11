using TMPro;
using UnityEngine;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomySeparationToggleLabel : MonoBehaviour
    {
        [SerializeField] private AnatomyExploder exploder;
        [SerializeField] private TMP_Text label;
        [SerializeField] private string assembledText = "Explore Skull";
        [SerializeField] private string separatedText = "Rebuild Skull";

        private bool lastSeparatedState;
        private bool waitingForExploderSync;

        private void Awake()
        {
            ResolveReferences();
            RefreshLabel();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (exploder != null)
            {
                exploder.SeparationChanged.AddListener(OnSeparationChanged);
            }

            RefreshLabel();
        }

        private void OnDisable()
        {
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

            if (waitingForExploderSync)
            {
                if (exploder.IsSeparated == lastSeparatedState)
                {
                    waitingForExploderSync = false;
                }

                return;
            }

            if (exploder.IsSeparated == lastSeparatedState)
            {
                return;
            }

            RefreshLabel();
        }

        public void RefreshLabel()
        {
            if (label == null)
            {
                return;
            }

            var isSeparated = exploder != null && exploder.IsSeparated;
            label.text = isSeparated ? separatedText : assembledText;
            lastSeparatedState = isSeparated;
            waitingForExploderSync = false;
        }

        public void RefreshForPendingToggle()
        {
            if (label == null || exploder == null)
            {
                return;
            }

            var nextSeparatedState = !exploder.IsSeparated;
            label.text = nextSeparatedState ? separatedText : assembledText;
            lastSeparatedState = nextSeparatedState;
            waitingForExploderSync = true;
        }

        private void OnSeparationChanged(bool separated)
        {
            waitingForExploderSync = false;
            RefreshLabel();
        }

        private void ResolveReferences()
        {
            if (label == null)
            {
                label = GetComponent<TMP_Text>();
            }

            if (label == null)
            {
                label = GetComponentInChildren<TMP_Text>(true);
            }

            if (exploder == null)
            {
                exploder = GetComponentInParent<AnatomyExploder>();
            }
        }
    }
}
