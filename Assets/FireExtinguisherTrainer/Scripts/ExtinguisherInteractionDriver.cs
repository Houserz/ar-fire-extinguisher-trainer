using System.Collections.Generic;
using UnityEngine;

namespace FireExtinguisherTrainer
{
    [DisallowMultipleComponent]
    public class ExtinguisherInteractionDriver : MonoBehaviour
    {
        public const string RightGripPoseName = "RightGripPose";
        public const string LeftSupportPoseName = "LeftSupportPose";
        public const string PinPullZoneName = "PinPullZone";

        [Header("Scene References")]
        [SerializeField] private FireTrainingManager trainingManager;
        [SerializeField] private ExtinguisherStation station;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private Transform leftHandAnchor;
        [SerializeField] private Transform playerCollisionRoot;

        [Header("Pickup")]
        [SerializeField] private float pickupRadius = 0.75f;
        [SerializeField] private LayerMask grabbableLayers = ~0;

        [Header("Held Pose")]
        [SerializeField] private Vector3 rightHandRotationOffsetEuler = Vector3.zero;
        [SerializeField] private float leftSupportPickupRadius = 0.34f;
        [SerializeField] private float positionFollowStrength = 45f;
        [SerializeField] private float rotationFollowStrength = 22f;
        [SerializeField] private float maxHeldLinearVelocity = 12f;
        [SerializeField] private float maxHeldAngularVelocity = 18f;

        [Header("Pin")]
        [SerializeField] private float pinPullRadius = 0.35f;
        [SerializeField] private float pinPullTravelDistance = 0.12f;
        [SerializeField] private float pinReleaseDistanceFromZone = 0.2f;

        [Header("Throw")]
        [SerializeField] private float throwVelocityMultiplier = 1f;
        [SerializeField] private float maxThrowVelocity = 5f;
        [SerializeField] private float maxThrowAngularVelocity = 16f;

        [Header("Debug")]
        [SerializeField] private bool debugLogging;
        [SerializeField] private float debugLogInterval = 0.5f;

        private readonly Collider[] overlapResults = new Collider[24];

        private ExtinguisherController heldExtinguisher;
        private Rigidbody heldBody;
        private Transform heldRightGripPose;
        private Transform heldLeftSupportPose;
        private Transform heldPinPullZone;
        private bool leftSupportActive;
        private bool pinDragActive;
        private bool pinPullQueued;
        private bool previousRightGripHeld;
        private bool previousLeftGripHeld;
        private bool previousLeftNearPin;
        private Vector3 pinDragStartPosition;
        private Vector3 previousRightHandPosition;
        private Quaternion previousRightHandRotation = Quaternion.identity;
        private Vector3 rightHandVelocity;
        private Vector3 rightHandAngularVelocity;
        private float nextDebugLogTime;
        private float lastRightGripValue;
        private int lastOverlapCount;
        private float lastNearestDistance = -1f;
        private string lastGrabStatus = "Idle.";
        private readonly List<CollisionPair> ignoredPlayerCollisionPairs = new List<CollisionPair>();

#if UNITY_EDITOR
        private bool debugInputActive;
        private bool debugRightGripHeld;
        private bool debugLeftGripHeld;
        private bool debugSprayHeld;
#endif

        public bool IsHeld => heldExtinguisher != null;
        public bool IsSupportedByLeftHand => leftSupportActive;
        public bool PinPullRequestedThisFrame => pinPullQueued;
        public bool SprayHeld => IsHeld && ReadSprayHeld();
        public ExtinguisherController HeldExtinguisher => heldExtinguisher;
        public float LastRightGripValue => lastRightGripValue;
        public int LastOverlapCount => lastOverlapCount;
        public float LastNearestDistance => lastNearestDistance;
        public string LastGrabStatus => lastGrabStatus;

        private void Awake()
        {
            CacheRightHandPose();
        }

        private void Update()
        {
            ProcessInputFrame();
            LogDebugState();
        }

        private void FixedUpdate()
        {
            float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            UpdateRightHandMotion(deltaTime);

            if (heldBody != null)
            {
                DriveHeldBody(deltaTime);
            }
        }

        public void Configure(
            FireTrainingManager manager,
            ExtinguisherStation owningStation,
            Transform rightHand,
            Transform leftHand,
            Transform playerRoot = null)
        {
            trainingManager = manager;
            station = owningStation;
            rightHandAnchor = rightHand;
            leftHandAnchor = leftHand;
            playerCollisionRoot = playerRoot;
            CacheRightHandPose();
        }

        public void SetPlayerCollisionRoot(Transform playerRoot)
        {
            playerCollisionRoot = playerRoot;
        }

        public void BindTrainingManager(FireTrainingManager manager)
        {
            trainingManager = manager;
        }

        public bool ConsumePinPullRequested()
        {
            bool wasQueued = pinPullQueued;
            pinPullQueued = false;
            return wasQueued;
        }

        public Ray BuildSprayRay()
        {
            if (heldExtinguisher != null && heldExtinguisher.Nozzle != null)
            {
                Transform nozzle = heldExtinguisher.Nozzle;
                return new Ray(nozzle.position, nozzle.forward);
            }

            Transform fallback = rightHandAnchor != null ? rightHandAnchor : transform;
            return new Ray(fallback.position, fallback.forward);
        }

        public bool DebugTryGrab(ExtinguisherController explicitTarget = null)
        {
            return explicitTarget != null ? TryGrab(explicitTarget) : TryGrabNearest();
        }

        public void DebugRelease(Vector3 releaseVelocity)
        {
            rightHandVelocity = releaseVelocity;
            ReleaseHeld();
        }

#if UNITY_EDITOR
        public void DebugSetInputState(
            bool rightGripHeld,
            bool leftGripHeld = false,
            bool sprayHeld = false,
            bool pullPressed = false)
        {
            debugInputActive = true;
            debugRightGripHeld = rightGripHeld;
            debugLeftGripHeld = leftGripHeld;
            debugSprayHeld = sprayHeld;
            ProcessInputFrame();
        }

        public void DebugClearInputState()
        {
            debugInputActive = false;
            debugRightGripHeld = false;
            debugLeftGripHeld = false;
            debugSprayHeld = false;
        }

        public void DebugDriveHeldPose(float deltaTime)
        {
            float safeDeltaTime = Mathf.Max(deltaTime, 0.0001f);
            UpdateRightHandMotion(safeDeltaTime);
            DriveHeldBody(safeDeltaTime);
        }

        public void DebugSnapHeldPose()
        {
            if (heldBody == null || heldExtinguisher == null)
            {
                return;
            }

            ComputeTargetPose(heldExtinguisher, out Vector3 targetPosition, out Quaternion targetRotation);
            heldBody.position = targetPosition;
            heldBody.rotation = targetRotation;
            heldExtinguisher.transform.SetPositionAndRotation(targetPosition, targetRotation);
            heldBody.linearVelocity = Vector3.zero;
            heldBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
        }
#endif

        private void ProcessInputFrame()
        {
            lastRightGripValue = QuestExtinguisherInput.RightGripValue();

            bool rightGripHeld = ReadRightGripHeld();
            bool leftGripHeld = ReadLeftGripHeld();

            if (rightGripHeld && heldExtinguisher == null)
            {
                TryGrabNearest();
            }
            else if (!rightGripHeld && heldExtinguisher != null)
            {
                ReleaseHeld();
            }

            if (heldExtinguisher != null)
            {
                UpdateLeftHandInteractions(leftGripHeld);
            }
            else
            {
                leftSupportActive = false;
                previousLeftNearPin = false;
            }

            previousRightGripHeld = rightGripHeld;
            previousLeftGripHeld = leftGripHeld;

        }

        private void UpdateLeftHandInteractions(bool leftGripHeld)
        {
            bool leftNearPin = IsLeftHandNear(heldPinPullZone, pinPullRadius);

            if (heldExtinguisher.IsPinPulled)
            {
                CancelActivePinDrag();
                UpdateLeftHandSupport(leftGripHeld);
                previousLeftNearPin = leftNearPin;
                return;
            }

            UpdateSafetyPinDrag(leftGripHeld, leftNearPin);
            UpdateLeftHandSupport(leftGripHeld);
            previousLeftNearPin = leftNearPin;
        }

        private void UpdateSafetyPinDrag(bool leftGripHeld, bool leftNearPin)
        {
            if (pinDragActive)
            {
                if (!leftGripHeld || leftHandAnchor == null)
                {
                    CancelActivePinDrag();
                    return;
                }

                Vector3 leftHandPosition = leftHandAnchor.position;
                heldExtinguisher.UpdateSafetyPinDrag(leftHandPosition);

                float dragDistance = Vector3.Distance(leftHandPosition, pinDragStartPosition);
                float distanceFromPinZone = heldPinPullZone != null
                    ? Vector3.Distance(leftHandPosition, heldPinPullZone.position)
                    : dragDistance;

                if (dragDistance >= pinPullTravelDistance &&
                    distanceFromPinZone >= pinReleaseDistanceFromZone)
                {
                    pinDragActive = false;
                    pinPullQueued = true;
                }

                return;
            }

            if (!pinPullQueued && leftGripHeld && leftNearPin && leftHandAnchor != null)
            {
                pinDragActive = true;
                pinDragStartPosition = leftHandAnchor.position;
                heldExtinguisher.BeginSafetyPinDrag(pinDragStartPosition);
            }
        }

        private void UpdateLeftHandSupport(bool leftGripHeld)
        {
            if (!leftGripHeld)
            {
                leftSupportActive = false;
            }
            else if (!leftSupportActive && IsLeftHandNear(heldLeftSupportPose, leftSupportPickupRadius))
            {
                leftSupportActive = true;
            }
        }

        private bool TryGrabNearest()
        {
            ExtinguisherController nearest = FindNearestExtinguisher(
                GetRightHandPosition(),
                out int overlapCount,
                out float distanceSqr);

            lastOverlapCount = overlapCount;
            lastNearestDistance = nearest != null ? Mathf.Sqrt(distanceSqr) : -1f;
            if (nearest == null)
            {
                lastGrabStatus = $"No extinguisher within {pickupRadius:F2}m. Overlaps: {lastOverlapCount}.";
                return false;
            }

            return TryGrab(nearest);
        }

        private ExtinguisherController FindNearestExtinguisher(
            Vector3 center,
            out int overlapCount,
            out float nearestDistanceSqr)
        {
            overlapCount = Physics.OverlapSphereNonAlloc(
                center,
                pickupRadius,
                overlapResults,
                grabbableLayers,
                QueryTriggerInteraction.Ignore);

            ExtinguisherController nearest = null;
            nearestDistanceSqr = float.MaxValue;
            for (int i = 0; i < overlapCount; i++)
            {
                Collider candidateCollider = overlapResults[i];
                if (candidateCollider == null)
                {
                    continue;
                }

                ExtinguisherController candidate = candidateCollider.GetComponentInParent<ExtinguisherController>();
                if (candidate == null || candidate.IsHeld || candidate.GetComponent<Rigidbody>() == null)
                {
                    continue;
                }

                float distanceSqr = (candidate.transform.position - center).sqrMagnitude;
                if (distanceSqr < nearestDistanceSqr)
                {
                    nearestDistanceSqr = distanceSqr;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        private bool TryGrab(ExtinguisherController target)
        {
            if (target == null || target.IsHeld || heldExtinguisher != null)
            {
                lastGrabStatus = "Grab failed: target missing, already held, or hand already holding.";
                return false;
            }

            Rigidbody targetBody = target.GetComponent<Rigidbody>();
            if (targetBody == null)
            {
                lastGrabStatus = $"Grab failed: {target.name} has no Rigidbody.";
                return false;
            }

            target.transform.SetParent(null, true);
            target.SetDockedPhysicsState(false);
            targetBody.isKinematic = false;
            targetBody.useGravity = false;
            targetBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            targetBody.interpolation = RigidbodyInterpolation.Interpolate;
            targetBody.linearVelocity = Vector3.zero;
            targetBody.angularVelocity = Vector3.zero;

            heldExtinguisher = target;
            heldBody = targetBody;
            heldRightGripPose = FindInteractionPoint(target.transform, RightGripPoseName);
            heldLeftSupportPose = FindInteractionPoint(target.transform, LeftSupportPoseName);
            heldPinPullZone = FindInteractionPoint(target.transform, PinPullZoneName);
            leftSupportActive = false;
            pinDragActive = false;
            pinPullQueued = false;
            previousLeftNearPin = false;

            ComputeTargetPose(target, out Vector3 targetPosition, out Quaternion targetRotation);
            targetBody.position = targetPosition;
            targetBody.rotation = targetRotation;
            target.transform.SetPositionAndRotation(targetPosition, targetRotation);
            Physics.SyncTransforms();

            IgnoreHeldPlayerCollisions(target);
            target.MarkPickedUp(rightHandAnchor, false);
            trainingManager?.RegisterHeldExtinguisher(target);
            station?.NotifyPickedUp(target);
            lastGrabStatus = $"Grabbed {target.name}.";
            return true;
        }

        private void ReleaseHeld()
        {
            if (heldExtinguisher == null)
            {
                return;
            }

            ExtinguisherController releasedExtinguisher = heldExtinguisher;
            Rigidbody releasedBody = heldBody;

            CancelActivePinDrag();
            RestoreHeldPlayerCollisions();

            if (releasedBody != null)
            {
                releasedBody.useGravity = true;
                releasedBody.isKinematic = false;
                releasedBody.linearVelocity = Vector3.ClampMagnitude(
                    rightHandVelocity * throwVelocityMultiplier,
                    maxThrowVelocity);
                releasedBody.angularVelocity = Vector3.ClampMagnitude(
                    rightHandAngularVelocity,
                    maxThrowAngularVelocity);
                releasedBody.WakeUp();
            }

            heldExtinguisher = null;
            heldBody = null;
            heldRightGripPose = null;
            heldLeftSupportPose = null;
            heldPinPullZone = null;
            leftSupportActive = false;
            pinDragActive = false;
            pinPullQueued = false;
            previousLeftNearPin = false;

            releasedExtinguisher.MarkReleased();
            trainingManager?.ReleaseHeldExtinguisher(releasedExtinguisher);
            station?.NotifyReleased(releasedExtinguisher);
            lastGrabStatus = "Released extinguisher.";
        }

        private void OnDisable()
        {
            CancelActivePinDrag();
            RestoreHeldPlayerCollisions();
        }

        private void DriveHeldBody(float deltaTime)
        {
            if (heldBody == null || heldExtinguisher == null)
            {
                return;
            }

            ComputeTargetPose(heldExtinguisher, out Vector3 targetPosition, out Quaternion targetRotation);

            Vector3 positionVelocity = (targetPosition - heldBody.position) * positionFollowStrength;
            heldBody.linearVelocity = Vector3.ClampMagnitude(positionVelocity, maxHeldLinearVelocity);

            Quaternion rotationDelta = targetRotation * Quaternion.Inverse(heldBody.rotation);
            rotationDelta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
            if (angleDegrees > 180f)
            {
                angleDegrees -= 360f;
            }

            if (!float.IsNaN(axis.x) && axis.sqrMagnitude > 0.0001f && Mathf.Abs(angleDegrees) > 0.01f)
            {
                Vector3 angularVelocity = axis.normalized * (angleDegrees * Mathf.Deg2Rad * rotationFollowStrength);
                heldBody.angularVelocity = Vector3.ClampMagnitude(angularVelocity, maxHeldAngularVelocity);
            }
            else
            {
                heldBody.angularVelocity = Vector3.zero;
            }
        }

        private void ComputeTargetPose(
            ExtinguisherController extinguisher,
            out Vector3 targetPosition,
            out Quaternion targetRotation)
        {
            Transform root = extinguisher != null ? extinguisher.transform : transform;
            Transform gripPose = heldRightGripPose != null ? heldRightGripPose : root;
            Vector3 gripLocalPosition = root.InverseTransformPoint(gripPose.position);
            Quaternion gripLocalRotation = Quaternion.Inverse(root.rotation) * gripPose.rotation;

            Quaternion handRotation = GetRightHandRotation() * Quaternion.Euler(rightHandRotationOffsetEuler);
            targetRotation = handRotation * Quaternion.Inverse(gripLocalRotation);

            if (leftSupportActive && leftHandAnchor != null && heldLeftSupportPose != null)
            {
                Vector3 supportLocalPosition = root.InverseTransformPoint(heldLeftSupportPose.position);
                Vector3 localSupportDirection = supportLocalPosition - gripLocalPosition;
                Vector3 worldSupportDirection = leftHandAnchor.position - GetRightHandPosition();
                if (localSupportDirection.sqrMagnitude > 0.0001f &&
                    worldSupportDirection.sqrMagnitude > 0.0001f)
                {
                    Vector3 currentSupportDirection = targetRotation * localSupportDirection.normalized;
                    Quaternion supportRotation = Quaternion.FromToRotation(
                        currentSupportDirection,
                        worldSupportDirection.normalized);
                    targetRotation = supportRotation * targetRotation;
                }
            }

            targetPosition = GetRightHandPosition() - targetRotation * gripLocalPosition;
        }

        private bool IsLeftHandNear(Transform point, float radius)
        {
            return leftHandAnchor != null &&
                   point != null &&
                   Vector3.Distance(leftHandAnchor.position, point.position) <= radius;
        }

        private Transform FindInteractionPoint(Transform root, string pointName)
        {
            Transform point = root.Find(pointName);
            return point != null ? point : root;
        }

        private Vector3 GetRightHandPosition()
        {
            return rightHandAnchor != null ? rightHandAnchor.position : transform.position;
        }

        private Quaternion GetRightHandRotation()
        {
            return rightHandAnchor != null ? rightHandAnchor.rotation : transform.rotation;
        }

        private void CacheRightHandPose()
        {
            previousRightHandPosition = GetRightHandPosition();
            previousRightHandRotation = GetRightHandRotation();
        }

        private void UpdateRightHandMotion(float deltaTime)
        {
            Vector3 position = GetRightHandPosition();
            Quaternion rotation = GetRightHandRotation();
            rightHandVelocity = (position - previousRightHandPosition) / deltaTime;
            rightHandAngularVelocity = GetAngularVelocity(previousRightHandRotation, rotation, deltaTime);
            previousRightHandPosition = position;
            previousRightHandRotation = rotation;
        }

        private bool ReadRightGripHeld()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugRightGripHeld;
            }
#endif

            return QuestExtinguisherInput.RightGripHeld();
        }

        private bool ReadLeftGripHeld()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugLeftGripHeld;
            }
#endif

            return QuestExtinguisherInput.LeftGripHeld();
        }

        private bool ReadSprayHeld()
        {
#if UNITY_EDITOR
            if (debugInputActive)
            {
                return debugSprayHeld;
            }
#endif

            return QuestExtinguisherInput.RightTriggerHeld();
        }

        private static Vector3 GetAngularVelocity(Quaternion previous, Quaternion current, float deltaTime)
        {
            Quaternion delta = current * Quaternion.Inverse(previous);
            delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            if (angleDegrees > 180f)
            {
                angleDegrees -= 360f;
            }

            return axis.normalized * (angleDegrees * Mathf.Deg2Rad / deltaTime);
        }

        private void LogDebugState()
        {
            if (!debugLogging || Time.time < nextDebugLogTime)
            {
                return;
            }

            nextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
            Debug.Log(
                $"Extinguisher interaction: held={IsHeld}, rightGrip={previousRightGripHeld}, " +
                $"support={leftSupportActive}, spray={SprayHeld}, pinQueued={pinPullQueued}, " +
                $"overlaps={lastOverlapCount}, nearest={lastNearestDistance:F2}m, status={lastGrabStatus}",
                this);
        }

        private void CancelActivePinDrag()
        {
            if (!pinDragActive)
            {
                return;
            }

            pinDragActive = false;
            if (heldExtinguisher != null && !heldExtinguisher.IsPinPulled)
            {
                heldExtinguisher.CancelSafetyPinDrag();
            }
        }

        private void IgnoreHeldPlayerCollisions(ExtinguisherController extinguisher)
        {
            RestoreHeldPlayerCollisions();

            Transform playerRoot = ResolvePlayerCollisionRoot();
            if (extinguisher == null || playerRoot == null)
            {
                return;
            }

            Collider[] extinguisherColliders = extinguisher.GetComponentsInChildren<Collider>(true);
            Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>(true);
            foreach (Collider extinguisherCollider in extinguisherColliders)
            {
                if (extinguisherCollider == null)
                {
                    continue;
                }

                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider == null || playerCollider.transform.IsChildOf(extinguisher.transform))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(extinguisherCollider, playerCollider, true);
                    ignoredPlayerCollisionPairs.Add(new CollisionPair(extinguisherCollider, playerCollider));
                }
            }
        }

        private Transform ResolvePlayerCollisionRoot()
        {
            if (playerCollisionRoot != null)
            {
                return playerCollisionRoot;
            }

            CharacterController characterController = FindFirstObjectByType<CharacterController>();
            if (characterController != null)
            {
                playerCollisionRoot = characterController.transform;
            }

            return playerCollisionRoot;
        }

        private void RestoreHeldPlayerCollisions()
        {
            foreach (CollisionPair pair in ignoredPlayerCollisionPairs)
            {
                if (pair.ExtinguisherCollider != null && pair.PlayerCollider != null)
                {
                    Physics.IgnoreCollision(pair.ExtinguisherCollider, pair.PlayerCollider, false);
                }
            }

            ignoredPlayerCollisionPairs.Clear();
        }

        private readonly struct CollisionPair
        {
            public CollisionPair(Collider extinguisherCollider, Collider playerCollider)
            {
                ExtinguisherCollider = extinguisherCollider;
                PlayerCollider = playerCollider;
            }

            public Collider ExtinguisherCollider { get; }
            public Collider PlayerCollider { get; }
        }
    }
}
