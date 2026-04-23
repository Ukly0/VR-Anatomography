using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DemoMedicine.Anatomy;
using UnityEditor;
using UnityEngine;

namespace DemoMedicine.Anatomy.Editor
{
    public static class AnatomyPrefabBuilder
    {
        private const string SourceSkullFolder = "Assets/External Assets/Medicine/BP63256_FMA3_2_1_inference_partof_FMA46565_Skull";
        private const string OutputRoot = "Assets/Anatomy";
        private const string PrefabRoot = OutputRoot + "/Prefabs";
        private const string PartPrefabFolder = PrefabRoot + "/Parts/Skeletal/Skull";
        private const string LayerPrefabFolder = PrefabRoot + "/Layers";
        private const string MaterialFolder = OutputRoot + "/Materials";
        private const string BoneMaterialPath = MaterialFolder + "/MAT_Bone.mat";
        private const float MillimetersToMeters = 0.001f;

        private static readonly Regex FileNamePattern = new Regex(
            @"^(FJ\d+)_BP(\d+)_FMA(\d+)_(.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BoundsPattern = new Regex(
            @"Bounds\(mm\): \(([^,]+),([^,]+),([^)]+)\)-\(([^,]+),([^,]+),([^)]+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [MenuItem("Tools/Anatomy/Build Skull Prefabs")]
        public static void BuildSkullPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(SourceSkullFolder))
            {
                Debug.LogError($"Source folder not found: {SourceSkullFolder}");
                return;
            }

            EnsureFolders();

            var boneMaterial = GetOrCreateBoneMaterial();
            var sourceFiles = Directory.GetFiles(Path.GetFullPath(SourceSkullFolder), "*.obj", SearchOption.TopDirectoryOnly);
            Array.Sort(sourceFiles, StringComparer.OrdinalIgnoreCase);

            var anatomyRoot = new GameObject("AnatomyRoot");
            anatomyRoot.transform.localScale = Vector3.one * MillimetersToMeters;

            var skeletalLayer = new GameObject("SkeletalLayer");
            skeletalLayer.transform.SetParent(anatomyRoot.transform, false);
            skeletalLayer.AddComponent<AnatomyLayer>().Configure("Skeletal", true);

            var skullGroup = new GameObject("Skull");
            skullGroup.transform.SetParent(skeletalLayer.transform, false);

            var createdCount = 0;

            try
            {
                foreach (var sourceFile in sourceFiles)
                {
                    var assetPath = ToAssetPath(sourceFile);
                    var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                    if (modelAsset == null)
                    {
                        Debug.LogWarning($"Skipped model that Unity could not load: {assetPath}");
                        continue;
                    }

                    var metadata = ReadMetadata(sourceFile, assetPath);
                    var prefabPath = $"{PartPrefabFolder}/{ToSafeAssetName(metadata.DisplayName)}.prefab";
                    var partRoot = CreatePartInstance(modelAsset, metadata, boneMaterial);

                    PrefabUtility.SaveAsPrefabAsset(partRoot, prefabPath);
                    UnityEngine.Object.DestroyImmediate(partRoot);

                    var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    var partInstance = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab);
                    partInstance.transform.SetParent(skullGroup.transform, false);

                    createdCount++;
                }

                PrefabUtility.SaveAsPrefabAsset(anatomyRoot, $"{LayerPrefabFolder}/PF_AnatomyRoot_Skull.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anatomyRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {createdCount} skull part prefabs and PF_AnatomyRoot_Skull.prefab.");
        }

        private static GameObject CreatePartInstance(GameObject modelAsset, PartMetadata metadata, Material material)
        {
            var partRoot = new GameObject($"PF_{ToSafeObjectName(metadata.DisplayName)}");
            partRoot.AddComponent<AnatomyPart>().Configure(
                metadata.DisplayName,
                metadata.FmaId,
                "Skeletal",
                "Skull",
                "Skeletal",
                metadata.SourceFileId,
                metadata.RepresentationId,
                metadata.SourceAssetPath,
                metadata.BoundsMin,
                metadata.BoundsMax);

            var meshInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            meshInstance.name = $"{ToSafeObjectName(metadata.DisplayName)}_Mesh";
            meshInstance.transform.SetParent(partRoot.transform, false);

            foreach (var renderer in meshInstance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }

            return partRoot;
        }

        private static Material GetOrCreateBoneMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(BoneMaterialPath);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Diffuse");

            material = new Material(shader)
            {
                name = "MAT_Bone"
            };

            SetMaterialColor(material, new Color(0.86f, 0.80f, 0.68f, 1f));
            AssetDatabase.CreateAsset(material, BoneMaterialPath);
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private static PartMetadata ReadMetadata(string fullPath, string assetPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            var match = FileNamePattern.Match(fileName);

            var metadata = new PartMetadata
            {
                DisplayName = match.Success ? match.Groups[4].Value : fileName,
                SourceFileId = match.Success ? match.Groups[1].Value : string.Empty,
                RepresentationId = match.Success ? $"BP{match.Groups[2].Value}" : string.Empty,
                FmaId = match.Success ? $"FMA{match.Groups[3].Value}" : string.Empty,
                SourceAssetPath = assetPath,
                BoundsMin = Vector3.zero,
                BoundsMax = Vector3.zero
            };

            foreach (var line in File.ReadLines(fullPath))
            {
                var boundsMatch = BoundsPattern.Match(line);
                if (!boundsMatch.Success)
                {
                    continue;
                }

                metadata.BoundsMin = new Vector3(
                    ParseFloat(boundsMatch.Groups[1].Value),
                    ParseFloat(boundsMatch.Groups[2].Value),
                    ParseFloat(boundsMatch.Groups[3].Value));
                metadata.BoundsMax = new Vector3(
                    ParseFloat(boundsMatch.Groups[4].Value),
                    ParseFloat(boundsMatch.Groups[5].Value),
                    ParseFloat(boundsMatch.Groups[6].Value));
                break;
            }

            return metadata;
        }

        private static float ParseFloat(string value)
        {
            return float.Parse(value, CultureInfo.InvariantCulture);
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Anatomy");
            EnsureFolder(OutputRoot, "Materials");
            EnsureFolder(OutputRoot, "Prefabs");
            EnsureFolder(PrefabRoot, "Parts");
            EnsureFolder(PrefabRoot + "/Parts", "Skeletal");
            EnsureFolder(PrefabRoot + "/Parts/Skeletal", "Skull");
            EnsureFolder(PrefabRoot, "Layers");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static string ToAssetPath(string fullPath)
        {
            return fullPath.Replace('\\', '/').Substring(Application.dataPath.Length - "Assets".Length);
        }

        private static string ToSafeAssetName(string value)
        {
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return $"PF_{value}";
        }

        private static string ToSafeObjectName(string value)
        {
            return value.Replace(' ', '_');
        }

        private struct PartMetadata
        {
            public string DisplayName;
            public string FmaId;
            public string SourceFileId;
            public string RepresentationId;
            public string SourceAssetPath;
            public Vector3 BoundsMin;
            public Vector3 BoundsMax;
        }
    }
}
