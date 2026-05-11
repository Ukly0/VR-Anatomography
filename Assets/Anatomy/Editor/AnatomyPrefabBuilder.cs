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
        private const string SourceRibCageFolder = "Assets/External Assets/Medicine/BP13928_FMA3_0_partof_FMA7480_Rib_cage";
        private const string OutputRoot = "Assets/Anatomy";
        private const string PrefabRoot = OutputRoot + "/Prefabs";
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

        private static readonly Regex FolderNamePattern = new Regex(
            @"^BP\d+_.+_partof_FMA\d+_(.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [MenuItem("Tools/Anatomy/Build Skull Prefabs")]
        public static void BuildSkullPrefabs()
        {
            BuildAnatomyPrefabs(new AnatomyBuildConfig(SourceSkullFolder, "Skeletal", "Skull", "Skeletal"));
        }

        [MenuItem("Tools/Anatomy/Build Rib Cage Prefabs")]
        public static void BuildRibCagePrefabs()
        {
            BuildAnatomyPrefabs(new AnatomyBuildConfig(SourceRibCageFolder, "Skeletal", "Rib cage", "Skeletal"));
        }

        [MenuItem("Tools/Anatomy/Build Selected BodyParts3D Folder")]
        public static void BuildSelectedBodyPartsFolder()
        {
            var selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(selectedPath) || !AssetDatabase.IsValidFolder(selectedPath))
            {
                Debug.LogError("Select a BodyParts3D source folder in the Project window before running this tool.");
                return;
            }

            if (!selectedPath.StartsWith("Assets/External Assets/Medicine/", StringComparison.Ordinal)
                && !string.Equals(selectedPath, "Assets/External Assets/Medicine", StringComparison.Ordinal))
            {
                Debug.LogError(
                    "Selected folder is not a BodyParts3D source folder. Select a folder under Assets/External Assets/Medicine, " +
                    "for example Assets/External Assets/Medicine/BP13928_FMA3_0_partof_FMA7480_Rib_cage.");
                return;
            }

            var region = RegionNameFromSourceFolder(selectedPath);
            BuildAnatomyPrefabs(new AnatomyBuildConfig(selectedPath, "Skeletal", region, "Skeletal"));
        }

        private static void BuildAnatomyPrefabs(AnatomyBuildConfig config)
        {
            if (!AssetDatabase.IsValidFolder(config.SourceFolder))
            {
                Debug.LogError($"Source folder not found: {config.SourceFolder}");
                return;
            }

            var partPrefabFolder = $"{PrefabRoot}/Parts/{config.BodySystem}/{config.Region}";
            EnsureFolders(partPrefabFolder);

            var boneMaterial = GetOrCreateBoneMaterial();
            var sourceFiles = Directory.GetFiles(Path.GetFullPath(config.SourceFolder), "*.obj", SearchOption.TopDirectoryOnly);
            Array.Sort(sourceFiles, StringComparer.OrdinalIgnoreCase);
            if (sourceFiles.Length == 0)
            {
                Debug.LogError(
                    $"No .obj files found in source folder: {config.SourceFolder}. " +
                    "Select the specific BodyParts3D folder that contains the .obj files, not an output prefab folder.");
                return;
            }

            var safeRegionName = ToSafeObjectName(config.Region);
            var anatomyRoot = new GameObject($"PF_AnatomyRoot_{safeRegionName}");
            anatomyRoot.transform.localScale = Vector3.one * MillimetersToMeters;

            var layer = new GameObject($"{config.LayerName}Layer");
            layer.transform.SetParent(anatomyRoot.transform, false);
            layer.AddComponent<AnatomyLayer>().Configure(config.LayerName, true);

            var regionGroup = new GameObject(config.Region);
            regionGroup.transform.SetParent(layer.transform, false);

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
                    var prefabPath = $"{partPrefabFolder}/{ToSafeAssetName(metadata.DisplayName)}.prefab";
                    var partRoot = CreatePartInstance(modelAsset, metadata, boneMaterial, config);

                    PrefabUtility.SaveAsPrefabAsset(partRoot, prefabPath);
                    UnityEngine.Object.DestroyImmediate(partRoot);

                    var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    var partInstance = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab);
                    partInstance.transform.SetParent(regionGroup.transform, false);

                    createdCount++;
                }

                PrefabUtility.SaveAsPrefabAsset(anatomyRoot, $"{LayerPrefabFolder}/PF_AnatomyRoot_{safeRegionName}.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anatomyRoot);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Built {createdCount} {config.Region} part prefabs and PF_AnatomyRoot_{safeRegionName}.prefab.");
        }

        private static GameObject CreatePartInstance(GameObject modelAsset, PartMetadata metadata, Material material, AnatomyBuildConfig config)
        {
            var partRoot = new GameObject($"PF_{ToSafeObjectName(metadata.DisplayName)}");
            partRoot.AddComponent<AnatomyPart>().Configure(
                metadata.DisplayName,
                metadata.FmaId,
                config.BodySystem,
                config.Region,
                config.LayerName,
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

        private static void EnsureFolders(string partPrefabFolder)
        {
            EnsureFolderPath(OutputRoot);
            EnsureFolderPath(MaterialFolder);
            EnsureFolderPath(PrefabRoot);
            EnsureFolderPath(PrefabRoot + "/Parts");
            EnsureFolderPath(partPrefabFolder);
            EnsureFolderPath(LayerPrefabFolder);
        }

        private static void EnsureFolderPath(string folderPath)
        {
            var normalizedPath = folderPath.Replace('\\', '/');
            var parts = normalizedPath.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
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

        private static string RegionNameFromSourceFolder(string sourceFolder)
        {
            var folderName = Path.GetFileName(sourceFolder.TrimEnd('/', '\\'));
            var match = FolderNamePattern.Match(folderName);
            var region = match.Success ? match.Groups[1].Value : folderName;

            return region.Replace('_', ' ');
        }

        private readonly struct AnatomyBuildConfig
        {
            public AnatomyBuildConfig(string sourceFolder, string bodySystem, string region, string layerName)
            {
                SourceFolder = sourceFolder;
                BodySystem = bodySystem;
                Region = region;
                LayerName = layerName;
            }

            public string SourceFolder { get; }
            public string BodySystem { get; }
            public string Region { get; }
            public string LayerName { get; }
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
