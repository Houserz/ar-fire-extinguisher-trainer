using System;
using UnityEngine;

#if META_MR_SDK_INSTALLED
using Meta.XR.MRUtilityKit;
#endif

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
        [SerializeField] private ExtinguisherStation station;
        [SerializeField] private bool preferMetaSceneFloor = true;
        [SerializeField] private float fireMinDistance = 2f;
        [SerializeField] private float fireMaxDistance = 3f;
        [SerializeField] private float stationMinDistance = 0.8f;
        [SerializeField] private float stationMaxDistance = 1.4f;
        [SerializeField] private float minimumFireStationDistance = 1f;
        [SerializeField] private float planeEdgeMargin = 0.25f;
        [SerializeField] private float fallbackGroundY = 0f;

        private int layoutSequenceIndex;

        public string LastPlacementMessage { get; private set; } = "Placement has not run yet.";

        public void Configure(
            Transform referenceOrigin,
            object unusedDetectedPlaneManager = null,
            ExtinguisherStation linkedStation = null)
        {
            userOrigin = referenceOrigin;
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
            if (preferMetaSceneFloor && TryGetMetaSceneFloorLayout(slot, out layout))
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
            if (!preferMetaSceneFloor || !TryGetMetaSceneFloorLayout(slot, out layout))
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
            if (!TryGetHorizontalPlaneBasedLayout(
                    slot,
                    Matrix4x4.TRS(planePose.position, planePose.rotation, Vector3.one),
                    new Rect(-planeExtents.x, -planeExtents.y, planeExtents.x * 2f, planeExtents.y * 2f),
                    SpatialPlacementSource.DetectedPlane,
                    "Horizontal surface locked.",
                    out layout))
            {
                layout = default;
                return false;
            }

            layoutSequenceIndex++;
            return true;
        }

        private bool TryGetHorizontalPlaneBasedLayout(
            int slot,
            Matrix4x4 surfaceToWorld,
            Rect localBounds,
            SpatialPlacementSource source,
            string message,
            out SpatialTrainingLayout layout)
        {
            layout = default;
            BuildDesiredLayout(
                slot,
                out Vector3 desiredFire,
                out Vector3 desiredStation,
                out Quaternion fireRotation,
                out Quaternion stationRotation);
            if (!TryProjectWithinHorizontalPlane(
                    surfaceToWorld,
                    localBounds,
                    desiredFire,
                    fireRotation,
                    out Pose firePose) ||
                !TryProjectWithinHorizontalPlane(
                    surfaceToWorld,
                    localBounds,
                    desiredStation,
                    stationRotation,
                    out Pose stationPose))
            {
                return false;
            }

            layout = new SpatialTrainingLayout(firePose, stationPose, source, message);
            return IsValidLayout(layout.FirePose.position, layout.StationPose.position);
        }

        public void ApplyLayout(SpatialTrainingLayout layout)
        {
            station?.MoveStationToPose(layout.StationPose);
        }

        private bool TryGetMetaSceneFloorLayout(int slot, out SpatialTrainingLayout layout)
        {
            layout = default;

#if META_MR_SDK_INSTALLED
            MRUK mruk = MRUK.Instance != null ? MRUK.Instance : FindFirstObjectByType<MRUK>();
            if (mruk == null)
            {
                SetPlacementMessage("MRUK floor unavailable: missing MRUK scene object.");
                return false;
            }

            if (mruk.EnableWorldLock)
            {
                SetPlacementMessage("MRUK floor unavailable: EnableWorldLock is on and can move the tracking space.");
                return false;
            }

            if (!mruk.IsInitialized)
            {
                SetPlacementMessage("MRUK floor unavailable: scene data is still loading.");
                return false;
            }

            MRUKRoom room;
            try
            {
                room = mruk.GetCurrentRoom();
            }
            catch (Exception exception)
            {
                SetPlacementMessage($"MRUK floor unavailable: {exception.GetType().Name} while reading current room.");
                return false;
            }

            if (room == null && mruk.Rooms.Count > 0)
            {
                room = mruk.Rooms[0];
            }

            if (room == null)
            {
                SetPlacementMessage("MRUK floor unavailable: no current room was found.");
                return false;
            }

            if (room.FloorAnchors == null || room.FloorAnchors.Count == 0)
            {
                SetPlacementMessage("MRUK floor unavailable: current room has no floor anchors.");
                return false;
            }

            Transform reference = ReferenceTransform;
            Vector3 referencePosition = reference != null ? reference.position : transform.position;
            float bestScore = float.NegativeInfinity;
            SpatialTrainingLayout bestLayout = default;

            foreach (MRUKAnchor floorAnchor in room.FloorAnchors)
            {
                if (!TryGetLayoutOnMrukFloor(slot, floorAnchor, out SpatialTrainingLayout candidate))
                {
                    continue;
                }

                float area = floorAnchor.PlaneRect.HasValue
                    ? floorAnchor.PlaneRect.Value.size.x * floorAnchor.PlaneRect.Value.size.y
                    : 0f;
                float distancePenalty = Vector3.Distance(referencePosition, candidate.FirePose.position) * 0.05f;
                float score = area - distancePenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLayout = candidate;
                }
            }

            if (bestScore <= float.NegativeInfinity)
            {
                SetPlacementMessage("MRUK floor unavailable: no floor anchor can contain the demo layout.");
                return false;
            }

            layout = bestLayout;
            SetPlacementMessage(layout.Message);
            return true;
#else
            SetPlacementMessage("MRUK floor unavailable: Meta MR Utility Kit is not compiled.");
            return false;
#endif
        }

#if META_MR_SDK_INSTALLED
        private bool TryGetLayoutOnMrukFloor(int slot, MRUKAnchor floorAnchor, out SpatialTrainingLayout layout)
        {
            layout = default;
            if (floorAnchor == null || !floorAnchor.PlaneRect.HasValue)
            {
                return false;
            }

            if (floorAnchor.transform.forward.y < 0.65f)
            {
                return false;
            }

            return TryGetSurfaceBasedLayout(
                slot,
                floorAnchor.transform.localToWorldMatrix,
                floorAnchor.PlaneRect.Value,
                SpatialPlacementSource.MetaSceneFloor,
                "Horizontal surface locked with MRUK floor.",
                out layout,
                floorAnchor);
        }
#endif

        private bool TryGetFallbackLayout(int slot, out SpatialTrainingLayout layout)
        {
            string reason = string.IsNullOrWhiteSpace(LastPlacementMessage)
                ? "No MRUK floor data was available."
                : LastPlacementMessage;
            BuildDesiredLayout(
                slot,
                out Vector3 firePosition,
                out Vector3 stationPosition,
                out Quaternion fireRotation,
                out Quaternion stationRotation);
            firePosition.y = fallbackGroundY;
            stationPosition.y = fallbackGroundY;
            var firePose = new Pose(firePosition, fireRotation);
            var stationPose = new Pose(stationPosition, stationRotation);
            layout = new SpatialTrainingLayout(
                firePose,
                stationPose,
                SpatialPlacementSource.Fallback,
                $"Using fallback placement. {reason}");

            if (!IsValidLayout(layout.FirePose.position, layout.StationPose.position))
            {
                SetPlacementMessage("Fallback placement failed: generated positions were outside the safe demo range.");
                return false;
            }

            SetPlacementMessage(layout.Message);
            return true;
        }

        private bool TryGetSurfaceBasedLayout(
            int slot,
            Matrix4x4 surfaceToWorld,
            Rect localBounds,
            SpatialPlacementSource source,
            string message,
            out SpatialTrainingLayout layout,
#if META_MR_SDK_INSTALLED
            MRUKAnchor boundaryAnchor = null)
#else
            object boundaryAnchor = null)
#endif
        {
            layout = default;
            BuildDesiredLayout(
                slot,
                out Vector3 desiredFire,
                out Vector3 desiredStation,
                out Quaternion fireRotation,
                out Quaternion stationRotation);
            if (!TryProjectWithinSurface(
                    surfaceToWorld,
                    localBounds,
                    desiredFire,
                    fireRotation,
                    out Pose firePose,
                    boundaryAnchor) ||
                !TryProjectWithinSurface(
                    surfaceToWorld,
                    localBounds,
                    desiredStation,
                    stationRotation,
                    out Pose stationPose,
                    boundaryAnchor))
            {
                return false;
            }

            layout = new SpatialTrainingLayout(firePose, stationPose, source, message);
            return IsValidLayout(layout.FirePose.position, layout.StationPose.position);
        }

        private bool TryProjectWithinSurface(
            Matrix4x4 surfaceToWorld,
            Rect localBounds,
            Vector3 desiredWorldPosition,
            Quaternion rotation,
            out Pose pose,
#if META_MR_SDK_INSTALLED
            MRUKAnchor boundaryAnchor = null)
#else
            object boundaryAnchor = null)
#endif
        {
            pose = default;
            if (localBounds.width <= planeEdgeMargin * 2f || localBounds.height <= planeEdgeMargin * 2f)
            {
                return false;
            }

            Matrix4x4 worldToSurface = surfaceToWorld.inverse;
            Vector3 local = worldToSurface.MultiplyPoint3x4(desiredWorldPosition);
            if (local.x < localBounds.xMin + planeEdgeMargin ||
                local.x > localBounds.xMax - planeEdgeMargin ||
                local.y < localBounds.yMin + planeEdgeMargin ||
                local.y > localBounds.yMax - planeEdgeMargin)
            {
                return false;
            }

#if META_MR_SDK_INSTALLED
            if (boundaryAnchor != null &&
                boundaryAnchor.PlaneBoundary2D != null &&
                boundaryAnchor.PlaneBoundary2D.Count > 0 &&
                !boundaryAnchor.IsPositionInBoundary(new Vector2(local.x, local.y)))
            {
                return false;
            }
#endif

            local.z = 0f;
            pose = new Pose(surfaceToWorld.MultiplyPoint3x4(local), rotation);
            return true;
        }

        private bool TryProjectWithinHorizontalPlane(
            Matrix4x4 surfaceToWorld,
            Rect localBounds,
            Vector3 desiredWorldPosition,
            Quaternion rotation,
            out Pose pose)
        {
            pose = default;
            if (localBounds.width <= planeEdgeMargin * 2f || localBounds.height <= planeEdgeMargin * 2f)
            {
                return false;
            }

            Matrix4x4 worldToSurface = surfaceToWorld.inverse;
            Vector3 local = worldToSurface.MultiplyPoint3x4(desiredWorldPosition);
            if (local.x < localBounds.xMin + planeEdgeMargin ||
                local.x > localBounds.xMax - planeEdgeMargin ||
                local.z < localBounds.yMin + planeEdgeMargin ||
                local.z > localBounds.yMax - planeEdgeMargin)
            {
                return false;
            }

            local.y = 0f;
            pose = new Pose(surfaceToWorld.MultiplyPoint3x4(local), rotation);
            return true;
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

        private void SetPlacementMessage(string message)
        {
            LastPlacementMessage = string.IsNullOrWhiteSpace(message)
                ? "Placement status unavailable."
                : message;
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
