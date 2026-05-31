using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace FireExtinguisherTrainer
{
    [DisallowMultipleComponent]
    public class SpatialTrainingPlacementManager : MonoBehaviour
    {
        private static readonly float[] FireDistanceSlots = { 0.65f, 0.45f, 0.8f, 0.55f, 0.72f };
        private static readonly float[] FireLateralSlots = { 0f, -0.55f, 0.55f, -0.28f, 0.35f };
        private static readonly float[] StationDistanceSlots = { 0.35f, 0.5f, 0.28f, 0.44f, 0.38f };
        private static readonly float[] StationLateralSlots = { -0.65f, 0.35f, -0.35f, 0.72f, -0.72f };

        [SerializeField] private Transform userOrigin;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ExtinguisherStation station;
        [SerializeField] private bool preferDetectedPlanes = true;
        [SerializeField] private float fireMinDistance = 2f;
        [SerializeField] private float fireMaxDistance = 4f;
        [SerializeField] private float stationMinDistance = 0.8f;
        [SerializeField] private float stationMaxDistance = 1.8f;
        [SerializeField] private float minimumFireStationDistance = 1f;
        [SerializeField] private float planeEdgeMargin = 0.25f;

        private int layoutSequenceIndex;

        public void Configure(
            Transform referenceOrigin,
            ARPlaneManager detectedPlaneManager = null,
            ExtinguisherStation linkedStation = null)
        {
            userOrigin = referenceOrigin;
            planeManager = detectedPlaneManager;
            station = linkedStation;
        }

        public bool TryPrepareTrainingLayout(out Pose firePose)
        {
            if (!TryGetTrainingPoses(out firePose, out Pose stationPose))
            {
                return false;
            }

            station?.MoveStationToPose(stationPose);
            return true;
        }

        public bool TryGetTrainingPoses(out Pose firePose, out Pose stationPose)
        {
            int slot = layoutSequenceIndex++;
            if (preferDetectedPlanes && TryGetPlaneBasedPoses(slot, out firePose, out stationPose))
            {
                return true;
            }

            return TryGetFallbackPoses(slot, out firePose, out stationPose);
        }

        private bool TryGetPlaneBasedPoses(int slot, out Pose firePose, out Pose stationPose)
        {
            firePose = default;
            stationPose = default;

            ARPlane plane = PickBestHorizontalPlane();
            if (plane == null)
            {
                return false;
            }

            BuildDesiredLayout(slot, out Vector3 desiredFire, out Vector3 desiredStation, out Quaternion fireRotation, out Quaternion stationRotation);
            if (!TryProjectOntoPlane(plane, desiredFire, fireRotation, out firePose) ||
                !TryProjectOntoPlane(plane, desiredStation, stationRotation, out stationPose))
            {
                return false;
            }

            return IsValidLayout(firePose.position, stationPose.position);
        }

        private bool TryGetFallbackPoses(int slot, out Pose firePose, out Pose stationPose)
        {
            BuildDesiredLayout(slot, out Vector3 firePosition, out Vector3 stationPosition, out Quaternion fireRotation, out Quaternion stationRotation);
            firePose = new Pose(firePosition, fireRotation);
            stationPose = new Pose(stationPosition, stationRotation);
            return IsValidLayout(firePose.position, stationPose.position);
        }

        private void BuildDesiredLayout(
            int slot,
            out Vector3 firePosition,
            out Vector3 stationPosition,
            out Quaternion fireRotation,
            out Quaternion stationRotation)
        {
            Transform reference = ReferenceTransform;
            Vector3 origin = reference != null ? reference.position : transform.position;
            Vector3 forward = reference != null ? reference.forward : transform.forward;
            forward = FlattenDirection(forward, Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            int index = Mathf.Abs(slot) % FireDistanceSlots.Length;
            float fireDistance = Mathf.Lerp(fireMinDistance, fireMaxDistance, FireDistanceSlots[index]);
            float stationDistance = Mathf.Lerp(stationMinDistance, stationMaxDistance, StationDistanceSlots[index]);
            float fireLateral = FireLateralSlots[index];
            float stationLateral = StationLateralSlots[index];

            firePosition = origin + forward * fireDistance + right * fireLateral;
            stationPosition = origin + forward * stationDistance + right * stationLateral;

            fireRotation = FacePosition(firePosition, origin, -forward);
            stationRotation = FacePosition(stationPosition, origin, forward);
        }

        private Transform ReferenceTransform
        {
            get
            {
                if (userOrigin != null)
                {
                    return userOrigin;
                }

                return Camera.main != null ? Camera.main.transform : transform;
            }
        }

        private ARPlane PickBestHorizontalPlane()
        {
            if (planeManager == null)
            {
                return null;
            }

            Transform reference = ReferenceTransform;
            Vector3 referencePosition = reference != null ? reference.position : transform.position;
            ARPlane bestPlane = null;
            float bestScore = float.NegativeInfinity;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane == null ||
                    plane.subsumedBy != null ||
                    !plane.gameObject.activeInHierarchy ||
                    plane.alignment != PlaneAlignment.HorizontalUp ||
                    plane.extents.x <= planeEdgeMargin ||
                    plane.extents.y <= planeEdgeMargin)
                {
                    continue;
                }

                float area = plane.size.x * plane.size.y;
                float distancePenalty = Vector3.Distance(referencePosition, plane.center) * 0.05f;
                float score = area - distancePenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPlane = plane;
                }
            }

            return bestPlane;
        }

        private bool TryProjectOntoPlane(ARPlane plane, Vector3 desiredWorldPosition, Quaternion rotation, out Pose pose)
        {
            pose = default;
            Vector2 extents = plane.extents;
            if (extents.x <= planeEdgeMargin || extents.y <= planeEdgeMargin)
            {
                return false;
            }

            Vector3 local = plane.transform.InverseTransformPoint(desiredWorldPosition);
            local.x = Mathf.Clamp(local.x, -extents.x + planeEdgeMargin, extents.x - planeEdgeMargin);
            local.y = 0f;
            local.z = Mathf.Clamp(local.z, -extents.y + planeEdgeMargin, extents.y - planeEdgeMargin);
            pose = new Pose(plane.transform.TransformPoint(local), rotation);
            return true;
        }

        private bool IsValidLayout(Vector3 firePosition, Vector3 stationPosition)
        {
            Transform reference = ReferenceTransform;
            Vector3 origin = reference != null ? reference.position : transform.position;
            Vector3 forward = reference != null ? reference.forward : transform.forward;
            forward = FlattenDirection(forward, Vector3.forward);

            Vector3 fireOffset = firePosition - origin;
            Vector3 stationOffset = stationPosition - origin;
            return Vector3.Dot(FlattenDirection(fireOffset, forward), forward) > 0f &&
                   Vector3.Dot(FlattenDirection(stationOffset, forward), forward) > 0f &&
                   Vector3.Distance(FlattenPosition(firePosition), FlattenPosition(stationPosition)) >= minimumFireStationDistance;
        }

        private static Vector3 FaceDirection(Vector3 from, Vector3 to, Vector3 fallbackDirection)
        {
            return FlattenDirection(to - from, fallbackDirection);
        }

        private static Quaternion FacePosition(Vector3 from, Vector3 to, Vector3 fallbackDirection)
        {
            return Quaternion.LookRotation(FaceDirection(from, to, fallbackDirection), Vector3.up);
        }

        private static Vector3 FlattenDirection(Vector3 direction, Vector3 fallbackDirection)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f)
            {
                flat = Vector3.ProjectOnPlane(fallbackDirection, Vector3.up);
            }

            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }

        private static Vector3 FlattenPosition(Vector3 position)
        {
            return new Vector3(position.x, 0f, position.z);
        }
    }
}
