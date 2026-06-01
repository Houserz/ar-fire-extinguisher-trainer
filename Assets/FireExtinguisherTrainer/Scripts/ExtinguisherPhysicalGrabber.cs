using UnityEngine;

namespace FireExtinguisherTrainer
{
    [DisallowMultipleComponent]
    public class ExtinguisherPhysicalGrabber : MonoBehaviour
    {
        [SerializeField] private FireTrainingManager trainingManager;
        [SerializeField] private ExtinguisherStation station;
        [SerializeField] private Transform grabOrigin;
        [SerializeField] private float pickupRadius = 0.75f;
        [SerializeField] private Vector3 holdOffset = new Vector3(0.08f, -0.08f, 0.18f);
        [SerializeField] private Vector3 holdEulerAngles = Vector3.zero;
        [SerializeField] private LayerMask grabbableLayers = ~0;
        [SerializeField] private float throwVelocityMultiplier = 1f;
        [SerializeField] private float maxThrowVelocity = 5f;
        [SerializeField] private bool debugLogging;
        [SerializeField] private float debugLogInterval = 0.5f;

        private readonly Collider[] overlapResults = new Collider[24];

        private Rigidbody handBody;
        private ExtinguisherController heldExtinguisher;
        private ExtinguisherHoldTracker heldTracker;
        private FixedJoint heldJoint;
        private Rigidbody heldBody;
        private Vector3 previousHandPosition;
        private Quaternion previousHandRotation;
        private Vector3 handVelocity;
        private Vector3 handAngularVelocity;
        private float nextDebugLogTime;
        private float lastGripValue;
        private int lastOverlapCount;
        private float lastNearestDistance = -1f;
        private string lastGrabStatus = "Idle.";

        public ExtinguisherController HeldExtinguisher => heldExtinguisher;
        public bool IsHolding => heldExtinguisher != null;
        public Rigidbody HandRigidbody => handBody;
        public float LastGripValue => lastGripValue;
        public int LastOverlapCount => lastOverlapCount;
        public float LastNearestDistance => lastNearestDistance;
        public string LastGrabStatus => lastGrabStatus;

        private void Awake()
        {
            EnsureHandBody();
            if (grabOrigin == null)
            {
                grabOrigin = transform.parent != null ? transform.parent : transform;
            }

            Vector3 holdPosition = GetHoldPosition();
            previousHandPosition = holdPosition;
            previousHandRotation = GetHoldRotation();
            handBody.position = holdPosition;
            handBody.rotation = previousHandRotation;
        }

        private void Update()
        {
            lastGripValue = RightControllerGripInput.ControllerGripValue();
            bool gripHeld = GripHeld();
            if (gripHeld && heldExtinguisher == null)
            {
                TryGrabNearest();
            }
            else if (!gripHeld && heldExtinguisher != null)
            {
                ReleaseHeld();
            }

            LogDebugState(gripHeld);
        }

        private void FixedUpdate()
        {
            EnsureHandBody();
            Vector3 holdPosition = GetHoldPosition();
            Quaternion holdRotation = GetHoldRotation();

            float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            handVelocity = (holdPosition - previousHandPosition) / deltaTime;
            handAngularVelocity = GetAngularVelocity(previousHandRotation, holdRotation, deltaTime);

            handBody.MovePosition(holdPosition);
            handBody.MoveRotation(holdRotation);

            previousHandPosition = holdPosition;
            previousHandRotation = holdRotation;
        }

        public void Configure(
            FireTrainingManager manager,
            ExtinguisherStation owningStation,
            Transform origin)
        {
            trainingManager = manager;
            station = owningStation;
            grabOrigin = origin;
        }

        public bool DebugTryGrab(ExtinguisherController explicitTarget = null)
        {
            return explicitTarget != null ? TryGrab(explicitTarget) : TryGrabNearest();
        }

        public void DebugRelease(Vector3 releaseVelocity)
        {
            handVelocity = releaseVelocity;
            ReleaseHeld();
        }

        private bool TryGrabNearest()
        {
            Vector3 holdPosition = GetHoldPosition();
            ExtinguisherController nearest = FindNearestExtinguisher(
                holdPosition,
                out int holdOverlapCount,
                out float holdDistanceSqr);

            Vector3 originPosition = grabOrigin != null ? grabOrigin.position : transform.position;
            ExtinguisherController originNearest = FindNearestExtinguisher(
                originPosition,
                out int originOverlapCount,
                out float originDistanceSqr);

            lastOverlapCount = holdOverlapCount + originOverlapCount;
            if (originNearest != null && (nearest == null || originDistanceSqr < holdDistanceSqr))
            {
                nearest = originNearest;
                holdDistanceSqr = originDistanceSqr;
            }

            lastNearestDistance = nearest != null ? Mathf.Sqrt(holdDistanceSqr) : -1f;
            if (nearest == null)
            {
                lastGrabStatus = $"No extinguisher within {pickupRadius:F2}m. Overlaps: {lastOverlapCount}.";
                return false;
            }

            return nearest != null && TryGrab(nearest);
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

            EnsureHandBody();
            target.transform.SetParent(null, true);
            target.SetDockedPhysicsState(false);
            targetBody.isKinematic = false;
            targetBody.useGravity = true;
            targetBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            targetBody.interpolation = RigidbodyInterpolation.Interpolate;
            targetBody.position = GetHoldPosition();
            targetBody.rotation = GetHoldRotation();

            heldJoint = target.gameObject.AddComponent<FixedJoint>();
            heldJoint.connectedBody = handBody;
            heldJoint.breakForce = Mathf.Infinity;
            heldJoint.breakTorque = Mathf.Infinity;
            heldJoint.enableCollision = false;

            heldExtinguisher = target;
            heldBody = targetBody;
            heldTracker = target.GetComponent<ExtinguisherHoldTracker>();
            if (heldTracker == null)
            {
                heldTracker = target.gameObject.AddComponent<ExtinguisherHoldTracker>();
            }

            heldTracker.SetGripFallbackEnabled(false);
            heldTracker.Configure(trainingManager, station, transform);
            heldTracker.NotifyPhysicalGrabbed(transform);
            lastGrabStatus = $"Grabbed {target.name}.";
            return true;
        }

        private void ReleaseHeld()
        {
            if (heldExtinguisher == null)
            {
                return;
            }

            if (heldJoint != null)
            {
                DestroyJoint(heldJoint);
            }

            if (heldBody != null)
            {
                Vector3 releaseVelocity = Vector3.ClampMagnitude(
                    handVelocity * throwVelocityMultiplier,
                    maxThrowVelocity);
                heldBody.linearVelocity = releaseVelocity;
                heldBody.angularVelocity = handAngularVelocity;
                heldBody.WakeUp();
            }

            heldTracker?.NotifyPhysicalReleased();
            lastGrabStatus = "Released extinguisher.";
            heldExtinguisher = null;
            heldTracker = null;
            heldJoint = null;
            heldBody = null;
        }

        private static void DestroyJoint(FixedJoint joint)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(joint);
                return;
            }
#endif

            Destroy(joint);
        }

        private void EnsureHandBody()
        {
            if (handBody == null)
            {
                handBody = GetComponent<Rigidbody>();
                if (handBody == null)
                {
                    handBody = gameObject.AddComponent<Rigidbody>();
                }
            }

            handBody.isKinematic = true;
            handBody.useGravity = false;
            handBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            handBody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private Vector3 GetHoldPosition()
        {
            Transform origin = grabOrigin != null ? grabOrigin : transform;
            return origin.TransformPoint(holdOffset);
        }

        private Quaternion GetHoldRotation()
        {
            Transform origin = grabOrigin != null ? grabOrigin : transform;
            return origin.rotation * Quaternion.Euler(holdEulerAngles);
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

        private static bool GripHeld()
        {
            return RightControllerGripInput.IsHeld();
        }

        private void LogDebugState(bool gripHeld)
        {
            if (!debugLogging || Time.time < nextDebugLogTime)
            {
                return;
            }

            nextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
            Debug.Log(
                $"Extinguisher grabber: gripHeld={gripHeld}, gripValue={lastGripValue:F2}, " +
                $"overlaps={lastOverlapCount}, nearest={lastNearestDistance:F2}m, status={lastGrabStatus}",
                this);
        }
    }
}
