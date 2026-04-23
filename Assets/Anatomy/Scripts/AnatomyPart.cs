using UnityEngine;

namespace DemoMedicine.Anatomy
{
    public sealed class AnatomyPart : MonoBehaviour
    {
        [SerializeField] private string displayName;
        [SerializeField] private string fmaId;
        [SerializeField] private string bodySystem;
        [SerializeField] private string region;
        [SerializeField] private string layerName;
        [SerializeField] private string sourceFileId;
        [SerializeField] private string representationId;
        [SerializeField] private string sourceAssetPath;
        [SerializeField] private Vector3 sourceBoundsMin;
        [SerializeField] private Vector3 sourceBoundsMax;

        public string DisplayName => displayName;
        public string FmaId => fmaId;
        public string BodySystem => bodySystem;
        public string Region => region;
        public string LayerName => layerName;
        public string SourceFileId => sourceFileId;
        public string RepresentationId => representationId;
        public string SourceAssetPath => sourceAssetPath;
        public Vector3 SourceBoundsMin => sourceBoundsMin;
        public Vector3 SourceBoundsMax => sourceBoundsMax;

        public void Configure(
            string newDisplayName,
            string newFmaId,
            string newBodySystem,
            string newRegion,
            string newLayerName,
            string newSourceFileId,
            string newRepresentationId,
            string newSourceAssetPath,
            Vector3 newSourceBoundsMin,
            Vector3 newSourceBoundsMax)
        {
            displayName = newDisplayName;
            fmaId = newFmaId;
            bodySystem = newBodySystem;
            region = newRegion;
            layerName = newLayerName;
            sourceFileId = newSourceFileId;
            representationId = newRepresentationId;
            sourceAssetPath = newSourceAssetPath;
            sourceBoundsMin = newSourceBoundsMin;
            sourceBoundsMax = newSourceBoundsMax;
        }
    }
}
