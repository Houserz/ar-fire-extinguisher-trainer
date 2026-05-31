#if UNITY_EDITOR
using System.Collections.Generic;
using FireExtinguisherTrainer;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FireExtinguisherTrainerEditor
{
    public static class FireTrainerWeek1Setup
    {
        private const string RootFolder = "Assets/FireExtinguisherTrainer";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string PrefabFolder = RootFolder + "/Prefabs";
        private const string FirePrefabPath = PrefabFolder + "/TrainingFire.prefab";
        private const string ExtinguisherPrefabPath = PrefabFolder + "/TrainingExtinguisher.prefab";
        private const string ScenePath = "Assets/Scenes/FireTrainerWeek1.unity";

        [MenuItem("Tools/Fire Trainer/Setup Week 1 MVP Scene")]
        public static void SetupWeek1MvpScene()
        {
            EnsureFolders();

            Material fireMaterial = CreateMaterial("Fire_Orange", new Color(1f, 0.28f, 0.04f, 1f));
            Material emberMaterial = CreateMaterial("Fire_Ember", new Color(1f, 0.75f, 0.08f, 1f));
            Material baseMaterial = CreateMaterial("Target_Base_Cyan", new Color(0.05f, 0.9f, 1f, 0.45f));
            Material extinguisherMaterial = CreateMaterial("Extinguisher_Red", new Color(0.85f, 0.06f, 0.04f, 1f));
            Material metalMaterial = CreateMaterial("Extinguisher_Metal", new Color(0.72f, 0.72f, 0.68f, 1f));
            Material safetyPinMaterial = CreateMaterial("Extinguisher_SafetyPin", new Color(1f, 0.9f, 0.08f, 1f));

            GameObject firePrefab = CreateFirePrefab(fireMaterial, emberMaterial, baseMaterial);
            GameObject extinguisherPrefab = CreateExtinguisherPrefab(extinguisherMaterial, metalMaterial, safetyPinMaterial);

            Scene scene = OpenScene();
            RigReferences rig = CreateOrUpdateOvrCameraRig();
            Camera camera = rig.CenterEyeCamera != null ? rig.CenterEyeCamera : Camera.main;
            if (camera == null)
            {
                camera = CreatePreviewCamera();
            }

            GameObject root = FindOrCreate("FireTrainer_Week1");
            FireSpawner spawner = AddComponentIfMissing<FireSpawner>(root);
            FireTrainingManager manager = AddComponentIfMissing<FireTrainingManager>(root);

            Transform[] spawnPoints = CreateSpawnPoints(root.transform);
            SetObjectReference(spawner, "firePrefab", firePrefab.GetComponent<FireTarget>());
            SetObjectArray(spawner, "spawnPoints", spawnPoints);
            SetBool(spawner, "spawnOnStart", false);

            ExtinguisherController extinguisher = CreateOrUpdateExtinguisherInstance(extinguisherPrefab, rig.RightControllerAnchor, camera);
            TrainingHUD hud = CreateOrUpdateHud(rig.CenterEyeAnchor, camera);

            SetObjectReference(manager, "fireSpawner", spawner);
            SetObjectReference(manager, "extinguisher", extinguisher);
            SetObjectReference(manager, "hud", hud);
            SetObjectReference(manager, "playerCamera", camera);
            SetObjectReference(manager, "rayOriginOverride", rig.RightControllerAnchor != null ? rig.RightControllerAnchor : camera.transform);
            SetBool(manager, "preferOvrCameraRig", true);
            SetFloat(manager, "aimHoldSeconds", 0.45f);
            SetFloat(manager, "squeezeConfirmSeconds", 0.25f);
            SetFloat(manager, "requiredSweepDegrees", 18f);
            SetInt(manager, "totalExtinguishers", 2);
            SetBool(manager, "showIntroOnFirstStart", true);
            SetFloat(manager, "introMinimumSeconds", 2.5f);
            SetFloat(manager, "introAutoDismissSeconds", 18f);

            EnsureEventSystem();
            EnsureBuildScene(scene.path);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Fire Trainer Week 1 MVP scene setup complete.");
        }

        private static Scene OpenScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path == ScenePath)
            {
                return activeScene;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return EditorSceneManager.OpenScene(ScenePath);
            }

            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(newScene, ScenePath);
            return newScene;
        }

        private static void EnsureFolders()
        {
            EnsureFolder(RootFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
            string folder = System.IO.Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader =
                    Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateFirePrefab(Material fireMaterial, Material emberMaterial, Material baseMaterial)
        {
            GameObject root = new GameObject("TrainingFire");
            FireTarget fireTarget = root.AddComponent<FireTarget>();

            GameObject flameBody = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameBody.name = "Flame Placeholder";
            flameBody.transform.SetParent(root.transform, false);
            flameBody.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            flameBody.transform.localScale = new Vector3(0.55f, 0.85f, 0.55f);
            Renderer flameBodyRenderer = flameBody.GetComponent<Renderer>();
            flameBodyRenderer.sharedMaterial = fireMaterial;
            flameBodyRenderer.enabled = false;
            Object.DestroyImmediate(flameBody.GetComponent<Collider>());

            CreateFlameTongue(
                flameBody.transform,
                "Flame Core Tongue",
                new Vector3(0f, 0.12f, -0.02f),
                new Vector3(0.36f, 0.82f, 0.36f),
                fireMaterial);
            CreateFlameTongue(
                flameBody.transform,
                "Flame Left Tongue",
                new Vector3(-0.18f, -0.02f, 0.04f),
                new Vector3(0.24f, 0.62f, 0.24f),
                fireMaterial);
            CreateFlameTongue(
                flameBody.transform,
                "Flame Right Tongue",
                new Vector3(0.17f, 0f, 0.02f),
                new Vector3(0.22f, 0.58f, 0.22f),
                fireMaterial);

            GameObject ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ember.name = "Base Ember";
            ember.transform.SetParent(root.transform, false);
            ember.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            ember.transform.localScale = new Vector3(0.7f, 0.18f, 0.7f);
            ember.GetComponent<Renderer>().sharedMaterial = emberMaterial;
            Object.DestroyImmediate(ember.GetComponent<Collider>());

            GameObject baseTarget = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseTarget.name = "Base Target Zone";
            baseTarget.transform.SetParent(root.transform, false);
            baseTarget.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            baseTarget.transform.localScale = new Vector3(0.75f, 0.03f, 0.75f);
            baseTarget.GetComponent<Renderer>().sharedMaterial = baseMaterial;
            Object.DestroyImmediate(baseTarget.GetComponent<Collider>());

            ParticleSystem flameParticles = CreateParticleSystem(
                "Flame Particles",
                root.transform,
                new Vector3(0f, 0.15f, 0f),
                new Color(1f, 0.22f, 0.02f, 1f),
                24f,
                0.68f,
                0.5f,
                0.08f,
                0.28f,
                14f,
                0.16f,
                72);

            ParticleSystem smokeParticles = CreateParticleSystem(
                "Smoke Particles",
                root.transform,
                new Vector3(0f, 0.7f, 0f),
                new Color(0.22f, 0.22f, 0.22f, 0.38f),
                6f,
                1.1f,
                0.28f,
                0.16f,
                0.38f,
                10f,
                0.12f,
                36);

            GameObject lightObject = new GameObject("Fire Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            Light fireLight = lightObject.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.42f, 0.08f);
            fireLight.range = 2f;
            fireLight.intensity = 2.5f;

            SetObjectReference(fireTarget, "baseTarget", baseTarget.transform);
            SetObjectReference(fireTarget, "flameParticles", flameParticles);
            SetObjectReference(fireTarget, "smokeParticles", smokeParticles);
            SetObjectReference(fireTarget, "fireLight", fireLight);
            SetFloat(fireTarget, "maxHealth", 100f);
            SetFloat(fireTarget, "damagePerSecond", 34f);
            SetFloat(fireTarget, "baseRadius", 0.38f);
            SetFloat(fireTarget, "bodyRadius", 0.75f);
            SetBool(fireTarget, "useParticleEffects", true);
            SetBool(fireTarget, "useSmokeParticles", true);
            SetBool(fireTarget, "useBaseAimFeedback", true);
            SetBool(fireTarget, "lockWorldRotation", true);
            SetVector3(fireTarget, "flameFullScale", new Vector3(0.9f, 1.35f, 0.9f));
            SetVector3(fireTarget, "flameMinimumScale", new Vector3(0.34f, 0.45f, 0.34f));

            PrefabUtility.SaveAsPrefabAsset(root, FirePrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(FirePrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(FirePrefabPath);
        }

        private static GameObject CreateExtinguisherPrefab(Material bodyMaterial, Material metalMaterial, Material safetyPinMaterial)
        {
            GameObject root = new GameObject("TrainingExtinguisher");
            ExtinguisherController extinguisher = root.AddComponent<ExtinguisherController>();

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            body.transform.localScale = new Vector3(0.18f, 0.45f, 0.18f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            handle.transform.SetParent(root.transform, false);
            handle.transform.localPosition = new Vector3(0f, 0.98f, 0f);
            handle.transform.localScale = new Vector3(0.45f, 0.08f, 0.12f);
            handle.GetComponent<Renderer>().sharedMaterial = metalMaterial;

            CreateOrUpdateSafetyPinVisual(root.transform, safetyPinMaterial);

            GameObject nozzle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nozzle.name = "Nozzle";
            nozzle.transform.SetParent(root.transform, false);
            nozzle.transform.localPosition = new Vector3(0f, 0.92f, 0.28f);
            nozzle.transform.localScale = new Vector3(0.09f, 0.09f, 0.32f);
            nozzle.GetComponent<Renderer>().sharedMaterial = metalMaterial;

            CreateInteractionPoint(root.transform, ExtinguisherInteractionDriver.RightGripPoseName, new Vector3(0f, 0.92f, -0.06f));
            CreateInteractionPoint(root.transform, ExtinguisherInteractionDriver.LeftSupportPoseName, new Vector3(0f, 0.62f, 0.18f));
            CreateInteractionPoint(root.transform, ExtinguisherInteractionDriver.PinPullZoneName, new Vector3(0f, 1.02f, 0f));

            GameObject sprayObject = new GameObject("Spray Particles");
            sprayObject.transform.SetParent(nozzle.transform, false);
            sprayObject.transform.localPosition = new Vector3(0f, 0f, 0.18f);
            sprayObject.transform.localRotation = Quaternion.identity;
            ParticleSystem sprayParticles = sprayObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = sprayParticles.main;
            main.playOnAwake = false;
            main.startColor = new Color(0.82f, 0.96f, 1f, 0.56f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = sprayParticles.emission;
            emission.rateOverTime = 70f;
            emission.enabled = true;
            ParticleSystem.ShapeModule shape = sprayParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.025f;
            VrStableParticleVisuals.ConfigureMeshParticleRenderer(sprayParticles, "Sphere.fbx");
            sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            SetObjectReference(extinguisher, "nozzle", nozzle.transform);
            SetObjectReference(extinguisher, "sprayParticles", sprayParticles);
            SetFloat(extinguisher, "capacitySeconds", 8f);
            SetBool(extinguisher, "useVrStableSprayLine", true);
            SetBool(extinguisher, "showSprayGuideLine", false);
            SetFloat(extinguisher, "sprayVisualLength", 3f);
            SetFloat(extinguisher, "sprayStartWidth", 0.05f);
            SetFloat(extinguisher, "sprayEndWidth", 0.24f);
            AddComponentIfMissing<ExtinguisherCapacityGauge>(root).ForceRefresh();

            PrefabUtility.SaveAsPrefabAsset(root, ExtinguisherPrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(ExtinguisherPrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ExtinguisherPrefabPath);
        }

        private static Transform CreateInteractionPoint(Transform parent, string name, Vector3 localPosition)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = localPosition;
            point.localRotation = Quaternion.identity;
            point.localScale = Vector3.one;
            return point;
        }

        private static void CreateOrUpdateSafetyPinVisual(Transform root, Material material)
        {
            Transform pin = root.Find(ExtinguisherController.SafetyPinName);
            if (pin == null)
            {
                pin = new GameObject(ExtinguisherController.SafetyPinName).transform;
                pin.SetParent(root, false);
            }

            pin.localPosition = new Vector3(0f, 1.04f, 0f);
            pin.localRotation = Quaternion.identity;
            pin.localScale = Vector3.one;

            CreateSafetyPinSegment(
                pin,
                ExtinguisherController.SafetyPinShaftName,
                new Vector3(0.05f, 0f, 0f),
                Quaternion.identity,
                new Vector3(0.42f, 0.026f, 0.026f),
                material);

            Transform ring = pin.Find(ExtinguisherController.SafetyPinRingName);
            if (ring == null)
            {
                ring = new GameObject(ExtinguisherController.SafetyPinRingName).transform;
                ring.SetParent(pin, false);
            }

            ring.localPosition = new Vector3(-0.28f, 0f, 0f);
            ring.localRotation = Quaternion.identity;
            ring.localScale = Vector3.one;

            const int segmentCount = 12;
            const float radius = 0.085f;
            for (int i = 0; i < segmentCount; i++)
            {
                float angle = i * Mathf.PI * 2f / segmentCount;
                CreateSafetyPinSegment(
                    ring,
                    $"RingSegment_{i:00}",
                    new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f),
                    Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg),
                    new Vector3(0.05f, 0.016f, 0.018f),
                    material);
            }

            Transform label = pin.Find(ExtinguisherController.SafetyPinLabelName);
            if (label == null)
            {
                label = new GameObject(ExtinguisherController.SafetyPinLabelName).transform;
                label.SetParent(pin, false);
            }

            label.localPosition = new Vector3(-0.28f, 0.135f, -0.015f);
            label.localRotation = Quaternion.Euler(65f, 0f, 0f);
            label.localScale = Vector3.one * 0.12f;
            TextMeshPro text = label.GetComponent<TextMeshPro>();
            if (text == null)
            {
                text = label.gameObject.AddComponent<TextMeshPro>();
            }

            text.text = "PULL PIN";
            text.fontSize = 0.34f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.95f, 0.25f, 1f);
            text.enableWordWrapping = false;
            text.richText = false;
        }

        private static void CreateSafetyPinSegment(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Material material)
        {
            Transform segment = parent.Find(name);
            if (segment == null)
            {
                GameObject segmentObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segmentObject.name = name;
                segment = segmentObject.transform;
                segment.SetParent(parent, false);
            }

            segment.localPosition = localPosition;
            segment.localRotation = localRotation;
            segment.localScale = localScale;
            Collider collider = segment.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Renderer renderer = segment.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void CreateFlameTongue(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject tongue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tongue.name = name;
            tongue.transform.SetParent(parent, false);
            tongue.transform.localPosition = localPosition;
            tongue.transform.localScale = localScale;
            tongue.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(tongue.GetComponent<Collider>());
        }

        private static ParticleSystem CreateParticleSystem(
            string name,
            Transform parent,
            Vector3 localPosition,
            Color color,
            float rate,
            float lifetime,
            float speed,
            float minSize,
            float maxSize,
            float coneAngle,
            float radius,
            int maxParticles)
        {
            GameObject particleObject = new GameObject(name);
            particleObject.transform.SetParent(parent, false);
            particleObject.transform.localPosition = localPosition;
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.startColor = color;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;
            emission.enabled = true;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius;

            string meshName = name.Contains("Smoke") ? "Sphere.fbx" : "Capsule.fbx";
            VrStableParticleVisuals.ConfigureMeshParticleRenderer(particles, meshName);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static Camera CreatePreviewCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1.45f, -2f);
            camera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            return camera;
        }

        private sealed class RigReferences
        {
            public Camera CenterEyeCamera;
            public Transform CenterEyeAnchor;
            public Transform RightControllerAnchor;
        }

        private static RigReferences CreateOrUpdateOvrCameraRig()
        {
            var references = new RigReferences();
            OVRCameraRig cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();

            if (cameraRig == null)
            {
                GameObject rigPrefab = LoadOvrCameraRigPrefab();
                if (rigPrefab != null)
                {
                    GameObject rigObject = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
                    rigObject.name = "OVRCameraRig";
                    rigObject.transform.position = Vector3.zero;
                    rigObject.transform.rotation = Quaternion.identity;
                    cameraRig = rigObject.GetComponent<OVRCameraRig>();
                }
                else
                {
                    Debug.LogWarning("Could not find Meta XR OVRCameraRig prefab. Falling back to a regular preview camera.");
                }
            }

            if (cameraRig == null)
            {
                return references;
            }

            RemoveStandaloneMainCameras(cameraRig);

            references.CenterEyeAnchor = cameraRig.centerEyeAnchor != null
                ? cameraRig.centerEyeAnchor
                : FindDeepChild(cameraRig.transform, "CenterEyeAnchor");
            references.RightControllerAnchor = cameraRig.rightControllerAnchor != null
                ? cameraRig.rightControllerAnchor
                : FindDeepChild(cameraRig.transform, "RightControllerAnchor");

            if (references.CenterEyeAnchor != null)
            {
                references.CenterEyeCamera = references.CenterEyeAnchor.GetComponent<Camera>();
                if (references.CenterEyeCamera == null)
                {
                    references.CenterEyeCamera = references.CenterEyeAnchor.gameObject.AddComponent<Camera>();
                }

                references.CenterEyeCamera.tag = "MainCamera";
                references.CenterEyeCamera.nearClipPlane = 0.05f;
                references.CenterEyeCamera.farClipPlane = 100f;
            }

            return references;
        }

        private static GameObject LoadOvrCameraRigPrefab()
        {
            const string packagePath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(packagePath);
            if (prefab != null)
            {
                return prefab;
            }

            string[] guids = AssetDatabase.FindAssets("OVRCameraRig t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/OVRCameraRig.prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }

            return null;
        }

        private static void RemoveStandaloneMainCameras(OVRCameraRig cameraRig)
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera sceneCamera in cameras)
            {
                if (sceneCamera == null || sceneCamera.GetComponentInParent<OVRCameraRig>() != null)
                {
                    continue;
                }

                if (sceneCamera.CompareTag("MainCamera") || sceneCamera.gameObject.name == "Main Camera")
                {
                    Object.DestroyImmediate(sceneCamera.gameObject);
                }
            }
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (Transform child in parent)
            {
                if (child.name == childName)
                {
                    return child;
                }

                Transform match = FindDeepChild(child, childName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static GameObject FindOrCreate(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                return existing;
            }

            return new GameObject(name);
        }

        private static Transform[] CreateSpawnPoints(Transform parent)
        {
            Vector3[] positions =
            {
                new Vector3(0f, 0f, 2.1f),
                new Vector3(-0.9f, 0f, 2.4f),
                new Vector3(0.9f, 0f, 2.35f),
            };

            var points = new List<Transform>();
            for (int i = 0; i < positions.Length; i++)
            {
                string pointName = $"FireSpawnPoint_{i + 1}";
                Transform point = parent.Find(pointName);
                if (point == null)
                {
                    GameObject pointObject = new GameObject(pointName);
                    pointObject.transform.SetParent(parent, false);
                    point = pointObject.transform;
                }

                point.localPosition = positions[i];
                point.localRotation = Quaternion.identity;
                points.Add(point);
            }

            return points.ToArray();
        }

        private static ExtinguisherController CreateOrUpdateExtinguisherInstance(
            GameObject prefab,
            Transform controllerAnchor,
            Camera camera)
        {
            GameObject existing = GameObject.Find("TrainingExtinguisher_Instance");
            if (existing == null)
            {
                existing = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                existing.name = "TrainingExtinguisher_Instance";
            }

            if (controllerAnchor != null)
            {
                existing.transform.SetParent(controllerAnchor, false);
                existing.transform.localPosition = new Vector3(0.08f, -0.08f, 0.18f);
                existing.transform.localRotation = Quaternion.identity;
                existing.transform.localScale = Vector3.one;
            }
            else if (camera != null)
            {
                existing.transform.SetParent(null);
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.forward;
                }

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                existing.transform.position = camera.transform.position + forward * 0.95f + right * 0.45f - Vector3.up * 0.55f;
                existing.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            else
            {
                existing.transform.SetParent(null);
                existing.transform.position = new Vector3(0.45f, 0.75f, 0.8f);
                existing.transform.rotation = Quaternion.identity;
            }

            AddComponentIfMissing<ExtinguisherCapacityGauge>(existing).ForceRefresh();
            return existing.GetComponent<ExtinguisherController>();
        }

        private static TrainingHUD CreateOrUpdateHud(Transform hudParent, Camera worldCamera)
        {
            GameObject canvasObject = GameObject.Find("FireTrainerHUD");
            if (canvasObject == null)
            {
                canvasObject = new GameObject("FireTrainerHUD");
                canvasObject.AddComponent<Canvas>();
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = canvasObject.AddComponent<Canvas>();
            }

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvasObject.AddComponent<CanvasScaler>();
            }

            if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            {
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = worldCamera;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasObject.transform.SetParent(null, true);

            if (worldCamera != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.forward;
                }

                canvasObject.transform.position = worldCamera.transform.position + forward * 1.35f - Vector3.up * 0.22f;
                canvasObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            else
            {
                canvasObject.transform.position = new Vector3(0f, 1.25f, 1.35f);
                canvasObject.transform.rotation = Quaternion.identity;
            }

            canvasObject.transform.localScale = Vector3.one * 0.00135f;
            canvasRect.sizeDelta = new Vector2(960f, 470f);

            TrainingHUD hud = canvasObject.GetComponent<TrainingHUD>();
            if (hud == null)
            {
                hud = canvasObject.AddComponent<TrainingHUD>();
            }

            TextMeshProUGUI stepText = CreateText("Step Text", canvasRect, new Vector2(24f, -24f), new Vector2(560f, 44f), 28f);
            TextMeshProUGUI checklistText = CreateText("Checklist Text", canvasRect, new Vector2(24f, -72f), new Vector2(520f, 150f), 19f);
            TextMeshProUGUI statusText = CreateText("Status Text", canvasRect, new Vector2(24f, -232f), new Vector2(840f, 92f), 20f);
            Slider extinguisherSlider = CreateSlider("Extinguisher Capacity", canvasRect, new Vector2(24f, -340f), new Vector2(360f, 22f), new Color(0.1f, 0.65f, 1f, 1f));
            Slider fireSlider = CreateSlider("Fire Health", canvasRect, new Vector2(24f, -374f), new Vector2(360f, 22f), new Color(1f, 0.25f, 0.05f, 1f));

            GameObject resultPanel = CreatePanel("Result Panel", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(680f, 410f));
            TextMeshProUGUI resultText = CreateText("Result Text", resultPanel.GetComponent<RectTransform>(), new Vector2(24f, -24f), new Vector2(632f, 362f), 21f);
            resultText.alignment = TextAlignmentOptions.Center;
            resultPanel.SetActive(false);

            GameObject introPanel = CreatePanel("Intro Panel", canvasRect, new Vector2(0.5f, 0.5f), new Vector2(760f, 390f));
            TextMeshProUGUI introText = CreateText("Intro Text", introPanel.GetComponent<RectTransform>(), new Vector2(32f, -28f), new Vector2(696f, 334f), 24f);
            introText.alignment = TextAlignmentOptions.Center;
            introPanel.SetActive(false);

            SetObjectReference(hud, "stepText", stepText);
            SetObjectReference(hud, "checklistText", checklistText);
            SetObjectReference(hud, "statusText", statusText);
            SetObjectReference(hud, "resultText", resultText);
            SetObjectReference(hud, "introText", introText);
            SetObjectReference(hud, "extinguisherSlider", extinguisherSlider);
            SetObjectReference(hud, "fireSlider", fireSlider);
            SetObjectReference(hud, "resultPanel", resultPanel);
            SetObjectReference(hud, "introPanel", introPanel);
            SetBool(hud, "detachFromCameraRigOnStart", true);

            return hud;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            RectTransform parent,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize)
        {
            GameObject textObject = parent.Find(name)?.gameObject;
            if (textObject == null)
            {
                textObject = new GameObject(name);
                textObject.transform.SetParent(parent, false);
            }

            RectTransform rect = textObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = textObject.AddComponent<RectTransform>();
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                text = textObject.AddComponent<TextMeshProUGUI>();
            }

            text.text = name;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private static Slider CreateSlider(
            string name,
            RectTransform parent,
            Vector2 anchoredPosition,
            Vector2 size,
            Color fillColor)
        {
            GameObject sliderObject = parent.Find(name)?.gameObject;
            if (sliderObject == null)
            {
                sliderObject = new GameObject(name);
                sliderObject.transform.SetParent(parent, false);
            }

            RectTransform rect = EnsureRectTransform(sliderObject);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image background = CreateImageChild(sliderObject.transform, "Background", new Color(0f, 0f, 0f, 0.65f));
            Stretch(background.rectTransform, Vector2.zero, Vector2.zero);

            RectTransform fillArea = FindOrCreateRect(sliderObject.transform, "Fill Area");
            Stretch(fillArea, new Vector2(3f, 3f), new Vector2(-3f, -3f));

            Image fill = CreateImageChild(fillArea, "Fill", fillColor);
            Stretch(fill.rectTransform, Vector2.zero, Vector2.zero);

            Slider slider = sliderObject.GetComponent<Slider>();
            if (slider == null)
            {
                slider = sliderObject.AddComponent<Slider>();
            }

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.fillRect = fill.rectTransform;
            slider.targetGraphic = fill;
            return slider;
        }

        private static GameObject CreatePanel(string name, RectTransform parent, Vector2 anchor, Vector2 size)
        {
            GameObject panel = parent.Find(name)?.gameObject;
            if (panel == null)
            {
                panel = new GameObject(name);
                panel.transform.SetParent(parent, false);
            }

            RectTransform rect = EnsureRectTransform(panel);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            Image image = panel.GetComponent<Image>();
            if (image == null)
            {
                image = panel.AddComponent<Image>();
            }

            image.color = new Color(0f, 0f, 0f, 0.82f);
            return panel;
        }

        private static RectTransform EnsureRectTransform(GameObject gameObject)
        {
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            return rect != null ? rect : gameObject.AddComponent<RectTransform>();
        }

        private static Image CreateImageChild(Transform parent, string name, Color color)
        {
            RectTransform rect = FindOrCreateRect(parent, name);
            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
            }

            image.color = color;
            return image;
        }

        private static RectTransform FindOrCreateRect(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            GameObject gameObject = existing != null ? existing.gameObject : new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return EnsureRectTransform(gameObject);
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static T AddComponentIfMissing<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void EnsureEventSystem()
        {
            EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
            GameObject eventSystem = existing != null ? existing.gameObject : new GameObject("EventSystem");

            if (existing == null)
            {
                eventSystem.AddComponent<EventSystem>();
            }

            StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                Object.DestroyImmediate(legacyModule);
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static void EnsureBuildScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return;
            }

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == scenePath)
                {
                    scene.enabled = true;
                    return;
                }
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            if (target == null || value == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
            {
                property.objectReferenceValue = value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void SetObjectArray(Object target, string propertyName, Object[] values)
        {
            if (target == null || values == null)
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Float)
            {
                property.floatValue = value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void SetInt(Object target, string propertyName, int value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Boolean)
            {
                property.boolValue = value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private static void SetVector3(Object target, string propertyName, Vector3 value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null && property.propertyType == SerializedPropertyType.Vector3)
            {
                property.vector3Value = value;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif
