using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VLabOrchestrator.Showcase.Editor
{
    public static class ShowcaseSceneAuthoring
    {
        private const string ScenePath = "Assets/Scenes/Showcase.unity";
        private const string MaterialFolder = "Assets/VLabOrchestrator/Materials";
        private const string PrefabFolder = "Assets/VLabOrchestrator/Prefabs";
        private const string ShaderPath = "Assets/VLabOrchestrator/Resources/ShowcaseSurface.shader";
        private const string InstrumentPrefabPath = PrefabFolder + "/GenericSharedInstrument.prefab";
        private const string ParticipantPrefabPath = PrefabFolder + "/GenericResearcherAvatar.prefab";

        [InitializeOnLoadMethod]
        private static void CreateInitialEditableSceneIfMissing()
        {
            if (Application.isBatchMode)
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling ||
                    EditorApplication.isUpdating ||
                    EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    return;
                }

                bool instrumentExists =
                    AssetDatabase.LoadAssetAtPath<GameObject>(InstrumentPrefabPath) != null;
                bool participantExists =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ParticipantPrefabPath) != null;
                if (!instrumentExists || !participantExists)
                    RebuildEditableScene();
            };
        }

        [MenuItem("VLab Orchestrator/Rebuild Editable Showcase Scene")]
        public static void RebuildEditableScene()
        {
            EnsureFolder("Assets/VLabOrchestrator", "Materials");
            EnsureFolder("Assets/VLabOrchestrator", "Prefabs");
            EnsureFolder("Assets", "Scenes");

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new InvalidOperationException($"Showcase shader was not found at {ShaderPath}.");

            Material neutral = CreateOrUpdateMaterial("InstrumentGraphite", Hex("#253A42"), shader);
            Material participantA = CreateOrUpdateMaterial("ResearcherA", Hex("#28C2B8"), shader);
            Material participantB = CreateOrUpdateMaterial("ResearcherB", Hex("#FF8C52"), shader);
            Material accent = CreateOrUpdateMaterial("InstrumentAccent", Hex("#E8C36A"), shader);
            Material floor = CreateOrUpdateMaterial("EnvironmentFloor", Hex("#0D222A"), shader);
            Material ceramic = CreateOrUpdateMaterial("InstrumentCeramic", Hex("#CAD9D5"), shader);

            GameObject instrumentPrefab =
                CreateInstrumentPrefab(neutral, participantA, participantB, accent, ceramic);
            GameObject participantPrefab = CreateParticipantPrefab(ceramic);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureRenderSettings();
            CreateCameraAndLighting();

            CreatePrimitive("Floor", PrimitiveType.Cube, null, new Vector3(1.5f, -0.2f, 0.8f),
                new Vector3(12f, 0.25f, 8f), floor);
            CreatePrimitive("Rear Panel", PrimitiveType.Cube, null, new Vector3(1.5f, 2.6f, 4.35f),
                new Vector3(12f, 5.5f, 0.2f), floor);

            var instrument = (GameObject)PrefabUtility.InstantiatePrefab(instrumentPrefab);
            instrument.name = "Generic Shared Instrument";
            instrument.transform.position = new Vector3(1.7f, 0f, 0.8f);
            ShowcaseSharedInstrumentDefinition instrumentDefinition =
                instrument.GetComponent<ShowcaseSharedInstrumentDefinition>();

            ShowcaseParticipantDefinition researcherA = CreateParticipantInstance(
                participantPrefab,
                "Researcher A",
                "Researcher_A",
                new Vector3(-2.3f, 0f, 0.8f),
                participantA);
            ShowcaseParticipantDefinition researcherB = CreateParticipantInstance(
                participantPrefab,
                "Researcher B",
                "Researcher_B",
                new Vector3(5.7f, 0f, 0.8f),
                participantB);

            var runtimeHost = new GameObject("VLab Orchestrator Showcase");
            OrchestrationShowcase showcase = runtimeHost.AddComponent<OrchestrationShowcase>();
            showcase.ConfigureAuthoredScene(
                instrumentDefinition,
                researcherA,
                researcherB,
                neutral,
                participantA,
                participantB,
                accent);

            EditorUtility.SetDirty(instrumentDefinition);
            EditorUtility.SetDirty(researcherA);
            EditorUtility.SetDirty(researcherB);
            EditorUtility.SetDirty(showcase);
            EditorSceneManager.MarkSceneDirty(scene);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save authored showcase scene at {ScenePath}.");

            Selection.activeGameObject = runtimeHost;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[VLab Orchestrator] Editable showcase scene rebuilt at {ScenePath}");
        }

        private static GameObject CreateInstrumentPrefab(
            Material neutral,
            Material participantA,
            Material participantB,
            Material accent,
            Material ceramic)
        {
            var root = new GameObject("Generic Shared Instrument");

            CreatePrimitive("Base", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.2f, 0f),
                new Vector3(3.3f, 0.4f, 2.3f), neutral);
            CreatePrimitive("Foot", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.45f, 0f),
                new Vector3(1.15f, 0.18f, 1.15f), ceramic);
            CreatePrimitive("Column", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 1.3f, 0.5f),
                new Vector3(0.48f, 1.4f, 0.48f), neutral);
            CreatePrimitive("Bridge", PrimitiveType.Cube, root.transform, new Vector3(0f, 2.35f, 0.05f),
                new Vector3(2.15f, 0.28f, 0.5f), neutral);
            CreatePrimitive("Work Platform", PrimitiveType.Cube, root.transform, new Vector3(0f, 1.15f, -0.25f),
                new Vector3(2.3f, 0.16f, 1.65f), ceramic);

            GameObject turretObject = CreatePrimitive(
                "Controlled Turret",
                PrimitiveType.Cylinder,
                root.transform,
                new Vector3(0f, 2.05f, -0.25f),
                new Vector3(0.9f, 0.2f, 0.9f),
                accent);

            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                GameObject arm = CreatePrimitive(
                    $"Turret Arm {i + 1}",
                    PrimitiveType.Cube,
                    turretObject.transform,
                    Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, -0.45f, 0.72f),
                    new Vector3(0.16f, 0.75f, 0.16f),
                    i % 2 == 0 ? participantA : participantB);
                arm.transform.localRotation = Quaternion.Euler(18f, angle, 0f);
            }

            GameObject indicatorObject = CreatePrimitive(
                "Access Indicator",
                PrimitiveType.Sphere,
                root.transform,
                new Vector3(0f, 2.85f, 0.5f),
                Vector3.one * 0.28f,
                neutral);

            ShowcaseSharedInstrumentDefinition definition =
                root.AddComponent<ShowcaseSharedInstrumentDefinition>();
            definition.Configure(
                "generic_instrument_01",
                turretObject.transform,
                indicatorObject.GetComponent<Renderer>());

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, InstrumentPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject CreateParticipantPrefab(Material defaultMaterial)
        {
            var root = new GameObject("Generic Researcher Avatar");
            CreatePrimitive("Body", PrimitiveType.Capsule, root.transform, new Vector3(0f, 0.85f, 0f),
                new Vector3(0.55f, 0.8f, 0.55f), defaultMaterial);
            CreatePrimitive("Head", PrimitiveType.Sphere, root.transform, new Vector3(0f, 1.95f, 0f),
                Vector3.one * 0.65f, defaultMaterial);
            CreatePrimitive("Station Ring", PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.08f, 0f),
                new Vector3(0.85f, 0.08f, 0.85f), defaultMaterial);

            ShowcaseParticipantDefinition definition = root.AddComponent<ShowcaseParticipantDefinition>();
            definition.Configure("Researcher", "Researcher", root.GetComponentsInChildren<Renderer>(true));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ParticipantPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static ShowcaseParticipantDefinition CreateParticipantInstance(
            GameObject participantPrefab,
            string sceneName,
            string participantId,
            Vector3 position,
            Material material)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(participantPrefab);
            instance.name = sceneName;
            instance.transform.position = position;

            ShowcaseParticipantDefinition definition =
                instance.GetComponent<ShowcaseParticipantDefinition>();
            Renderer[] visuals = instance.GetComponentsInChildren<Renderer>(true);
            definition.Configure(participantId, sceneName, visuals);
            definition.ApplyMaterial(material);
            return definition;
        }

        private static void ConfigureRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#42606A");
            RenderSettings.ambientEquatorColor = Hex("#18333C");
            RenderSettings.ambientGroundColor = Hex("#071319");
            RenderSettings.ambientIntensity = 0.8f;
            RenderSettings.skybox = null;
        }

        private static void CreateCameraAndLighting()
        {
            var cameraObject = new GameObject("Showcase Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Hex("#07141B");
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(8.8f, 6.4f, -10.8f);
            cameraObject.transform.LookAt(new Vector3(1.5f, 1.4f, 0.8f));

            var keyLightObject = new GameObject("Key Light");
            var keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Hex("#FFF2D2");
            keyLight.intensity = 1.25f;
            keyLightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var rimLightObject = new GameObject("Rim Light");
            var rimLight = rimLightObject.AddComponent<Light>();
            rimLight.type = LightType.Point;
            rimLight.color = Hex("#23B9B1");
            rimLight.intensity = 7f;
            rimLight.range = 12f;
            rimLightObject.transform.position = new Vector3(4f, 4.8f, 2.5f);
        }

        private static GameObject CreatePrimitive(
            string objectName,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject created = GameObject.CreatePrimitive(primitiveType);
            created.name = objectName;
            created.transform.SetParent(parent, false);
            created.transform.localPosition = localPosition;
            created.transform.localScale = localScale;
            created.GetComponent<Renderer>().sharedMaterial = material;

            Collider collider = created.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);

            return created;
        }

        private static Material CreateOrUpdateMaterial(string assetName, Color color, Shader shader)
        {
            string path = $"{MaterialFolder}/{assetName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out Color color) ? color : Color.white;
        }
    }
}
