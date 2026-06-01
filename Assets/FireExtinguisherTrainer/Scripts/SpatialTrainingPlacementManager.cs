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
        [SerializeField] private float fireMaxDistance = 3f;
        [SerializeField] private float stationMinDistance = 0.8f;
        [SerializeField] private float stationMaxDistance = 1.4f;
        [SerializeField] private float minimumFireStationDistance = 1f;
        [SerializeField] private float planeEdgeMargin = 0.25f;
        [SerializeField] private float fallbackGroundY = 0f;

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
            if (!TryPrepareTrainingLayout(out SpatialTrainingLayout layout))
            {
                firePose = default;
                return false;
            }

            firePose = layout.FirePose;
            return true;
        }

        public bool TryPrepareTrainingLayout(out SpatialTrainingLayout layout)
        {
            if (!TryGetTrainingLayout(allowFallback: true, out layout))
            {
                return false;
            }

            ApplyLayout(layout);
            return true;
        }

        public bool TryGetTrainingPoses(out Pose firePose, out Pose stationPose)
        {
            if (!TryGetTrainingLayout(allowFallback: true, out SpatialTrainingLayout layout))
            {
                firePose = default;
                stationPose = default;
                return false;
            }

            firePose = layout.FirePose;
            stationPose = layout.StationPose;
            return true;
        }

        public bool TryGetTrainingLayout(bool allowFallback, out SpatialTrainingLayout layout)
        {
            int slot = layoutSequenceIndex;
            if (preferDetectedPlanes && TryGetPlaneBasedLayout(slot, out layout))
            {
                layoutSequenceIndex++;
                return true;
            }

            if (allowFallback && TryGetFallbackLayout(slot, out layout))
            {
                layoutSequenceIndex++;
                return true;
            }

            layout = default;
            return false;
        }

        public bool TryGetDetectedPlaneLayout(out SpatialTrainingLayout layout)
        {
            int slot = layoutSequenceIndex;
            if (!preferDetectedPlanes || !TryGetPlaneBasedLayout(slot, out layout))
            {
                layout = default;
                return false;
            }

            layoutSequenceIndex++;
            return true;
        }

        public bool TryGetFallbackLayout(out SpatialTrainingLayout layout)
        {
            int slot = layoutSequenceIndex;
            if (!TryGetFallbackLayout(slot, out layout))
            {
                layout = default;
                return false;
            }

            layoutSequenceIndex++;
            return true;
        }

        public bool TryGetLayoutOnHorizontalPlane(Pose planePose, Vector2 planeExtents, out SpatialTrainingLayout layout)
        {
            int slot = layoutSequenceIndex;
            if (!TryGetSurfaceBasedLayout(
                    slot,
                    planePose.position,
                    planePose.rotation,
                    planeExtents,
                    SpatialPlacementSource.DetectedPlane,
                    out layout))
            {
                layout = default;
                return false;
            }

            layoutSequenceIndex++;
            return true;
        }

        public void ApplyLayout(SpatialTrainingLayout layout)
        {
            station?.MoveStationToPose(layout.StationPose);
        }

        private bool TryGetPlaneBasedLayout(int slot, out SpatialTrainingLayout layout)
        {
            layout = default;

            if (planeManager == null)
            {
                return false;
            }

            Transform reference = ReferenceTransform;
            Vector3 referencePosition = reference != null ? reference.position : transform.position;
            float bestScore = float.NegativeInfinity;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (!IsUsableHorizontalPlane(plane))
                {
                    continue;
                }

                if (!TryGetSurfaceBasedLayout(
                        slot,
                        plane.transform.position,
                        plane.transform.rotation,
                        plane.extents,
                        SpatialPlacementSource.DetectedPlane,
                        out SpatialTrainingLayout candidate))
                {
                    continue;
                }

                float area = plane.size.x * plane.size.y;
                float distancePenalty = Vector3.Distance(referencePosition, plane.center) * 0.05f;
                float score = area - distancePenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    layout = candidate;
                }
            }

            return bestScore > float.NegativeInfinity;
        }

        private bool TryGetFallbackLayout(int slot, out SpatialTrainingLayout layout)
        {
            BuildDesiredLayout(slot, out Vector3 firePosition, out Vector3 stationPosition, out Quaternion fireRotation, out Quaternion stationRotation);
            firePosition.y = fallbackGroundY;
            stationPosition.y = fallbackGroundY;
            var firePose = new Pose(firePosition, fireRotation);
            var stationPose = new Pose(stationPosition, stationRotation);
            layout = new SpatialTrainingLayout(firePose, stationPose, SpatialPlacementSource.Fallback);
            return IsValidLayout(layout.FirePose.position, layout.StationPose.position);
        }

        private bool TryGetSurfaceBasedLayout(
            int slot,
            Vector3 surfacePosition,
            Quaternion surfaceRotation,
            Vector2 extents,
            SpatialPlacementSource source,
            out SpatialTrainingLayout layout)
        {
            layout = default;
            BuildDesiredLayout(slot, out Vector3 desiredFire, out Vector3 desiredStation, out Quaternion fireRotation, out Quaternion stationRotation);
            if (!TryProjectWithinSurface(surfacePosition, surfaceRotation, extents, desiredFire, fireRotation, out Pose firePose) ||
                !TryProjectWithinSurface(surfacePosition, surfaceRotation, extents, desiredStation, stationRotation, out Pose stationPose))
            {
                return false;
            }

            layout = new SpatialTrainingLayout(firePose, stationPose, source);
            return IsValidLayout(layout.FirePose.position, layout.StationPose.position);
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

        private bool IsUsableHorizontalPlane(ARPlane plane)
        {
            return plane != null &&
                   plane.subsumedBy == null &&
                   plane.gameObject.activeInHierarchy &&
                   plane.alignment == PlaneAlignment.HorizontalUp &&
                   plane.extents.x > planeEdgeMargin &&
                   plane.extents.y > planeEdgeMargin;
        }

        private bool TryProjectWithinSurface(
            Vector3 surfacePosition,
            Quaternion surfaceRotation,
            Vector2 extents,
            Vector3 desiredWorldPosition,
            Quaternion rotation,
            out Pose pose)
        {
            pose = default;
            if (extents.x <= planeEdgeMargin || extents.y <= planeEdgeMargin)
            {
                return false;
            }

            Matrix4x4 surfaceToWorld = Matrix4x4.TRS(surfacePosition, surfaceRotation, Vector3.one);
            Matrix4x4 worldToSurface = surfaceToWorld.inverse;
            Vector3 local = worldToSurface.MultiplyPoint3x4(desiredWorldPosition);
            if (local.x < -extents.x + planeEdgeMargin ||
                local.x > extents.x - planeEdgeMargin ||
                local.z < -extents.y + planeEdgeMargin ||
                local.z > extents.y - planeEdgeMargin)
            {
                return false;
            }

            local.y = 0f;
            pose = new Pose(surfaceToWorld.MultiplyPoint3x4(local), rotation);
            return true;
        }

        private bool IsValidLayout(Vector3 firePosition, Vector3 stationPosition)
        {
            Transform reference = ReferenceTransform;
            Vector3 origin = reference != null ? reference.position : transform.position;
            Vector3 forward = reference != null ? reference.forward : transform.forward;
            forward = FlattenDirection(forward, Vector3.forward);

            Vector3 flatOrigin = FlattenPosition(origin);
            float fireForwardDistance = Vector3.Dot(FlattenPosition(firePosition) - flatOrigin, forward);
            float stationForwardDistance = Vector3.Dot(FlattenPosition(stationPosition) - flatOrigin, forward);
            return fireForwardDistance >= fireMinDistance &&
                   fireForwardDistance <= fireMaxDistance &&
                   stationForwardDistance >= stationMinDistance &&
                   stationForwardDistance <= stationMaxDistance &&
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
