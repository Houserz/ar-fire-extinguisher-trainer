#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FireExtinguisherTrainer;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FireExtinguisherTrainerEditor
{
    public static class PlatformSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/FireTrainerWeek1.unity";
        private const string ExtinguisherPrefabPath = "Assets/FireExtinguisherTrainer/Prefabs/TrainingExtinguisher.prefab";
        private const string OculusProjectConfigPath = "Assets/Oculus/OculusProjectConfig.asset";
        private const string MrukPrefabPath = "Packages/com.meta.xr.mrutilitykit/Core/Tools/MRUK.prefab";
        private const string MrSpatialOriginName = "MRSpatialOrigin";

        [MenuItem("Tools/Fire Trainer/Setup Platform Training Scene")]
        public static void SetupPlatformTrainingScene()
        {
            Scene scene = OpenScene();
            GameObject extinguisherPrefab = PrepareExtinguisherPrefab();
            if (extinguisherPrefab == null)
            {
                return;
            }

            OVRCameraRig rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null)
            {
                Debug.LogWarning("Platform setup needs an OVRCameraRig. Run the Week 1 scene setup first.");
                return;
            }

            GameObject player = CreateOrUpdatePlayer(rig);
            Transform leftHand = FindDeepChild(rig.transform, "LeftControllerAnchor");
            Transform rightHand = FindDeepChild(rig.transform, "RightControllerAnchor");
            Transform centerEye = rig.centerEyeAnchor != null
                ? rig.centerEyeAnchor
                : FindDeepChild(rig.transform, "CenterEyeAnchor");
            Camera centerEyeCamera = centerEye != null ? centerEye.GetComponent<Camera>() : Camera.main;

            CreateHandSphere(leftHand, "Left Hand Ball", new Color(0.1f, 0.55f, 1f, 1f));
            CreateHandSphere(rightHand, "Right Hand Ball", new Color(1f, 0.15f, 0.08f, 1f));

            Transform platformRoot = CreatePlatform();
            CreateOrUpdateMrukRuntime(player, rig, centerEyeCamera);
            ConfigureOculusProjectMixedReality();
            Transform[] spawnPoints = CreateFireSpawnPoints(platformRoot);
            Transform stationSpawn = CreateStationVisual(platformRoot);

            GameObject root = FindOrCreate("FireTrainer_Week1");
            FireSpawner spawner = root.GetComponent<FireSpawner>();
            FireTrainingManager manager = root.GetComponent<FireTrainingManager>();
            if (spawner == null || manager == null)
            {
                Debug.LogWarning("Platform setup needs FireSpawner and FireTrainingManager on FireTrainer_Week1.");
                return;
            }

            SetObjectArray(spawner, "spawnPoints", spawnPoints);

            ExtinguisherStation station = FindOrCreate("ExtinguisherStation").GetComponent<ExtinguisherStation>();
            if (station == null)
            {
                station = GameObject.Find("ExtinguisherStation").AddComponent<ExtinguisherStation>();
            }

            SetObjectReference(station, "extinguisherPrefab", extinguisherPrefab.GetComponent<ExtinguisherController>());
            SetObjectReference(station, "spawnPoint", stationSpawn);
            SetObjectReference(station, "rightHandAnchor", rightHand);
            SetObjectReference(station, "trainingManager", manager);
            SetBool(station, "spawnOnStart", true);
            ExtinguisherController stationExtinguisher = CreateOrUpdateStationExtinguisher(
                station.transform,
                stationSpawn,
                extinguisherPrefab,
                manager,
                station,
                rightHand);
            SetObjectReference(station, "availableExtinguisher", stationExtinguisher);

            SpatialTrainingPlacementManager placement = AddComponentIfMissing<SpatialTrainingPlacementManager>(root);
            Transform placementReference = centerEyeCamera != null ? centerEyeCamera.transform : player.transform;
            SetObjectReference(placement, "userOrigin", placementReference);
            SetObjectReference(placement, "station", station);
            SetBool(placement, "preferMetaSceneFloor", true);
            SetFloat(placement, "fireMinDistance", 2f);
            SetFloat(placement, "fireMaxDistance", 3f);
            SetFloat(placement, "stationMinDistance", 0.8f);
            SetFloat(placement, "stationMaxDistance", 1.4f);
            SetFloat(placement, "minimumFireStationDistance", 1f);
            SetFloat(placement, "fallbackGroundY", 0f);
            SetObjectReference(spawner, "spatialPlacement", placement);

            MixedRealityTrainingRuntime mrRuntime = AddComponentIfMissing<MixedRealityTrainingRuntime>(root);
            mrRuntime.Configure(platformRoot.gameObject, centerEyeCamera);
            SetObjectReference(mrRuntime, "platformRoot", platformRoot.gameObject);
            SetObjectReference(mrRuntime, "centerEyeCamera", centerEyeCamera);
            SetBool(mrRuntime, "hidePlatformInMrRuntime", true);

            ExtinguisherInteractionDriver interactionDriver = CreateOrUpdateInteractionDriver(
                rightHand,
                leftHand,
                player.transform,
                manager,
                station);

            SetObjectReference(manager, "extinguisherStation", station);
            SetObjectReference(manager, "interactionDriver", interactionDriver);
            SetObjectReference(manager, "hud", Object.FindFirstObjectByType<TrainingHUD>());
            SetObjectReference(manager, "playerCamera", centerEyeCamera);
            SetObjectReference(manager, "rayOriginOverride", rightHand);
            SetObjectReference(manager, "extinguisher", null);
            SetInt(manager, "totalExtinguishers", 0);
            SetBool(manager, "waitForSpatialPlacementOnStart", true);
            SetFloat(manager, "spatialScanTimeoutSeconds", 3f);
            SetBool(manager, "showIntroOnFirstStart", true);
            SetFloat(manager, "introMinimumSeconds", 2.5f);
            SetFloat(manager, "introAutoDismissSeconds", 18f);

            DeleteSceneObject("RightPhysicsGrabHandle");
            DeleteSceneObject("TrainingExtinguisher_Instance");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Platform training scene setup complete on {player.name}.");
        }

        [MenuItem("Tools/Fire Trainer/Validate Platform Training Scene")]
        public static void ValidatePlatformTrainingScene()
        {
            OpenScene();

            OVRCameraRig rig = Require(Object.FindFirstObjectByType<OVRCameraRig>(), "OVRCameraRig");
            GameObject player = Require(GameObject.Find("FireTrainerPlayer"), "FireTrainerPlayer");
            Require(player.GetComponent<CharacterController>(), "FireTrainerPlayer CharacterController");
            Require(player.GetComponent<OVRPlayerController>(), "FireTrainerPlayer OVRPlayerController");
            Require(rig.GetComponentInParent<OVRPlayerController>(), "OVRCameraRig parented under OVRPlayerController");
            RequireNoComponentType(player, "XROrigin");
            RequireNoComponentType(player, "ARPlaneManager");
            RequireNoSceneObject("AR Session");
            RequireNoSceneObject(MrSpatialOriginName);
            RequireNoSceneComponent("XROrigin");
            RequireNoSceneComponent("ARPlaneManager");

            Camera centerEyeCamera = Require(rig.centerEyeAnchor != null
                ? rig.centerEyeAnchor.GetComponent<Camera>()
                : FindDeepChild(rig.transform, "CenterEyeAnchor")?.GetComponent<Camera>(), "CenterEyeAnchor Camera");

            MRUK mruk = Require(Object.FindFirstObjectByType<MRUK>(), "MRUK");
            if (mruk.EnableWorldLock)
            {
                throw new InvalidOperationException("MRUK EnableWorldLock must be disabled to avoid moving OVRCameraRig tracking space.");
            }

            if (mruk.SceneSettings == null ||
                mruk.SceneSettings.DataSource != MRUK.SceneDataSource.Device ||
                !mruk.SceneSettings.LoadSceneOnStartup)
            {
                throw new InvalidOperationException("MRUK must load device scene data on startup.");
            }

            OVRManager ovrManager = Require(rig.GetComponent<OVRManager>(), "OVRManager");
            if (!ovrManager.isInsightPassthroughEnabled)
            {
                throw new InvalidOperationException("OVRManager passthrough must be enabled.");
            }

            OVRPassthroughLayer passthroughLayer = Require(rig.GetComponent<OVRPassthroughLayer>(), "OVRPassthroughLayer");
            if (passthroughLayer.hidden || passthroughLayer.overlayType != OVROverlay.OverlayType.Underlay)
            {
                throw new InvalidOperationException("OVRPassthroughLayer must be visible as an underlay.");
            }

            RequireOculusProjectPassthroughConfig();

            Transform leftHand = Require(FindDeepChild(rig.transform, "LeftControllerAnchor"), "LeftControllerAnchor");
            Transform rightHand = Require(FindDeepChild(rig.transform, "RightControllerAnchor"), "RightControllerAnchor");
            Require(leftHand.Find("Left Hand Ball"), "Left hand sphere");
            Require(rightHand.Find("Right Hand Ball"), "Right hand sphere");
            Transform driverTransform = Require(rightHand.Find("ExtinguisherInteractionDriver"), "ExtinguisherInteractionDriver");

            GameObject platform = Require(GameObject.Find("FireTrainer_Platform"), "FireTrainer_Platform");
            for (int i = 1; i <= 5; i++)
            {
                Require(platform.transform.Find($"FireSpawnPoint_{i}"), $"FireSpawnPoint_{i}");
            }

            GameObject root = Require(GameObject.Find("FireTrainer_Week1"), "FireTrainer_Week1");
            FireSpawner spawner = Require(root.GetComponent<FireSpawner>(), "FireSpawner");
            FireTrainingManager manager = Require(root.GetComponent<FireTrainingManager>(), "FireTrainingManager");
            SpatialTrainingPlacementManager placement = Require(
                root.GetComponent<SpatialTrainingPlacementManager>(),
                "SpatialTrainingPlacementManager");
            MixedRealityTrainingRuntime mrRuntime = Require(
                root.GetComponent<MixedRealityTrainingRuntime>(),
                "MixedRealityTrainingRuntime");
            ExtinguisherStation station = Require(Object.FindFirstObjectByType<ExtinguisherStation>(), "ExtinguisherStation");
            Require(station.transform.Find("ExtinguisherSpawnPoint"), "ExtinguisherSpawnPoint");
            Transform stationExtinguisherTransform = Require(
                station.transform.Find("Station Extinguisher"),
                "Station Extinguisher scene instance");
            ExtinguisherController stationExtinguisher = Require(
                stationExtinguisherTransform.GetComponent<ExtinguisherController>(),
                "Station Extinguisher ExtinguisherController");
            ExtinguisherInteractionDriver interactionDriver = Require(
                driverTransform.GetComponent<ExtinguisherInteractionDriver>(),
                "ExtinguisherInteractionDriver");

            RequireSerializedReference(manager, "extinguisherStation", station);
            RequireSerializedReference(manager, "interactionDriver", interactionDriver);
            RequireSerializedReference(manager, "rayOriginOverride", rightHand);
            RequireSerializedBool(manager, "waitForSpatialPlacementOnStart", true);
            RequireSerializedFloat(manager, "spatialScanTimeoutSeconds", 3f);
            RequireSerializedReference(interactionDriver, "trainingManager", manager);
            RequireSerializedReference(interactionDriver, "station", station);
            RequireSerializedReference(interactionDriver, "rightHandAnchor", rightHand);
            RequireSerializedReference(interactionDriver, "leftHandAnchor", leftHand);
            RequireSerializedReference(interactionDriver, "playerCollisionRoot", player.transform);
            RequireSerializedReference(station, "availableExtinguisher", stationExtinguisher);
            RequireSerializedReference(spawner, "spatialPlacement", placement);
            RequireSerializedReference(placement, "userOrigin", centerEyeCamera.transform);
            RequireSerializedReference(placement, "station", station);
            RequireSerializedBool(placement, "preferMetaSceneFloor", true);
            RequireSerializedFloat(placement, "fireMaxDistance", 3f);
            RequireSerializedFloat(placement, "stationMaxDistance", 1.4f);
            RequireSerializedFloat(placement, "fallbackGroundY", 0f);
            RequireSerializedReference(mrRuntime, "platformRoot", platform);
            RequireSerializedReference(mrRuntime, "centerEyeCamera", centerEyeCamera);

            SerializedProperty spawnPoints = new SerializedObject(spawner).FindProperty("spawnPoints");
            if (spawnPoints == null || !spawnPoints.isArray || spawnPoints.arraySize < 5)
            {
                throw new InvalidOperationException("FireSpawner needs five platform spawn points.");
            }

            GameObject prefab = Require(AssetDatabase.LoadAssetAtPath<GameObject>(ExtinguisherPrefabPath), "TrainingExtinguisher prefab");
            Rigidbody prefabRigidbody = Require(prefab.GetComponent<Rigidbody>(), "TrainingExtinguisher Rigidbody");
            if (prefabRigidbody.isKinematic)
            {
                throw new InvalidOperationException("TrainingExtinguisher Rigidbody must be non-kinematic.");
            }

            RequireStableRigidbody(prefabRigidbody, "TrainingExtinguisher Rigidbody");
            RequireStableBodyWidth(prefab.transform, "TrainingExtinguisher Body");
            RequireInteractionPoint(prefab.transform, ExtinguisherInteractionDriver.RightGripPoseName);
            RequireInteractionPoint(prefab.transform, ExtinguisherInteractionDriver.LeftSupportPoseName);
            RequireInteractionPoint(prefab.transform, ExtinguisherInteractionDriver.PinPullZoneName);
            RequireNonTriggerCollider(prefab, "TrainingExtinguisher collider");
            Grabbable grabbable = Require(prefab.GetComponent<Grabbable>(), "TrainingExtinguisher Grabbable");
            GrabInteractable grabInteractable = Require(prefab.GetComponent<GrabInteractable>(), "TrainingExtinguisher GrabInteractable");
            Require(prefab.GetComponent<ExtinguisherHoldTracker>(), "TrainingExtinguisher ExtinguisherHoldTracker");
            RequireSerializedReference(grabInteractable, "_pointableElement", grabbable);
            RequireUsableSceneExtinguisher(stationExtinguisher);

            Debug.Log("Platform training scene validation passed.");
        }

        private static Scene OpenScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.path == ScenePath)
            {
                return activeScene;
            }

            return EditorSceneManager.OpenScene(ScenePath);
        }

        private static GameObject PrepareExtinguisherPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExtinguisherPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Could not load extinguisher prefab at {ExtinguisherPrefabPath}.");
                return null;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ExtinguisherPrefabPath);
            Rigidbody rigidbody = root.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = root.AddComponent<Rigidbody>();
            }

            ConfigureStableRigidbody(rigidbody);
            ConfigureStableExtinguisherShape(root.transform);
            EnsureExtinguisherInteractionPoints(root.transform);

            Grabbable grabbable = AddComponentIfMissing<Grabbable>(root);
            GrabInteractable grabInteractable = AddComponentIfMissing<GrabInteractable>(root);
            ExtinguisherHoldTracker tracker = AddComponentIfMissing<ExtinguisherHoldTracker>(root);

            SetObjectReference(grabbable, "_rigidbody", rigidbody);
            SetObjectReference(grabbable, "_targetTransform", root.transform);
            SetBool(grabbable, "_throwWhenUnselected", true);
            SetBool(grabbable, "_kinematicWhileSelected", true);
            SetObjectReference(grabInteractable, "_rigidbody", rigidbody);
            SetObjectReference(grabInteractable, "_pointableElement", grabbable);
            SetBool(tracker, "enableGripFallback", false);

            PrefabUtility.SaveAsPrefabAsset(root, ExtinguisherPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.ImportAsset(ExtinguisherPrefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ExtinguisherPrefabPath);
        }

        private static GameObject CreateOrUpdatePlayer(OVRCameraRig rig)
        {
            GameObject player = FindOrCreate("FireTrainerPlayer");
            player.transform.position = Vector3.zero;
            player.transform.rotation = Quaternion.identity;

            CharacterController characterController = AddComponentIfMissing<CharacterController>(player);
            characterController.height = 1.75f;
            characterController.radius = 0.28f;
            characterController.center = new Vector3(0f, 0.875f, 0f);
            characterController.stepOffset = 0.25f;
            characterController.slopeLimit = 35f;

            OVRPlayerController playerController = AddComponentIfMissing<OVRPlayerController>(player);
            playerController.Acceleration = 0.08f;
            playerController.Damping = 0.35f;
            playerController.BackAndSideDampen = 0.65f;
            playerController.SnapRotation = true;
            playerController.EnableLinearMovement = true;
            playerController.EnableRotation = true;
            playerController.RotationEitherThumbstick = false;

            rig.transform.SetParent(player.transform, false);
            rig.transform.localPosition = Vector3.zero;
            rig.transform.localRotation = Quaternion.identity;
            return player;
        }

        private static MRUK CreateOrUpdateMrukRuntime(
            GameObject player,
            OVRCameraRig rig,
            Camera centerEyeCamera)
        {
            DeleteSceneObject("AR Session");
            DeleteSceneObject(MrSpatialOriginName);
            RemoveComponentIfPresentByTypeName(player, "ARPlaneManager");
            RemoveComponentIfPresentByTypeName(player, "XROrigin");

            MRUK mruk = Object.FindFirstObjectByType<MRUK>();
            if (mruk == null)
            {
                GameObject mrukPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MrukPrefabPath);
                GameObject mrukObject = mrukPrefab != null
                    ? PrefabUtility.InstantiatePrefab(mrukPrefab) as GameObject
                    : new GameObject("MRUK");
                if (mrukObject == null)
                {
                    mrukObject = new GameObject("MRUK");
                }

                mrukObject.name = "MRUK";
                mruk = AddComponentIfMissing<MRUK>(mrukObject);
            }

            mruk.gameObject.name = "MRUK";
            mruk.transform.SetParent(null, false);
            mruk.transform.position = Vector3.zero;
            mruk.transform.rotation = Quaternion.identity;
            mruk.transform.localScale = Vector3.one;
            mruk.EnableWorldLock = false;
            if (mruk.SceneSettings == null)
            {
                mruk.SceneSettings = new MRUK.MRUKSettings();
            }

            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            mruk.SceneSettings.LoadSceneOnStartup = true;
            mruk.SceneSettings.EnableHighFidelityScene = false;
            EditorUtility.SetDirty(mruk);

            if (centerEyeCamera != null)
            {
                centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                centerEyeCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            if (rig != null)
            {
                OVRManager ovrManager = AddComponentIfMissing<OVRManager>(rig.gameObject);
                ovrManager.isInsightPassthroughEnabled = true;
                SetBool(ovrManager, "isInsightPassthroughEnabled", true);
                EditorUtility.SetDirty(ovrManager);

                OVRPassthroughLayer passthroughLayer = AddComponentIfMissing<OVRPassthroughLayer>(rig.gameObject);
                passthroughLayer.hidden = false;
                passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
                passthroughLayer.textureOpacity = 1f;
                SetBool(passthroughLayer, "hidden", false);
                EditorUtility.SetDirty(passthroughLayer);
            }

            return mruk;
        }

        private static void ConfigureOculusProjectMixedReality()
        {
            Object config = AssetDatabase.LoadAssetAtPath<Object>(OculusProjectConfigPath);
            if (config == null)
            {
                Debug.LogWarning($"Could not find Oculus project config at {OculusProjectConfigPath}.");
                return;
            }

            SerializedObject serializedObject = new SerializedObject(config);
            bool changed = false;
            changed |= SetSerializedIntAtLeastIfPresent(serializedObject, "anchorSupport", 1);
            changed |= SetSerializedIntAtLeastIfPresent(serializedObject, "sceneSupport", 2);
            changed |= SetSerializedIntAtLeastIfPresent(serializedObject, "_insightPassthroughSupport", 1);
            changed |= SetSerializedBoolIfPresent(serializedObject, "isPassthroughCameraAccessEnabled", true);

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(config);
            }
        }

        private static ExtinguisherInteractionDriver CreateOrUpdateInteractionDriver(
            Transform rightHand,
            Transform leftHand,
            Transform playerCollisionRoot,
            FireTrainingManager manager,
            ExtinguisherStation station)
        {
            if (rightHand == null)
            {
                return null;
            }

            Transform driverTransform = rightHand.Find("ExtinguisherInteractionDriver");
            if (driverTransform == null)
            {
                driverTransform = new GameObject("ExtinguisherInteractionDriver").transform;
                driverTransform.SetParent(rightHand, false);
            }

            driverTransform.localPosition = Vector3.zero;
            driverTransform.localRotation = Quaternion.identity;
            driverTransform.localScale = Vector3.one;

            ExtinguisherPhysicalGrabber oldGrabber = driverTransform.GetComponent<ExtinguisherPhysicalGrabber>();
            if (oldGrabber != null)
            {
                Object.DestroyImmediate(oldGrabber);
            }

            ExtinguisherInteractionDriver driver = AddComponentIfMissing<ExtinguisherInteractionDriver>(driverTransform.gameObject);
            SetObjectReference(driver, "trainingManager", manager);
            SetObjectReference(driver, "station", station);
            SetObjectReference(driver, "rightHandAnchor", rightHand);
            SetObjectReference(driver, "leftHandAnchor", leftHand);
            SetObjectReference(driver, "playerCollisionRoot", playerCollisionRoot);
            SetFloat(driver, "pickupRadius", 0.75f);
            SetFloat(driver, "leftSupportPickupRadius", 0.34f);
            SetFloat(driver, "pinPullRadius", 0.35f);
            SetFloat(driver, "pinPullTravelDistance", 0.12f);
            SetFloat(driver, "pinReleaseDistanceFromZone", 0.2f);
            SetFloat(driver, "positionFollowStrength", 45f);
            SetFloat(driver, "rotationFollowStrength", 22f);
            return driver;
        }

        private static ExtinguisherController CreateOrUpdateStationExtinguisher(
            Transform station,
            Transform spawn,
            GameObject extinguisherPrefab,
            FireTrainingManager manager,
            ExtinguisherStation stationComponent,
            Transform rightHand)
        {
            Transform existing = station.Find("Station Extinguisher");
            GameObject extinguisherObject = existing != null ? existing.gameObject : null;
            if (extinguisherObject == null || extinguisherObject.GetComponent<ExtinguisherController>() == null)
            {
                if (extinguisherObject != null)
                {
                    Object.DestroyImmediate(extinguisherObject);
                }

                extinguisherObject = PrefabUtility.InstantiatePrefab(extinguisherPrefab, station) as GameObject;
                if (extinguisherObject == null)
                {
                    extinguisherObject = Object.Instantiate(extinguisherPrefab, station);
                }
            }

            extinguisherObject.name = "Station Extinguisher";
            extinguisherObject.SetActive(true);
            extinguisherObject.transform.SetParent(station, false);
            extinguisherObject.transform.position = spawn != null ? spawn.position : station.position;
            extinguisherObject.transform.rotation = spawn != null ? spawn.rotation : station.rotation;
            extinguisherObject.transform.localScale = Vector3.one;

            ConfigureExtinguisherInstance(extinguisherObject, manager, stationComponent, rightHand);
            ExtinguisherController extinguisher = extinguisherObject.GetComponent<ExtinguisherController>();
            stationComponent.SetAvailableExtinguisher(extinguisher, true);
            EditorUtility.SetDirty(extinguisherObject);
            return extinguisher;
        }

        private static void ConfigureExtinguisherInstance(
            GameObject extinguisherObject,
            FireTrainingManager manager,
            ExtinguisherStation station,
            Transform rightHand)
        {
            Rigidbody rigidbody = AddComponentIfMissing<Rigidbody>(extinguisherObject);
            ConfigureStableRigidbody(rigidbody);
            ConfigureStableExtinguisherShape(extinguisherObject.transform);
            EnsureExtinguisherInteractionPoints(extinguisherObject.transform);
            EnsureCapacityGauge(extinguisherObject);

            Grabbable grabbable = AddComponentIfMissing<Grabbable>(extinguisherObject);
            GrabInteractable grabInteractable = AddComponentIfMissing<GrabInteractable>(extinguisherObject);
            ExtinguisherHoldTracker tracker = AddComponentIfMissing<ExtinguisherHoldTracker>(extinguisherObject);

            SetObjectReference(grabbable, "_rigidbody", rigidbody);
            SetObjectReference(grabbable, "_targetTransform", extinguisherObject.transform);
            SetBool(grabbable, "_throwWhenUnselected", true);
            SetBool(grabbable, "_kinematicWhileSelected", true);
            SetObjectReference(grabInteractable, "_rigidbody", rigidbody);
            SetObjectReference(grabInteractable, "_pointableElement", grabbable);
            SetObjectReference(tracker, "trainingManager", manager);
            SetObjectReference(tracker, "station", station);
            SetObjectReference(tracker, "holdAnchor", rightHand);
            SetBool(tracker, "enableGripFallback", false);
        }

        private static void ConfigureStableRigidbody(Rigidbody rigidbody)
        {
            rigidbody.mass = 1.2f;
            rigidbody.useGravity = true;
            rigidbody.isKinematic = false;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.centerOfMass = new Vector3(0f, 0.22f, 0f);
            rigidbody.angularDamping = Mathf.Max(rigidbody.angularDamping, 1.25f);
            rigidbody.maxAngularVelocity = Mathf.Min(rigidbody.maxAngularVelocity, 6f);
        }

        private static void ConfigureStableExtinguisherShape(Transform root)
        {
            Transform body = root.Find("Body");
            if (body != null)
            {
                body.localScale = new Vector3(0.24f, 0.45f, 0.24f);
            }

            CreateOrUpdateSafetyPinVisual(root);
            EnsureCapacityGauge(root.gameObject);
        }

        private static void EnsureCapacityGauge(GameObject root)
        {
            ExtinguisherCapacityGauge gauge = AddComponentIfMissing<ExtinguisherCapacityGauge>(root);
            gauge.ForceRefresh();
        }

        private static void EnsureExtinguisherInteractionPoints(Transform root)
        {
            CreateOrUpdateInteractionPoint(
                root,
                ExtinguisherInteractionDriver.RightGripPoseName,
                new Vector3(0f, 0.92f, -0.06f),
                Quaternion.identity);
            CreateOrUpdateInteractionPoint(
                root,
                ExtinguisherInteractionDriver.LeftSupportPoseName,
                new Vector3(0f, 0.62f, 0.18f),
                Quaternion.identity);
            CreateOrUpdateInteractionPoint(
                root,
                ExtinguisherInteractionDriver.PinPullZoneName,
                new Vector3(0f, 1.02f, 0f),
                Quaternion.identity);
        }

        private static void CreateOrUpdateSafetyPinVisual(Transform root)
        {
            Transform existing = root.Find(ExtinguisherController.SafetyPinName);
            GameObject pin = existing != null ? existing.gameObject : new GameObject(ExtinguisherController.SafetyPinName);
            pin.name = ExtinguisherController.SafetyPinName;
            pin.transform.SetParent(root, false);
            pin.transform.localPosition = new Vector3(0f, 1.04f, 0f);
            pin.transform.localRotation = Quaternion.identity;
            pin.transform.localScale = Vector3.one;

            Collider collider = pin.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Renderer renderer = pin.GetComponent<Renderer>();
            if (renderer != null)
            {
                Object.DestroyImmediate(renderer);
            }

            MeshFilter filter = pin.GetComponent<MeshFilter>();
            if (filter != null)
            {
                Object.DestroyImmediate(filter);
            }

            Material material = CreateMaterial("Extinguisher_SafetyPin", new Color(1f, 0.9f, 0.08f, 1f));
            CreateSafetyPinSegment(
                pin.transform,
                ExtinguisherController.SafetyPinShaftName,
                new Vector3(0.05f, 0f, 0f),
                Quaternion.identity,
                new Vector3(0.42f, 0.026f, 0.026f),
                material);

            Transform ring = pin.transform.Find(ExtinguisherController.SafetyPinRingName);
            if (ring == null)
            {
                ring = new GameObject(ExtinguisherController.SafetyPinRingName).transform;
                ring.SetParent(pin.transform, false);
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

            Transform label = pin.transform.Find(ExtinguisherController.SafetyPinLabelName);
            if (label == null)
            {
                label = new GameObject(ExtinguisherController.SafetyPinLabelName).transform;
                label.SetParent(pin.transform, false);
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

        private static Transform CreateOrUpdateInteractionPoint(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            Transform point = parent.Find(name);
            if (point == null)
            {
                point = new GameObject(name).transform;
                point.SetParent(parent, false);
            }

            point.localPosition = localPosition;
            point.localRotation = localRotation;
            point.localScale = Vector3.one;
            return point;
        }

        private static Transform CreatePlatform()
        {
            GameObject root = FindOrCreate("FireTrainer_Platform");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            CreatePrimitiveChild(root.transform, "Platform Floor", PrimitiveType.Cube, new Vector3(0f, -0.05f, 2.6f), new Vector3(6.4f, 0.1f, 6.4f), new Color(0.22f, 0.24f, 0.26f, 1f));
            CreatePrimitiveChild(root.transform, "Wall North", PrimitiveType.Cube, new Vector3(0f, 0.35f, 5.85f), new Vector3(6.5f, 0.7f, 0.12f), new Color(0.12f, 0.13f, 0.14f, 1f));
            CreatePrimitiveChild(root.transform, "Wall South", PrimitiveType.Cube, new Vector3(0f, 0.35f, -0.65f), new Vector3(6.5f, 0.7f, 0.12f), new Color(0.12f, 0.13f, 0.14f, 1f));
            CreatePrimitiveChild(root.transform, "Wall West", PrimitiveType.Cube, new Vector3(-3.25f, 0.35f, 2.6f), new Vector3(0.12f, 0.7f, 6.5f), new Color(0.12f, 0.13f, 0.14f, 1f));
            CreatePrimitiveChild(root.transform, "Wall East", PrimitiveType.Cube, new Vector3(3.25f, 0.35f, 2.6f), new Vector3(0.12f, 0.7f, 6.5f), new Color(0.12f, 0.13f, 0.14f, 1f));

            return root.transform;
        }

        private static Transform[] CreateFireSpawnPoints(Transform platformRoot)
        {
            DeleteFireSpawnPointsOutside(platformRoot);

            Vector3[] positions =
            {
                new Vector3(-1.9f, 0f, 4.45f),
                new Vector3(0f, 0f, 4.75f),
                new Vector3(1.9f, 0f, 4.45f),
                new Vector3(-1.65f, 0f, 2.45f),
                new Vector3(1.65f, 0f, 2.45f),
            };

            var points = new List<Transform>();
            for (int i = 0; i < positions.Length; i++)
            {
                string name = $"FireSpawnPoint_{i + 1}";
                Transform point = platformRoot.Find(name);
                if (point == null)
                {
                    point = new GameObject(name).transform;
                    point.SetParent(platformRoot, false);
                }

                point.position = positions[i];
                point.rotation = Quaternion.identity;
                points.Add(point);
            }

            return points.ToArray();
        }

        private static void DeleteFireSpawnPointsOutside(Transform platformRoot)
        {
            Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (Transform sceneTransform in transforms)
            {
                if (sceneTransform == null ||
                    sceneTransform == platformRoot ||
                    sceneTransform.IsChildOf(platformRoot) ||
                    !sceneTransform.name.StartsWith("FireSpawnPoint_", StringComparison.Ordinal))
                {
                    continue;
                }

                Object.DestroyImmediate(sceneTransform.gameObject);
            }
        }

        private static Transform CreateStationVisual(Transform platformRoot)
        {
            GameObject station = FindOrCreate("ExtinguisherStation");
            station.transform.SetParent(platformRoot, false);
            station.transform.position = new Vector3(-2.05f, 0f, 1.2f);
            station.transform.rotation = Quaternion.identity;

            CreatePrimitiveChild(station.transform, "Station Base", PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.04f, 0.7f), new Color(0.05f, 0.75f, 0.9f, 0.85f));
            CreatePrimitiveChild(station.transform, "Station Backplate", PrimitiveType.Cube, new Vector3(0f, 1.05f, 0.22f), new Vector3(0.9f, 2.0f, 0.08f), new Color(0.18f, 0.2f, 0.22f, 1f));
            CreatePrimitiveChild(station.transform, "Station Shelf", PrimitiveType.Cube, new Vector3(0f, 0.84f, -0.1f), new Vector3(0.68f, 0.08f, 0.66f), new Color(0.08f, 0.5f, 0.58f, 1f));
            CreatePrimitiveChild(station.transform, "Station Left Rail", PrimitiveType.Cube, new Vector3(-0.37f, 1.09f, -0.1f), new Vector3(0.06f, 0.26f, 0.34f), new Color(0.08f, 0.5f, 0.58f, 1f));
            CreatePrimitiveChild(station.transform, "Station Right Rail", PrimitiveType.Cube, new Vector3(0.37f, 1.09f, -0.1f), new Vector3(0.06f, 0.26f, 0.34f), new Color(0.08f, 0.5f, 0.58f, 1f));
            CreatePrimitiveChild(station.transform, "Station Front Lip", PrimitiveType.Cube, new Vector3(0f, 0.93f, -0.46f), new Vector3(0.68f, 0.18f, 0.06f), new Color(0.08f, 0.5f, 0.58f, 1f));

            Transform spawn = station.transform.Find("ExtinguisherSpawnPoint");
            if (spawn == null)
            {
                spawn = new GameObject("ExtinguisherSpawnPoint").transform;
                spawn.SetParent(station.transform, false);
            }

            spawn.localPosition = new Vector3(0f, 0.9f, -0.08f);
            spawn.localRotation = Quaternion.Euler(0f, 180f, 0f);
            return spawn;
        }

        private static void CreateHandSphere(Transform parent, string name, Color color)
        {
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find(name);
            GameObject sphere = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * 0.11f;

            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            sphere.GetComponent<Renderer>().sharedMaterial = CreateMaterial(name.Replace(" ", "_"), color);
        }

        private static GameObject CreatePrimitiveChild(Transform parent, string name, PrimitiveType primitiveType, Vector3 position, Vector3 scale, Color color)
        {
            Transform existing = parent.Find(name);
            GameObject gameObject = existing != null ? existing.gameObject : GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = parent.TransformPoint(position);
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = CreateMaterial(name.Replace(" ", "_"), color);
            return gameObject;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            const string folder = "Assets/FireExtinguisherTrainer/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets/FireExtinguisherTrainer", "Materials");
            }

            string path = $"{folder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject FindOrCreate(string name)
        {
            GameObject existing = GameObject.Find(name);
            return existing != null ? existing : new GameObject(name);
        }

        private static T AddComponentIfMissing<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static void RemoveComponentIfPresent<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject != null ? gameObject.GetComponent<T>() : null;
            if (component != null)
            {
                Object.DestroyImmediate(component);
            }
        }

        private static void RemoveComponentIfPresentByTypeName(GameObject gameObject, string componentTypeName)
        {
            if (gameObject == null)
            {
                return;
            }

            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name == componentTypeName)
                {
                    Object.DestroyImmediate(component);
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

        private static void DeleteSceneObject(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            if (target == null)
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

        private static T Require<T>(T value, string label) where T : Object
        {
            if (value == null)
            {
                throw new InvalidOperationException($"Missing required platform scene object: {label}.");
            }

            return value;
        }

        private static void RequireNoSceneObject(string objectName)
        {
            if (GameObject.Find(objectName) != null)
            {
                throw new InvalidOperationException($"{objectName} must not exist in the MRUK Quest demo scene.");
            }
        }

        private static void RequireNoComponentType(GameObject gameObject, string componentTypeName)
        {
            if (gameObject == null)
            {
                return;
            }

            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name == componentTypeName)
                {
                    throw new InvalidOperationException($"{gameObject.name} must not own {componentTypeName} in MRUK mode.");
                }
            }
        }

        private static void RequireNoSceneComponent(string componentTypeName)
        {
            foreach (GameObject sceneObject in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                foreach (Component component in sceneObject.GetComponents<Component>())
                {
                    if (component != null && component.GetType().Name == componentTypeName)
                    {
                        throw new InvalidOperationException($"{componentTypeName} must not exist in the MRUK Quest demo scene.");
                    }
                }
            }
        }

        private static void RequireSerializedReference(Object target, string propertyName, Object expectedValue)
        {
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.ObjectReference ||
                property.objectReferenceValue != expectedValue)
            {
                throw new InvalidOperationException($"{target.name}.{propertyName} is not bound to {expectedValue.name}.");
            }
        }

        private static void RequireSerializedBool(Object target, string propertyName, bool expectedValue)
        {
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.Boolean ||
                property.boolValue != expectedValue)
            {
                throw new InvalidOperationException($"{target.name}.{propertyName} must be {expectedValue}.");
            }
        }

        private static void RequireOculusProjectPassthroughConfig()
        {
            Object config = Require(
                AssetDatabase.LoadAssetAtPath<Object>(OculusProjectConfigPath),
                "OculusProjectConfig asset");
            SerializedObject serializedObject = new SerializedObject(config);
            RequireSerializedIntAtLeast(serializedObject, "anchorSupport", 1, "Oculus project anchor support");
            RequireSerializedIntAtLeast(serializedObject, "sceneSupport", 2, "Oculus project scene support");
            RequireSerializedIntAtLeast(serializedObject, "_insightPassthroughSupport", 1, "Oculus project passthrough support");
            RequireSerializedBool(serializedObject, "isPassthroughCameraAccessEnabled", true, "Oculus passthrough camera access");
        }

        private static bool SetSerializedBoolIfPresent(
            SerializedObject serializedObject,
            string propertyName,
            bool value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            {
                return false;
            }

            if (property.boolValue == value)
            {
                return false;
            }

            property.boolValue = value;
            return true;
        }

        private static bool SetSerializedIntAtLeastIfPresent(
            SerializedObject serializedObject,
            string propertyName,
            int minimumValue)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null ||
                (property.propertyType != SerializedPropertyType.Integer &&
                 property.propertyType != SerializedPropertyType.Enum))
            {
                return false;
            }

            if (property.intValue >= minimumValue)
            {
                return false;
            }

            property.intValue = minimumValue;
            return true;
        }

        private static void RequireSerializedBool(
            SerializedObject serializedObject,
            string propertyName,
            bool expectedValue,
            string label)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.Boolean ||
                property.boolValue != expectedValue)
            {
                throw new InvalidOperationException($"{label} must be {expectedValue}.");
            }
        }

        private static void RequireSerializedIntAtLeast(
            SerializedObject serializedObject,
            string propertyName,
            int minimumValue,
            string label)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null ||
                (property.propertyType != SerializedPropertyType.Integer &&
                 property.propertyType != SerializedPropertyType.Enum) ||
                property.intValue < minimumValue)
            {
                throw new InvalidOperationException($"{label} must be enabled.");
            }
        }

        private static void RequireSerializedFloat(Object target, string propertyName, float expectedValue)
        {
            SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
            if (property == null ||
                property.propertyType != SerializedPropertyType.Float ||
                Mathf.Abs(property.floatValue - expectedValue) > 0.001f)
            {
                throw new InvalidOperationException($"{target.name}.{propertyName} must be {expectedValue}.");
            }
        }

        private static void RequireNonTriggerCollider(GameObject root, string label)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider != null && !collider.isTrigger)
                {
                    return;
                }
            }

            throw new InvalidOperationException($"{label} needs at least one non-trigger collider.");
        }

        private static void RequireUsableSceneExtinguisher(ExtinguisherController extinguisher)
        {
            if (!extinguisher.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException("Station Extinguisher must be active in the scene.");
            }

            Rigidbody rigidbody = Require(
                extinguisher.GetComponent<Rigidbody>(),
                "Station Extinguisher Rigidbody");
            if (rigidbody.isKinematic)
            {
                throw new InvalidOperationException("Station Extinguisher Rigidbody must be non-kinematic.");
            }

            if (!rigidbody.useGravity)
            {
                throw new InvalidOperationException("Station Extinguisher Rigidbody must use gravity.");
            }

            RequireStableRigidbody(rigidbody, "Station Extinguisher Rigidbody");
            RequireStableBodyWidth(extinguisher.transform, "Station Extinguisher Body");
            RequireInteractionPoint(extinguisher.transform, ExtinguisherInteractionDriver.RightGripPoseName);
            RequireInteractionPoint(extinguisher.transform, ExtinguisherInteractionDriver.LeftSupportPoseName);
            RequireInteractionPoint(extinguisher.transform, ExtinguisherInteractionDriver.PinPullZoneName);
            RequireNonTriggerCollider(extinguisher.gameObject, "Station Extinguisher collider");
            Require(extinguisher.GetComponent<ExtinguisherCapacityGauge>(), "Station Extinguisher ExtinguisherCapacityGauge");
            Require(extinguisher.GetComponent<ExtinguisherHoldTracker>(), "Station Extinguisher ExtinguisherHoldTracker");
            Require(extinguisher.GetComponent<Grabbable>(), "Station Extinguisher Grabbable");
            Require(extinguisher.GetComponent<GrabInteractable>(), "Station Extinguisher GrabInteractable");
        }

        private static void RequireStableRigidbody(Rigidbody rigidbody, string label)
        {
            if (Vector3.Distance(GetSerializedCenterOfMass(rigidbody), new Vector3(0f, 0.22f, 0f)) > 0.01f)
            {
                throw new InvalidOperationException($"{label} needs a low center of mass.");
            }

            if (rigidbody.angularDamping < 1.2f)
            {
                throw new InvalidOperationException($"{label} needs higher angular damping.");
            }
        }

        private static Vector3 GetSerializedCenterOfMass(Rigidbody rigidbody)
        {
            SerializedProperty centerOfMass = new SerializedObject(rigidbody).FindProperty("m_CenterOfMass");
            return centerOfMass != null ? centerOfMass.vector3Value : rigidbody.centerOfMass;
        }

        private static void RequireStableBodyWidth(Transform root, string label)
        {
            Transform body = root.Find("Body");
            if (body == null)
            {
                throw new InvalidOperationException($"{label} is missing.");
            }

            if (body.localScale.x < 0.23f || body.localScale.z < 0.23f)
            {
                throw new InvalidOperationException($"{label} needs the widened bottle body scale.");
            }
        }

        private static void RequireInteractionPoint(Transform root, string pointName)
        {
            if (root.Find(pointName) == null)
            {
                throw new InvalidOperationException($"{root.name} is missing {pointName}.");
            }
        }
    }
}
#endif
