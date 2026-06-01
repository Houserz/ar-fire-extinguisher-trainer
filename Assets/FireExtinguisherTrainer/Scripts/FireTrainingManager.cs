using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FireExtinguisherTrainer
{
    public class FireTrainingManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private FireSpawner fireSpawner;
        [SerializeField] private ExtinguisherController extinguisher;
        [SerializeField] private ExtinguisherStation extinguisherStation;
        [SerializeField] private ExtinguisherInteractionDriver interactionDriver;
        [SerializeField] private TrainingHUD hud;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform rayOriginOverride;
        [SerializeField] private bool preferOvrCameraRig = true;

        [Header("Training Rules")]
        [SerializeField] private float aimHoldSeconds = 0.45f;
        [SerializeField] private float squeezeConfirmSeconds = 0.25f;
        [SerializeField] private float requiredSweepDegrees = 18f;
        [SerializeField] private float minimumSafeDistance = 0.75f;
        [SerializeField] private float maximumUsefulDistance = 4f;
        [SerializeField] private int totalExtinguishers = 2;
        [SerializeField] private bool useExtinguisherNozzleRay = true;

        [Header("MR Placement")]
        [SerializeField] private bool waitForSpatialPlacementOnStart = false;
        [SerializeField] private float spatialScanTimeoutSeconds = 4f;

        [Header("Diagnostics")]
        [SerializeField] private bool requireCameraVisibleSpatialObjects = true;
        [SerializeField] private bool logSpatialVisibilityDiagnostics = true;

        [Header("Intro")]
        [SerializeField] private bool showIntroOnFirstStart = true;
        [SerializeField] private float introMinimumSeconds = 2.5f;
        [SerializeField] private float introAutoDismissSeconds = 18f;

#if META_MR_SDK_INSTALLED
        [Header("Meta Input")]
        [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
        [SerializeField] private OVRHand pinchHand;
#endif

        private readonly Dictionary<string, float> mistakeCooldowns = new Dictionary<string, float>();
        private readonly Dictionary<string, int> mistakeCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, string> mistakeLabels = new Dictionary<string, string>();
        private readonly HashSet<int> countedExtinguishers = new HashSet<int>();

        private FireTarget activeFire;
        private PassStep currentStep;
        private TrainingOutcome outcome;
        private string status;
        private float startedAt;
        private float aimHoldTimer;
        private float squeezeTimer;
        private float totalSprayTime;
        private float accurateSprayTime;
        private float totalExtinguisherUsed;
        private float previousYaw;
        private float accumulatedSweepDegrees;
        private int mistakes;
        private int spareExtinguishers;
        private int extinguishersUsed;
        private bool waitingForReplacement;
        private bool usedReplacement;
        private string resultReason;
        private SprayHitQuality currentAimQuality;
        private bool introHasBeenShown;
        private bool introVisible;
        private float introElapsedSeconds;
        private bool waitingForSpatialPlacement;
        private float spatialScanElapsedSeconds;
        private SpatialPlacementSource currentPlacementSource;
        private string currentPlacementMessage;

#if UNITY_EDITOR
        private bool debugInputActive;
        private bool debugRayOverrideActive;
        private Ray debugSprayRay;
        private float debugDeltaTime = -1f;
        private bool debugPullPressed;
        private bool debugSprayHeld;
        private bool debugReplacePressed;
        private bool debugRestartPressed;
#endif

        public TrainingSessionReport CurrentReport => BuildReport();
        public bool IntroVisible => introVisible;
        public bool WaitingForSpatialPlacement => waitingForSpatialPlacement;
        public SpatialPlacementSource CurrentPlacementSource => currentPlacementSource;

        private void Awake()
        {
#if META_MR_SDK_INSTALLED
            BindOvrCameraRigIfAvailable();
#endif

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }

            if (interactionDriver == null)
            {
                interactionDriver = FindFirstObjectByType<ExtinguisherInteractionDriver>();
            }

            interactionDriver?.BindTrainingManager(this);
        }

        private void Start()
        {
            BeginTraining();
            ShowIntroIfNeeded();
        }

        private void Update()
        {
            TickTraining();
        }

        private void TickTraining()
        {
            TickIntroState();

            if (waitingForSpatialPlacement)
            {
                extinguisher?.StopSpray();
                SetActiveFireAimFeedback(SprayHitQuality.Miss, false);
                if (!introVisible)
                {
                    TickSpatialPlacementScan();
                }

                UpdateHud();
                return;
            }

            if (outcome != TrainingOutcome.Running)
            {
                extinguisher?.StopSpray();
                SetActiveFireAimFeedback(SprayHitQuality.Miss, false);
                TryConsumeCompletedPinPull();
                hud?.ShowResult(BuildReport());

                if (RestartPressed())
                {
                    BeginTraining();
                }

                return;
            }

            if (waitingForReplacement)
            {
                extinguisher?.StopSpray();
                SetActiveFireAimFeedback(SprayHitQuality.Miss, false);
                if (extinguisherStation == null && ReplacePressed())
                {
                    ReplaceExtinguisher();
                }

                UpdateHud();
                return;
            }

            if (!HasUsableHeldExtinguisher())
            {
                SetActiveFireAimFeedback(SprayHitQuality.Miss, false);
                UpdateMissingExtinguisherStatus();
                UpdateHud();
                return;
            }

            Ray sprayRay = BuildSprayRay();
            SprayHitQuality aimQuality = activeFire != null
                ? activeFire.EvaluateAim(sprayRay, out _)
                : SprayHitQuality.Miss;
            SetActiveFireAimFeedback(aimQuality, true);

            HandlePassSteps(sprayRay, aimQuality);
            UpdateHud();
        }

        private void ShowIntroIfNeeded()
        {
            if (!showIntroOnFirstStart || introHasBeenShown || hud == null || waitingForSpatialPlacement)
            {
                return;
            }

            introHasBeenShown = true;
            introVisible = true;
            introElapsedSeconds = 0f;
            startedAt = Time.time;
            hud.ShowIntro();
        }

        private void TickIntroState()
        {
            if (!introVisible)
            {
                return;
            }

            introElapsedSeconds += FrameDeltaTime;
            startedAt = Time.time;

            bool minimumMet = introElapsedSeconds >= Mathf.Max(0f, introMinimumSeconds);
            bool autoDismissed = introElapsedSeconds >= Mathf.Max(introMinimumSeconds, introAutoDismissSeconds);
            if (autoDismissed || (minimumMet && IntroDismissPressed()))
            {
                HideIntroAndResetTimer();
            }
        }

        private void HideIntroAndResetTimer()
        {
            introVisible = false;
            introElapsedSeconds = 0f;
            hud?.HideIntro();
            startedAt = Time.time;
        }

        private void TryConsumeCompletedPinPull()
        {
            if (interactionDriver == null)
            {
                return;
            }

            if (!interactionDriver.ConsumePinPullRequested())
            {
                return;
            }

            ExtinguisherController held = interactionDriver.HeldExtinguisher;
            if (held != null && !held.IsPinPulled)
            {
                held.PullPin();
            }
        }

        public void BeginTraining()
        {
            if (TryBeginSpatialPlacementScan())
            {
                return;
            }

            StartTrainingWithFire(fireSpawner != null ? fireSpawner.SpawnRandomFire() : null);
        }

        private bool TryBeginSpatialPlacementScan()
        {
            SpatialTrainingPlacementManager placement = fireSpawner != null ? fireSpawner.SpatialPlacement : null;
            if (!waitForSpatialPlacementOnStart || placement == null)
            {
                return false;
            }

            ResetRunMetrics();
            waitingForSpatialPlacement = true;
            spatialScanElapsedSeconds = 0f;
            activeFire = null;
            currentStep = PassStep.PullPin;
            currentPlacementSource = SpatialPlacementSource.None;
            currentPlacementMessage = "Scanning for Meta room floor data...";
            status = currentPlacementMessage;
            resultReason = status;
            return true;
        }

        private void TickSpatialPlacementScan()
        {
            SpatialTrainingPlacementManager placement = fireSpawner != null ? fireSpawner.SpatialPlacement : null;
            if (placement == null)
            {
                waitingForSpatialPlacement = false;
                currentPlacementMessage = "Spatial placement manager missing; using FireSpawner fixed fallback.";
                StartTrainingWithFire(fireSpawner != null ? fireSpawner.SpawnRandomFire() : null);
                return;
            }

            if (placement.TryGetTrainingLayout(allowFallback: false, out SpatialTrainingLayout detectedLayout))
            {
                StartTrainingWithSpatialLayout(detectedLayout);
                return;
            }

            spatialScanElapsedSeconds += FrameDeltaTime;
            status = string.IsNullOrWhiteSpace(placement.LastPlacementMessage)
                ? "Scanning for Meta room floor data..."
                : placement.LastPlacementMessage;
            resultReason = status;
            if (spatialScanElapsedSeconds < Mathf.Max(0f, spatialScanTimeoutSeconds))
            {
                return;
            }

            if (placement.TryGetFallbackLayout(out SpatialTrainingLayout fallbackLayout))
            {
                StartTrainingWithSpatialLayout(fallbackLayout);
                return;
            }

            waitingForSpatialPlacement = false;
            currentPlacementMessage = "Spatial placement failed and fixed fallback was used.";
            StartTrainingWithFire(fireSpawner != null ? fireSpawner.SpawnRandomFire() : null);
        }

        private void StartTrainingWithSpatialLayout(SpatialTrainingLayout layout)
        {
            waitingForSpatialPlacement = false;
            currentPlacementSource = layout.Source;
            currentPlacementMessage = layout.Message;
            StartTrainingWithFire(fireSpawner != null ? fireSpawner.SpawnFireAt(layout) : null);
        }

        private void StartTrainingWithFire(FireTarget fire)
        {
            waitingForSpatialPlacement = false;
            activeFire = fire;
            activeFire?.ResetFire();

            if (interactionDriver == null)
            {
                interactionDriver = FindFirstObjectByType<ExtinguisherInteractionDriver>();
            }

            interactionDriver?.BindTrainingManager(this);

            ExtinguisherController carriedExtinguisher = GetReusableHeldExtinguisher();
            if (extinguisherStation != null)
            {
                extinguisher = carriedExtinguisher;
                extinguisherStation.BindTrainingManager(this);
                extinguisherStation.EnsureAvailableExtinguisher();
            }
            else
            {
                if (carriedExtinguisher != null)
                {
                    extinguisher = carriedExtinguisher;
                }
                else
                {
                    extinguisher?.ResetExtinguisher();
                }
            }

            currentStep = carriedExtinguisher != null && carriedExtinguisher.IsPinPulled
                ? PassStep.AimAtBase
                : PassStep.PullPin;
            ResetRunMetrics();
            currentPlacementSource = fireSpawner != null
                ? fireSpawner.LastPlacementSource
                : SpatialPlacementSource.None;
            currentPlacementMessage = fireSpawner != null
                ? fireSpawner.LastPlacementMessage
                : currentPlacementMessage;

            status = BuildInitialStatus(carriedExtinguisher);

            if (carriedExtinguisher != null)
            {
                CountExtinguisherUse(carriedExtinguisher);
            }

            string setupFailure = ValidateStartedTrainingObjects();
            if (!string.IsNullOrEmpty(setupFailure))
            {
                FailTraining(setupFailure);
                Debug.LogError(status, this);
            }
        }

        private void ResetRunMetrics()
        {
            outcome = TrainingOutcome.Running;
            resultReason = "Training is running.";
            startedAt = Time.time;
            aimHoldTimer = 0f;
            squeezeTimer = 0f;
            totalSprayTime = 0f;
            accurateSprayTime = 0f;
            totalExtinguisherUsed = 0f;
            mistakes = 0;
            spareExtinguishers = Mathf.Max(0, totalExtinguishers);
            extinguishersUsed = 0;
            waitingForReplacement = false;
            usedReplacement = false;
            accumulatedSweepDegrees = 0f;
            previousYaw = GetRayYaw(BuildSprayRay());
            currentAimQuality = SprayHitQuality.Miss;
            mistakeCooldowns.Clear();
            mistakeCounts.Clear();
            mistakeLabels.Clear();
            countedExtinguishers.Clear();
        }

        private string ValidateStartedTrainingObjects()
        {
            if (activeFire == null)
            {
                return fireSpawner != null && !string.IsNullOrWhiteSpace(fireSpawner.LastPlacementMessage)
                    ? fireSpawner.LastPlacementMessage
                    : "Fire did not spawn: FireTrainingManager has no active fire.";
            }

            ExtinguisherController availableExtinguisher = null;
            if (extinguisherStation != null)
            {
                availableExtinguisher = extinguisherStation.AvailableExtinguisher;
                if (availableExtinguisher == null)
                {
                    return string.IsNullOrWhiteSpace(extinguisherStation.LastStationMessage)
                        ? "Extinguisher did not spawn: station has no available bottle."
                        : extinguisherStation.LastStationMessage;
                }
            }
            else if (extinguisher == null)
            {
                return "Extinguisher did not spawn: no station or direct extinguisher reference is configured.";
            }

            if (currentPlacementSource == SpatialPlacementSource.MetaSceneFloor ||
                currentPlacementSource == SpatialPlacementSource.DetectedPlane ||
                currentPlacementSource == SpatialPlacementSource.Fallback)
            {
                string fireFailure = ValidateSpatialObjectReady("Fire", activeFire.gameObject, activeFire.transform.position);
                if (!string.IsNullOrEmpty(fireFailure))
                {
                    return fireFailure;
                }

                if (extinguisherStation != null)
                {
                    string stationFailure = ValidateSpatialObjectReady(
                        "Extinguisher station",
                        extinguisherStation.gameObject,
                        extinguisherStation.transform.position,
                        availableExtinguisher.transform);
                    if (!string.IsNullOrEmpty(stationFailure))
                    {
                        return stationFailure;
                    }

                    string bottleFailure = ValidateSpatialObjectReady(
                        "Station extinguisher",
                        availableExtinguisher.gameObject,
                        availableExtinguisher.transform.position);
                    if (!string.IsNullOrEmpty(bottleFailure))
                    {
                        return bottleFailure;
                    }
                }
            }

            Debug.Log(
                $"Training objects ready. fire={activeFire.transform.position:F2}, station={(extinguisherStation != null ? extinguisherStation.transform.position.ToString("F2") : "none")}, source={currentPlacementSource}, message={currentPlacementMessage}",
                this);
            return null;
        }

        private string ValidateSpatialObjectReady(
            string label,
            GameObject root,
            Vector3 position,
            Transform ignoredRendererRoot = null)
        {
            if (root == null)
            {
                return $"{label} did not spawn: object reference is missing.";
            }

            if (!root.activeInHierarchy)
            {
                return $"{label} spawned but is inactive. source={currentPlacementSource}.";
            }

            string poseFailure = ValidateSpatialObjectPose(label, position);
            if (!string.IsNullOrEmpty(poseFailure))
            {
                return poseFailure;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return $"{label} spawned but no renderer was found. position={position:F2}, source={currentPlacementSource}.";
            }

            bool hasRenderer = false;
            bool hasEnabledRenderer = false;
            bool hasCameraRenderableLayer = playerCamera == null;
            bool hasStableBoundsRenderer = HasStableSpatialBoundsRenderer(renderers, ignoredRendererRoot);
            int boundsRendererCount = 0;
            Bounds combinedBounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null ||
                    (ignoredRendererRoot != null && renderer.transform.IsChildOf(ignoredRendererRoot)))
                {
                    continue;
                }

                hasRenderer = true;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                hasEnabledRenderer = true;
                if (hasStableBoundsRenderer && !IsStableSpatialBoundsRenderer(renderer))
                {
                    continue;
                }

                boundsRendererCount++;
                Bounds bounds = renderer.bounds;
                if (!IsFinite(bounds.center) || !IsFinite(bounds.extents))
                {
                    return $"{label} spawned but renderer bounds are invalid. position={position:F2}, source={currentPlacementSource}.";
                }

                if (boundsRendererCount == 1)
                {
                    combinedBounds = bounds;
                }
                else
                {
                    combinedBounds.Encapsulate(bounds);
                }

                if (playerCamera != null && CameraCanRenderLayer(renderer.gameObject.layer))
                {
                    hasCameraRenderableLayer = true;
                }
            }

            if (!hasRenderer)
            {
                return $"{label} spawned but no renderer was found. position={position:F2}, source={currentPlacementSource}.";
            }

            if (!hasEnabledRenderer)
            {
                return $"{label} spawned but no enabled renderer was found. position={position:F2}, source={currentPlacementSource}.";
            }

            if (boundsRendererCount == 0)
            {
                return $"{label} spawned but no renderer could be used for visibility bounds. position={position:F2}, source={currentPlacementSource}.";
            }

            if (!hasCameraRenderableLayer)
            {
                return $"{label} spawned but the headset camera culling mask hides its renderer layer. layer={LayerMask.LayerToName(root.layer)}({root.layer}), cullingMask={playerCamera.cullingMask}, position={position:F2}, source={currentPlacementSource}.";
            }

            string cameraFailure = ValidateSpatialObjectCameraVisibility(label, position, combinedBounds, boundsRendererCount);
            if (!string.IsNullOrEmpty(cameraFailure))
            {
                return cameraFailure;
            }

            if (logSpatialVisibilityDiagnostics)
            {
                Debug.Log(
                    $"{label} visibility diagnostic: position={position:F2}, boundsCenter={combinedBounds.center:F2}, boundsSize={combinedBounds.size:F2}, viewport={(playerCamera != null ? playerCamera.WorldToViewportPoint(combinedBounds.center).ToString("F2") : "no-camera")}, boundsRenderers={boundsRendererCount}, source={currentPlacementSource}.",
                    this);
            }

            return null;
        }

        private static bool HasStableSpatialBoundsRenderer(Renderer[] renderers, Transform ignoredRendererRoot)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null ||
                    !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy ||
                    (ignoredRendererRoot != null && renderer.transform.IsChildOf(ignoredRendererRoot)))
                {
                    continue;
                }

                if (IsStableSpatialBoundsRenderer(renderer))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStableSpatialBoundsRenderer(Renderer renderer)
        {
            return renderer != null && !(renderer is ParticleSystemRenderer);
        }

        private string ValidateSpatialObjectPose(string label, Vector3 position)
        {
            if (playerCamera == null)
            {
                return null;
            }

            if (!IsFinite(position))
            {
                return $"{label} spawned at an invalid position. position={position:F2}, source={currentPlacementSource}.";
            }

            Transform reference = playerCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 flatDelta = new Vector3(position.x - reference.position.x, 0f, position.z - reference.position.z);
            float forwardDistance = Vector3.Dot(flatDelta, forward);
            float flatDistance = flatDelta.magnitude;
            if (forwardDistance <= 0.1f)
            {
                return $"{label} spawned behind the headset. position={position:F2}, source={currentPlacementSource}.";
            }

            if (flatDistance > Mathf.Max(maximumUsefulDistance + 2f, 6f))
            {
                return $"{label} spawned too far from the headset. position={position:F2}, distance={flatDistance:F2}m, source={currentPlacementSource}.";
            }

            return null;
        }

        private string ValidateSpatialObjectCameraVisibility(
            string label,
            Vector3 position,
            Bounds bounds,
            int enabledRendererCount)
        {
            if (!requireCameraVisibleSpatialObjects || playerCamera == null)
            {
                return null;
            }

            Vector3 viewportCenter = playerCamera.WorldToViewportPoint(bounds.center);
            if (!IsFinite(viewportCenter))
            {
                return $"{label} spawned but camera viewport coordinates are invalid. position={position:F2}, boundsCenter={bounds.center:F2}, source={currentPlacementSource}.";
            }

            bool inFrontOfCamera = viewportCenter.z > playerCamera.nearClipPlane;
            bool intersectsFrustum = GeometryUtility.TestPlanesAABB(
                GeometryUtility.CalculateFrustumPlanes(playerCamera),
                bounds);
            if (!inFrontOfCamera || !intersectsFrustum)
            {
                return $"{label} spawned but is outside the headset camera view. position={position:F2}, boundsCenter={bounds.center:F2}, boundsSize={bounds.size:F2}, viewport={viewportCenter:F2}, inFront={inFrontOfCamera}, inFrustum={intersectsFrustum}, renderers={enabledRendererCount}, source={currentPlacementSource}.";
            }

            return null;
        }

        private bool CameraCanRenderLayer(int layer)
        {
            if (playerCamera == null || layer < 0 || layer > 31)
            {
                return true;
            }

            return (playerCamera.cullingMask & (1 << layer)) != 0;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private ExtinguisherController GetReusableHeldExtinguisher()
        {
            ExtinguisherController driverHeld = interactionDriver != null
                ? interactionDriver.HeldExtinguisher
                : null;

            if (driverHeld != null && driverHeld.IsHeld && !driverHeld.IsEmpty)
            {
                return driverHeld;
            }

            if (extinguisher != null && extinguisher.IsHeld && !extinguisher.IsEmpty)
            {
                return extinguisher;
            }

            return null;
        }

        private string BuildInitialStatus(ExtinguisherController carriedExtinguisher)
        {
            string placementPrefix = currentPlacementSource == SpatialPlacementSource.MetaSceneFloor ||
                                     currentPlacementSource == SpatialPlacementSource.DetectedPlane
                ? "Horizontal surface locked. "
                : currentPlacementSource == SpatialPlacementSource.Fallback
                    ? "Using fallback placement. "
                    : string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPlacementMessage))
            {
                placementPrefix = currentPlacementMessage + " ";
            }

            if (carriedExtinguisher != null)
            {
                return placementPrefix + (carriedExtinguisher.IsPinPulled
                    ? "New fire ready. Keep aiming at the base and squeeze to spray."
                    : "Extinguisher ready. Grab the top safety pin with the left hand and pull it away.");
            }

            return placementPrefix + (extinguisherStation != null
                ? "Pick up the extinguisher from the station with the right grip."
                : "Grab the top safety pin with the left hand and pull it away. Spray: trigger/Space.");
        }

        public void RegisterHeldExtinguisher(ExtinguisherController heldExtinguisher)
        {
            if (heldExtinguisher == null || outcome != TrainingOutcome.Running)
            {
                return;
            }

            CountExtinguisherUse(heldExtinguisher);

            if (extinguisher != heldExtinguisher)
            {
                extinguisher?.StopSpray();
                extinguisher = heldExtinguisher;
            }

            if (heldExtinguisher.IsEmpty)
            {
                waitingForReplacement = true;
                status = "This extinguisher is empty. Drop it and return to the station.";
                return;
            }

            if (waitingForReplacement)
            {
                waitingForReplacement = false;
                currentStep = PassStep.PullPin;
                aimHoldTimer = 0f;
                squeezeTimer = 0f;
                accumulatedSweepDegrees = 0f;
                status = "Replacement picked up. Grab the top safety pin with the left hand and pull it away.";
                resultReason = "Replacement extinguisher picked up.";
                return;
            }

            if (currentStep == PassStep.PullPin)
            {
                status = "Extinguisher ready. Grab the top safety pin with the left hand and pull it away.";
            }
        }

        public void ReleaseHeldExtinguisher(ExtinguisherController releasedExtinguisher)
        {
            if (releasedExtinguisher == null || releasedExtinguisher != extinguisher)
            {
                return;
            }

            releasedExtinguisher.StopSpray();
            if (releasedExtinguisher.IsEmpty && outcome == TrainingOutcome.Running)
            {
                waitingForReplacement = true;
                status = "Empty extinguisher dropped. Go to the station and pick up a full one.";
                resultReason = "Waiting for a replacement extinguisher.";
                extinguisherStation?.RequestReplacementAfterDrop();
            }
            else if (outcome == TrainingOutcome.Running)
            {
                status = "Pick up the extinguisher to continue training.";
            }

            if (!releasedExtinguisher.IsHeld)
            {
                extinguisher = null;
            }
        }

        private void HandlePassSteps(Ray sprayRay, SprayHitQuality aimQuality)
        {
            bool sprayHeld = SprayHeld();

            if (currentStep == PassStep.PullPin)
            {
                if (sprayHeld)
                {
                    extinguisher?.StopSpray();
                    RegisterMistake("spray-before-pin", "Pull the safety pin before squeezing the handle.");
                }

                if (PullPressed())
                {
                    extinguisher?.PullPin();
                    currentStep = PassStep.AimAtBase;
                    status = "Aim at the base of the fire.";
                }
                return;
            }

            if (currentStep == PassStep.AimAtBase)
            {
                bool earlySprayAttempt = sprayHeld;
                if (sprayHeld)
                {
                    RegisterMistake("spray-before-aim", "Aim at the base before spraying.");
                    RunSprayFrame(sprayRay, aimQuality);
                    if (outcome != TrainingOutcome.Running || waitingForReplacement)
                    {
                        return;
                    }
                }

                if (aimQuality == SprayHitQuality.BaseHit)
                {
                    aimHoldTimer += FrameDeltaTime;
                    if (!earlySprayAttempt)
                    {
                        status = "Good aim. Keep the nozzle pointed at the base.";
                    }

                    if (aimHoldTimer >= aimHoldSeconds)
                    {
                        currentStep = PassStep.SqueezeHandle;
                        status = earlySprayAttempt
                            ? "Keep squeezing while aimed at the base."
                            : "Squeeze and hold to spray.";
                    }
                }
                else
                {
                    aimHoldTimer = 0f;
                    if (!earlySprayAttempt)
                    {
                        status = aimQuality == SprayHitQuality.WrongArea
                            ? "Aim lower. Fire extinguishers work best at the base of the flames."
                            : "Find the fire and aim at the base.";
                    }
                }

                if (!sprayHeld)
                {
                    extinguisher?.StopSpray();
                }

                return;
            }

            if (currentStep == PassStep.SqueezeHandle)
            {
                if (sprayHeld)
                {
                    if (aimQuality != SprayHitQuality.BaseHit)
                    {
                        squeezeTimer = 0f;
                        RegisterMistake(
                            aimQuality == SprayHitQuality.WrongArea ? "wrong-area-before-squeeze" : "miss-before-squeeze",
                            aimQuality == SprayHitQuality.WrongArea
                                ? "Aim at the base before squeezing."
                                : "Keep the nozzle on the fire base before squeezing.");
                        RunSprayFrame(sprayRay, aimQuality);
                        return;
                    }

                    squeezeTimer += FrameDeltaTime;
                    RunSprayFrame(sprayRay, aimQuality);
                    if (outcome != TrainingOutcome.Running || waitingForReplacement)
                    {
                        return;
                    }

                    if (squeezeTimer >= squeezeConfirmSeconds)
                    {
                        currentStep = PassStep.SweepSideToSide;
                        previousYaw = GetRayYaw(sprayRay);
                        status = "Sweep side to side while staying aimed at the base.";
                    }
                }
                else
                {
                    extinguisher?.StopSpray();
                    status = "Squeeze and hold to start spraying.";
                }

                return;
            }

            if (currentStep == PassStep.SweepSideToSide)
            {
                if (sprayHeld)
                {
                    TrackSweep(sprayRay);
                    RunSprayFrame(sprayRay, aimQuality);
                }
                else
                {
                    extinguisher?.StopSpray();
                    status = "Keep squeezing and sweep across the base of the fire.";
                }
            }
        }

        private void RunSprayFrame(Ray sprayRay, SprayHitQuality aimQuality)
        {
            if (extinguisher == null || activeFire == null)
            {
                return;
            }

            float deltaTime = FrameDeltaTime;
            if (!extinguisher.ConsumeSpray(deltaTime))
            {
                HandleEmptyExtinguisher();
                return;
            }

            totalSprayTime += deltaTime;
            totalExtinguisherUsed += deltaTime;

            float distanceToFire = Vector3.Distance(sprayRay.origin, activeFire.BaseTarget.position);
            if (distanceToFire < minimumSafeDistance)
            {
                RegisterMistake("too-close", "Back up to a safer distance.");
            }
            else if (distanceToFire > maximumUsefulDistance)
            {
                RegisterMistake("too-far", "Move closer so the spray can reach the fire.");
            }

            if (activeFire.ApplySpray(sprayRay, deltaTime, out SprayHitQuality appliedQuality))
            {
                accurateSprayTime += deltaTime;
                status = currentStep == PassStep.SweepSideToSide && accumulatedSweepDegrees < requiredSweepDegrees
                    ? "Fire is shrinking. Sweep side to side."
                    : "Good spray. Keep aiming at the base.";
            }
            else if (aimQuality == SprayHitQuality.WrongArea || appliedQuality == SprayHitQuality.WrongArea)
            {
                RegisterMistake("wrong-area", "Aim at the base, not the top of the flames.");
            }
            else
            {
                status = "Spray is missing the fire base.";
            }

            if (activeFire.IsExtinguished)
            {
                if (accumulatedSweepDegrees < requiredSweepDegrees)
                {
                    RegisterMistake("not-enough-sweep", "Use a clearer side-to-side sweep next time.");
                }

                currentStep = PassStep.Completed;
                CompleteTraining(accumulatedSweepDegrees < requiredSweepDegrees
                    ? "Fire extinguished, but the sweep motion was limited."
                    : "Fire extinguished with the PASS sequence.");
                extinguisher.StopSpray();
            }
            else if (!extinguisher.HasCapacity)
            {
                HandleEmptyExtinguisher();
            }
        }

        private void TrackSweep(Ray sprayRay)
        {
            float yaw = GetRayYaw(sprayRay);
            float deltaYaw = Mathf.Abs(Mathf.DeltaAngle(previousYaw, yaw));
            if (deltaYaw > 0.1f && deltaYaw < 25f)
            {
                accumulatedSweepDegrees += deltaYaw;
            }

            previousYaw = yaw;
        }

        private void HandleEmptyExtinguisher()
        {
            extinguisher?.StopSpray();

            if (activeFire != null && activeFire.IsExtinguished)
            {
                CompleteTraining("Fire extinguished as the extinguisher emptied.");
                return;
            }

            RegisterMistake("empty-extinguisher", "Extinguisher ran empty before the fire was out.");

            if (extinguisherStation != null)
            {
                waitingForReplacement = true;
                extinguisherStation.RequestReplacementAfterDrop();
                status = "Extinguisher is empty. Drop it, return to the station, and pick up a full one.";
                resultReason = "Empty extinguisher must be replaced at the station.";
                return;
            }

            if (spareExtinguishers > 0)
            {
                waitingForReplacement = true;
                status = "Extinguisher is empty. Press B / R to switch to a full extinguisher.";
                resultReason = "Extinguisher empty; replacement required to continue.";
            }
            else
            {
                FailTraining("No extinguishers left before the fire was extinguished.");
            }
        }

        private void ReplaceExtinguisher()
        {
            if (spareExtinguishers <= 0 || extinguisher == null)
            {
                FailTraining("Replacement was requested, but no full extinguishers are available.");
                return;
            }

            spareExtinguishers--;
            extinguishersUsed++;
            usedReplacement = true;
            waitingForReplacement = false;
            extinguisher.ReplaceWithFullExtinguisher();
            currentStep = PassStep.PullPin;
            aimHoldTimer = 0f;
            squeezeTimer = 0f;
            accumulatedSweepDegrees = 0f;
            status = "Replacement ready. Grab the top safety pin with the left hand and pull it away.";
            resultReason = "Replacement extinguisher loaded.";
        }

        private void SetActiveFireAimFeedback(SprayHitQuality quality, bool active)
        {
            currentAimQuality = active ? quality : SprayHitQuality.Miss;
            activeFire?.SetAimFeedback(quality, active);
        }

        private bool HasUsableHeldExtinguisher()
        {
            return extinguisher != null && extinguisher.IsHeld && !extinguisher.IsEmpty;
        }

        private void UpdateMissingExtinguisherStatus()
        {
            if (extinguisher != null && extinguisher.IsHeld && extinguisher.IsEmpty)
            {
                waitingForReplacement = true;
                extinguisherStation?.RequestReplacementAfterDrop();
                status = "Extinguisher is empty. Drop it and return to the station.";
                return;
            }

            if (waitingForReplacement)
            {
                status = extinguisherStation != null
                    ? "Go to the station and pick up a full extinguisher."
                    : "Switch to a full extinguisher.";
                return;
            }

            status = extinguisherStation != null
                ? "Pick up the extinguisher from the station with the right grip."
                : "Pick up the extinguisher before starting.";
        }

        private void CountExtinguisherUse(ExtinguisherController usedExtinguisher)
        {
            int id = usedExtinguisher.GetInstanceID();
            if (!countedExtinguishers.Add(id))
            {
                return;
            }

            extinguishersUsed++;
            usedReplacement = extinguishersUsed > 1;
            if (totalExtinguishers > 0)
            {
                spareExtinguishers = Mathf.Max(0, totalExtinguishers - extinguishersUsed);
            }
        }

        private void CompleteTraining(string reason)
        {
            outcome = TrainingOutcome.Success;
            resultReason = reason;
            status = reason;
        }

        private void FailTraining(string reason)
        {
            outcome = TrainingOutcome.Failed;
            resultReason = reason;
            status = reason + " Press A / Enter to restart.";
        }

        private float FrameDeltaTime
        {
            get
            {
#if UNITY_EDITOR
                if (debugInputActive && debugDeltaTime > 0f)
                {
                    return debugDeltaTime;
                }

                if (debugRayOverrideActive && debugDeltaTime > 0f)
                {
                    return debugDeltaTime;
                }
#endif

                return Time.deltaTime;
            }
        }

#if UNITY_EDITOR
        public void DebugBeginTraining(
            FireTarget fire,
            ExtinguisherController controller,
            TrainingHUD trainingHud = null,
            Camera camera = null)
        {
            extinguisher = controller;
            hud = trainingHud;
            if (camera != null)
            {
                playerCamera = camera;
            }

            StartTrainingWithFire(fire);
            controller?.MarkPickedUp(null, false);
            RegisterHeldExtinguisher(controller);
        }

        public void DebugBeginTrainingWithStation(
            FireTarget fire,
            ExtinguisherStation station,
            TrainingHUD trainingHud = null,
            Camera camera = null)
        {
            extinguisherStation = station;
            extinguisher = null;
            hud = trainingHud;
            if (camera != null)
            {
                playerCamera = camera;
            }

            StartTrainingWithFire(fire);
        }

        public void DebugSetInteractionDriver(ExtinguisherInteractionDriver driver)
        {
            interactionDriver = driver;
            interactionDriver?.BindTrainingManager(this);
        }

        public void DebugShowIntro()
        {
            introHasBeenShown = false;
            ShowIntroIfNeeded();
        }

        public void DebugRunFrame(
            Ray sprayRay,
            float deltaTime,
            bool pullPressed = false,
            bool sprayHeld = false,
            bool replacePressed = false,
            bool restartPressed = false)
        {
            debugInputActive = true;
            debugRayOverrideActive = true;
            debugSprayRay = sprayRay;
            debugDeltaTime = Mathf.Max(0.0001f, deltaTime);
            debugPullPressed = pullPressed;
            debugSprayHeld = sprayHeld;
            debugReplacePressed = replacePressed;
            debugRestartPressed = restartPressed;

            TickTraining();

            debugInputActive = false;
            debugRayOverrideActive = false;
            debugDeltaTime = -1f;
            debugPullPressed = false;
            debugSprayHeld = false;
            debugReplacePressed = false;
            debugRestartPressed = false;
        }

        public void DebugRunFrameWithInteraction(Ray sprayRay, float deltaTime)
        {
            debugRayOverrideActive = true;
            debugSprayRay = sprayRay;
            debugDeltaTime = Mathf.Max(0.0001f, deltaTime);

            TickTraining();

            debugRayOverrideActive = false;
            debugDeltaTime = -1f;
        }
#endif

        private Ray BuildSprayRay()
        {
#if UNITY_EDITOR
            if (debugRayOverrideActive)
            {
                return debugSprayRay;
            }

            if (!UnityEngine.XR.XRSettings.isDeviceActive && playerCamera != null && Mouse.current != null)
            {
                return playerCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            }
#endif

            if (useExtinguisherNozzleRay && extinguisher != null && extinguisher.Nozzle != null)
            {
                if (interactionDriver != null && interactionDriver.HeldExtinguisher == extinguisher)
                {
                    return interactionDriver.BuildSprayRay();
                }

                Transform nozzle = extinguisher.Nozzle;
                return new Ray(nozzle.position, nozzle.forward);
            }

            Transform rayTransform = GetRayTransform();
            return new Ray(rayTransform.position, rayTransform.forward);
        }

        private Transform GetRayTransform()
        {
#if META_MR_SDK_INSTALLED
            if (preferOvrCameraRig)
            {
                BindOvrCameraRigIfAvailable();
            }

            if (pinchHand != null)
            {
                Transform handRay = pinchHand.GetPointerRayTransform();
                if (handRay != null)
                {
                    return handRay;
                }
            }
#endif

            if (rayOriginOverride != null)
            {
                return rayOriginOverride;
            }

            if (playerCamera != null)
            {
                return playerCamera.transform;
            }

            return transform;
        }

#if META_MR_SDK_INSTALLED
        private void BindOvrCameraRigIfAvailable()
        {
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null)
            {
                return;
            }

            if (rig.centerEyeAnchor != null)
            {
                Camera centerEyeCamera = rig.centerEyeAnchor.GetComponent<Camera>();
                if (centerEyeCamera != null)
                {
                    playerCamera = centerEyeCamera;
                }
            }

            if (rig.rightControllerAnchor != null)
            {
                rayOriginOverride = rig.rightControllerAnchor;
            }
        }
#endif

        private bool PullPressed()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugPullPressed;
            }
#endif

            bool interactionPressed = interactionDriver != null && interactionDriver.ConsumePinPullRequested();
            return interactionPressed;
        }

        private bool SprayHeld()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugSprayHeld;
            }
#endif

            bool keyboardHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
            bool mouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool gamepadHeld = Gamepad.current != null && Gamepad.current.rightTrigger.ReadValue() > 0.35f;
            bool interactionHeld = interactionDriver != null && interactionDriver.SprayHeld;

#if META_MR_SDK_INSTALLED
            bool controllerHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.35f;
            bool pinchHeld = pinchHand != null && pinchHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
#else
            bool controllerHeld = false;
            bool pinchHeld = false;
#endif

            return interactionHeld || keyboardHeld || mouseHeld || gamepadHeld || controllerHeld || pinchHeld;
        }

        private bool ReplacePressed()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugReplacePressed;
            }
#endif

            bool keyboardPressed = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
            bool gamepadPressed = Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame;

#if META_MR_SDK_INSTALLED
            bool metaPressed = OVRInput.GetDown(OVRInput.Button.Two, controller);
#else
            bool metaPressed = false;
#endif

            return keyboardPressed || gamepadPressed || metaPressed;
        }

        private bool RestartPressed()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugRestartPressed;
            }
#endif

            bool keyboardPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            bool gamepadPressed = Gamepad.current != null &&
                (Gamepad.current.startButton.wasPressedThisFrame ||
                 Gamepad.current.buttonSouth.wasPressedThisFrame);

#if META_MR_SDK_INSTALLED
            bool metaPressed =
                OVRInput.GetDown(OVRInput.Button.One, controller) ||
                OVRInput.GetDown(OVRInput.Button.Two, controller);
#else
            bool metaPressed = false;
#endif

            return keyboardPressed || gamepadPressed || metaPressed;
        }

        private bool IntroDismissPressed()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugRestartPressed || debugSprayHeld;
            }
#endif

            bool keyboardPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            bool gamepadPressed = Gamepad.current != null &&
                (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                 Gamepad.current.rightTrigger.ReadValue() > 0.35f);

#if META_MR_SDK_INSTALLED
            bool metaPressed =
                OVRInput.GetDown(OVRInput.Button.One, controller) ||
                OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller) > 0.35f;
#else
            bool metaPressed = false;
#endif

            return keyboardPressed || gamepadPressed || metaPressed;
        }

        private void RegisterMistake(string id, string message)
        {
            if (mistakeCooldowns.TryGetValue(id, out float nextAllowedTime) && Time.time < nextAllowedTime)
            {
                status = message;
                return;
            }

            mistakeCooldowns[id] = Time.time + 1.25f;
            mistakes++;
            mistakeCounts.TryGetValue(id, out int count);
            mistakeCounts[id] = count + 1;
            mistakeLabels[id] = GetMistakeLabel(id);
            status = message;
        }

        private void UpdateHud()
        {
            hud?.SetRunning(BuildReport());
        }

        private TrainingSessionReport BuildReport()
        {
            return new TrainingSessionReport
            {
                Outcome = outcome,
                CurrentStep = currentStep,
                Status = status,
                InstructionText = BuildInstructionText(),
                ResultReason = string.IsNullOrEmpty(resultReason) ? status : resultReason,
                MistakeBreakdown = BuildMistakeBreakdown(),
                Mistakes = mistakes,
                ElapsedSeconds = Mathf.Max(0f, Time.time - startedAt),
                AimingAccuracy01 = GetAccuracy01(),
                TotalSprayTimeSeconds = totalSprayTime,
                AccurateSprayTimeSeconds = accurateSprayTime,
                ExtinguisherUsedSeconds = totalExtinguisherUsed,
                ExtinguishersUsed = extinguishersUsed,
                SpareExtinguishers = spareExtinguishers,
                FireHealth01 = activeFire != null ? activeFire.Health01 : 0f,
                ExtinguisherCapacity01 = extinguisher != null ? extinguisher.Capacity01 : 0f,
                CurrentAimQuality = currentAimQuality,
                SweepDegrees = accumulatedSweepDegrees,
                UsedReplacement = usedReplacement,
                WaitingForReplacement = waitingForReplacement,
                HasHeldExtinguisher = extinguisher != null && extinguisher.IsHeld,
                HeldExtinguisherIsEmpty = extinguisher != null && extinguisher.IsEmpty,
                NeedsExtinguisherPickup = outcome == TrainingOutcome.Running && !waitingForSpatialPlacement && !HasUsableHeldExtinguisher(),
                WaitingForSpatialPlacement = waitingForSpatialPlacement,
                PlacementSource = currentPlacementSource,
            };
        }

        private string BuildInstructionText()
        {
            if (waitingForSpatialPlacement)
            {
                return string.IsNullOrWhiteSpace(status)
                    ? "Scanning for Meta room floor data..."
                    : status;
            }

            if (outcome != TrainingOutcome.Running)
            {
                return "Review the result, then press A or Enter to restart.";
            }

            if (waitingForReplacement)
            {
                return extinguisherStation != null
                    ? "Drop the empty bottle and pick up the full replacement at the station."
                    : "Switch to a full extinguisher before continuing.";
            }

            if (!HasUsableHeldExtinguisher())
            {
                return "Pick up the extinguisher with the right grip.";
            }

            switch (currentStep)
            {
                case PassStep.PullPin:
                    return "Use the left grip on the yellow ring and pull it away.";
                case PassStep.AimAtBase:
                    return currentAimQuality == SprayHitQuality.WrongArea
                        ? "Aim lower until the base target turns green."
                        : "Point the nozzle at the base of the fire.";
                case PassStep.SqueezeHandle:
                    return "Hold the trigger while staying on the green base target.";
                case PassStep.SweepSideToSide:
                    return "Sweep side to side across the base while spraying.";
                case PassStep.Completed:
                    return "PASS sequence complete.";
                default:
                    return "Follow the PASS steps shown on the checklist.";
            }
        }

        private string BuildMistakeBreakdown()
        {
            if (mistakeCounts.Count == 0)
            {
                return "No recorded mistakes";
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> mistake in mistakeCounts)
            {
                string label = mistakeLabels.TryGetValue(mistake.Key, out string storedLabel)
                    ? storedLabel
                    : GetMistakeLabel(mistake.Key);
                parts.Add($"{label}: {mistake.Value}");
            }

            return string.Join(" | ", parts);
        }

        private static string GetMistakeLabel(string id)
        {
            switch (id)
            {
                case "spray-before-pin":
                case "spray-before-aim":
                    return "Early spray";
                case "wrong-area-before-squeeze":
                case "wrong-area":
                    return "Wrong aim";
                case "miss-before-squeeze":
                    return "Missed base";
                case "too-close":
                    return "Too close";
                case "too-far":
                    return "Too far";
                case "not-enough-sweep":
                    return "Limited sweep";
                case "empty-extinguisher":
                    return "Empty extinguisher";
                default:
                    return id;
            }
        }

        private float GetAccuracy01()
        {
            return totalSprayTime <= 0f ? 0f : Mathf.Clamp01(accurateSprayTime / totalSprayTime);
        }

        private static float GetRayYaw(Ray ray)
        {
            Vector3 flatDirection = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
            if (flatDirection.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Mathf.Atan2(flatDirection.x, flatDirection.z) * Mathf.Rad2Deg;
        }
    }
}
