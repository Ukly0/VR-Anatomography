using UnityEngine;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomyLayer : MonoBehaviour
    {
        [SerializeField] private string layerName;
        [SerializeField] private bool defaultVisible = true;

        public string LayerName => layerName;
        public bool IsVisible => gameObject.activeSelf;

        private void Awake()
        {
            SetVisible(defaultVisible);
        }

        public void Configure(string newLayerName, bool visibleByDefault)
        {
            layerName = newLayerName;
            defaultVisible = visibleByDefault;
            SetVisible(defaultVisible);
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
