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
        [SerializeField] private float floorRaycastStartHeight = 2.5f;
        [SerializeField] private float floorRaycastDistance = 5f;
        [SerializeField] private float minimumFloorNormalY = 0.65f;
        [SerializeField] private float maximumFloorHeightDelta = 1.25f;
        [SerializeField] private int generatedFloorSampleAttempts = 8;
        [SerializeField] private float fireFloorOffset = 0.05f;
        [SerializeField] private float stationFloorOffset = 0.02f;

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

        public void ApplyLayout(SpatialTrainingLayout layout)
        {
            station?.MoveStationToPose(layout.StationPose);
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

            layout = new SpatialTrainingLayout(
                OffsetPose(firePose, fireFloorOffset),
                OffsetPose(stationPose, stationFloorOffset),
                source,
                message);
            return TryValidateLayout(layout.FirePose.position, layout.StationPose.position, out _);
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
                SetPlacementMessage($"MRUK floor unavailable: no current room was found. rooms={mruk.Rooms.Count}.");
                return false;
            }

            if (room.FloorAnchors == null || room.FloorAnchors.Count == 0)
            {
                SetPlacementMessage($"MRUK floor unavailable: current room has no floor anchors. rooms={mruk.Rooms.Count}.");
                return false;
            }

            if (TryGetLayoutFromMrukFloorRaycasts(slot, room, out layout, out string rejectionReason) ||
                TryGetLayoutFromGeneratedMrukFloorSamples(slot, room, out layout, out rejectionReason))
            {
                SetPlacementMessage(layout.Message);
                LogLayout("MRUK floor accepted", layout);
                return true;
            }

            SetPlacementMessage(
                $"MRUK floor rejected: {rejectionReason} rooms={mruk.Rooms.Count}, floors={room.FloorAnchors.Count}.");
            return false;
#else
            SetPlacementMessage("MRUK floor unavailable: Meta MR Utility Kit is not compiled.");
            return false;
#endif
        }

#if META_MR_SDK_INSTALLED
        private bool TryGetLayoutFromMrukFloorRaycasts(
            int slot,
            MRUKRoom room,
            out SpatialTrainingLayout layout,
            out string rejectionReason)
        {
            layout = default;
            rejectionReason = "MRUK floor raycast did not hit the requested headset-forward floor points.";
            BuildDesiredLayout(
                slot,
                out Vector3 desiredFire,
                out Vector3 desiredStation,
                out Quaternion fireRotation,
                out Quaternion stationRotation);

            if (!TrySnapPointToMrukFloor(room, desiredFire, "fire", out Vector3 firePoint, out _, out string fireReason))
            {
                rejectionReason = fireReason;
                return false;
            }

            if (!TrySnapPointToMrukFloor(room, desiredStation, "extinguisher station", out Vector3 stationPoint, out _, out string stationReason))
            {
                rejectionReason = stationReason;
                return false;
            }

            return TryCreateValidatedLayout(
                firePoint,
                fireRotation,
                stationPoint,
                stationRotation,
                SpatialPlacementSource.MetaSceneFloor,
                "MRUK floor accepted from headset-forward raycast.",
                out layout,
                out rejectionReason);
        }

        private bool TryGetLayoutFromGeneratedMrukFloorSamples(
            int slot,
            MRUKRoom room,
            out SpatialTrainingLayout layout,
            out string rejectionReason)
        {
            layout = default;
            rejectionReason = "MRUK floor random surface sampling found no headset-forward valid layout.";
            var filter = new LabelFilter(MRUKAnchor.SceneLabels.FLOOR, MRUKAnchor.ComponentType.Plane);
            int attempts = Mathf.Max(0, generatedFloorSampleAttempts);
            Transform reference = ReferenceTransform;
            Vector3 origin = reference != null ? reference.position : transform.position;
            Vector3 forward = reference != null ? FlattenDirection(reference.forward, Vector3.forward) : transform.forward;

            for (int i = 0; i < attempts; i++)
            {
                if (!room.GenerateRandomPositionOnSurface(
                        MRUK.SurfaceType.FACING_UP,
                        planeEdgeMargin,
                        filter,
                        out Vector3 firePoint,
                        out Vector3 fireNormal))
                {
                    rejectionReason = "MRUK floor random surface sampling found no floor point for fire.";
                    return false;
                }

                if (!room.GenerateRandomPositionOnSurface(
                        MRUK.SurfaceType.FACING_UP,
                        planeEdgeMargin,
                        filter,
                        out Vector3 stationPoint,
                        out Vector3 stationNormal))
                {
                    rejectionReason = "MRUK floor random surface sampling found no floor point for extinguisher station.";
                    return false;
                }

                if (!TryAcceptFloorHit("fire", firePoint, fireNormal, out string fireReason))
                {
                    rejectionReason = fireReason;
                    continue;
                }

                if (!TryAcceptFloorHit("extinguisher station", stationPoint, stationNormal, out string stationReason))
                {
                    rejectionReason = stationReason;
                    continue;
                }

                if (TryCreateValidatedLayout(
                        firePoint,
                        FacePosition(firePoint, origin, -forward),
                        stationPoint,
                        FacePosition(stationPoint, origin, forward),
                        SpatialPlacementSource.MetaSceneFloor,
                        "MRUK floor accepted from official surface sampling.",
                        out layout,
                        out rejectionReason))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySnapPointToMrukFloor(
            MRUKRoom room,
            Vector3 desiredWorldPosition,
            string label,
            out Vector3 floorPoint,
            out Vector3 floorNormal,
            out string rejectionReason)
        {
            floorPoint = default;
            floorNormal = default;
            rejectionReason = $"MRUK floor raycast failed for {label}.";

            if (room == null)
            {
                rejectionReason = "MRUK floor raycast failed: room is missing.";
                return false;
            }

            Vector3 rayOrigin = desiredWorldPosition + Vector3.up * Mathf.Max(0.1f, floorRaycastStartHeight);
            var ray = new Ray(rayOrigin, Vector3.down);
            var filter = new LabelFilter(MRUKAnchor.SceneLabels.FLOOR, MRUKAnchor.ComponentType.Plane);
            if (!room.Raycast(ray, Mathf.Max(0.1f, floorRaycastDistance), filter, out RaycastHit hit, out MRUKAnchor anchor))
            {
                rejectionReason = $"MRUK floor raycast missed {label} target at {desiredWorldPosition:F2}.";
                return false;
            }

            floorPoint = hit.point;
            floorNormal = hit.normal;
            if (!TryAcceptFloorHit(label, floorPoint, floorNormal, out rejectionReason))
            {
                string anchorName = anchor != null ? anchor.name : "unknown";
                rejectionReason += $" anchor={anchorName}.";
                return false;
            }

            return true;
        }

        private bool TryAcceptFloorHit(
            string label,
            Vector3 point,
            Vector3 normal,
            out string rejectionReason)
        {
            rejectionReason = null;
            if (!IsFinite(point) || !IsFinite(normal))
            {
                rejectionReason = $"MRUK floor rejected: {label} floor hit is not finite. point={point:F2}, normal={normal:F2}.";
                return false;
            }

            if (normal.y < minimumFloorNormalY)
            {
                rejectionReason = $"MRUK floor rejected: {label} normal is not upward enough. normal={normal:F2}.";
                return false;
            }

            float groundDelta = Mathf.Abs(point.y - fallbackGroundY);
            if (groundDelta > Mathf.Max(0.01f, maximumFloorHeightDelta))
            {
                rejectionReason = $"MRUK floor rejected: {label} height {point.y:F2} is {groundDelta:F2}m from expected ground {fallbackGroundY:F2}.";
                return false;
            }

            return true;
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
            firePosition.y = fallbackGroundY + Mathf.Max(0f, fireFloorOffset);
            stationPosition.y = fallbackGroundY + Mathf.Max(0f, stationFloorOffset);
            var firePose = new Pose(firePosition, fireRotation);
            var stationPose = new Pose(stationPosition, stationRotation);
            layout = new SpatialTrainingLayout(
                firePose,
                stationPose,
                SpatialPlacementSource.Fallback,
                $"Using fallback placement: {reason}");

            if (!TryValidateLayout(layout.FirePose.position, layout.StationPose.position, out string rejectionReason))
            {
                SetPlacementMessage($"Fallback placement failed: {rejectionReason}");
                return false;
            }

            SetPlacementMessage(layout.Message);
            LogLayout("MRUK fallback placement", layout);
            return true;
        }

        private bool TryCreateValidatedLayout(
            Vector3 firePosition,
            Quaternion fireRotation,
            Vector3 stationPosition,
            Quaternion stationRotation,
            SpatialPlacementSource source,
            string message,
            out SpatialTrainingLayout layout,
            out string rejectionReason)
        {
            layout = new SpatialTrainingLayout(
                new Pose(OffsetFloorPosition(firePosition, fireFloorOffset), fireRotation),
                new Pose(OffsetFloorPosition(stationPosition, stationFloorOffset), stationRotation),
                source,
                message);
            return TryValidateLayout(layout.FirePose.position, layout.StationPose.position, out rejectionReason);
        }

        private static Pose OffsetPose(Pose pose, float floorOffset)
        {
            return new Pose(OffsetFloorPosition(pose.position, floorOffset), pose.rotation);
        }

        private static Vector3 OffsetFloorPosition(Vector3 position, float floorOffset)
        {
            position.y += Mathf.Max(0f, floorOffset);
            return position;
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

        private bool TryValidateLayout(Vector3 firePosition, Vector3 stationPosition, out string rejectionReason)
        {
            rejectionReason = null;
            if (!IsFinite(firePosition) || !IsFinite(stationPosition))
            {
                rejectionReason = $"layout contains non-finite positions. fire={firePosition:F2}, station={stationPosition:F2}.";
                return false;
            }

            Transform reference = ReferenceTransform;
            Vector3 origin = reference != null ? reference.position : transform.position;
            Vector3 forward = reference != null ? reference.forward : transform.forward;
            forward = FlattenDirection(forward, Vector3.forward);

            Vector3 flatOrigin = FlattenPosition(origin);
            float fireForwardDistance = Vector3.Dot(FlattenPosition(firePosition) - flatOrigin, forward);
            float stationForwardDistance = Vector3.Dot(FlattenPosition(stationPosition) - flatOrigin, forward);
            if (fireForwardDistance < fireMinDistance || fireForwardDistance > fireMaxDistance)
            {
                rejectionReason = $"fire is outside headset-forward range. forwardDistance={fireForwardDistance:F2}m, expected={fireMinDistance:F2}-{fireMaxDistance:F2}m.";
                return false;
            }

            if (stationForwardDistance < stationMinDistance || stationForwardDistance > stationMaxDistance)
            {
                rejectionReason = $"extinguisher station is outside headset-forward range. forwardDistance={stationForwardDistance:F2}m, expected={stationMinDistance:F2}-{stationMaxDistance:F2}m.";
                return false;
            }

            float fireStationDistance = Vector3.Distance(FlattenPosition(firePosition), FlattenPosition(stationPosition));
            if (fireStationDistance < minimumFireStationDistance)
            {
                rejectionReason = $"fire and extinguisher station are too close. distance={fireStationDistance:F2}m, minimum={minimumFireStationDistance:F2}m.";
                return false;
            }

            return true;
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

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void LogLayout(string prefix, SpatialTrainingLayout layout)
        {
            Transform reference = ReferenceTransform;
            Vector3 origin = reference != null ? reference.position : transform.position;
            Vector3 forward = reference != null
                ? FlattenDirection(reference.forward, Vector3.forward)
                : FlattenDirection(transform.forward, Vector3.forward);
            float fireForwardDistance = Vector3.Dot(FlattenPosition(layout.FirePose.position) - FlattenPosition(origin), forward);
            float stationForwardDistance = Vector3.Dot(FlattenPosition(layout.StationPose.position) - FlattenPosition(origin), forward);
            float fireStationDistance = Vector3.Distance(
                FlattenPosition(layout.FirePose.position),
                FlattenPosition(layout.StationPose.position));
            Debug.Log(
                $"{prefix}: source={layout.Source}, fire={layout.FirePose.position:F2} ({fireForwardDistance:F2}m forward), station={layout.StationPose.position:F2} ({stationForwardDistance:F2}m forward), fireStationDistance={fireStationDistance:F2}m, message={layout.Message}",
                this);
        }
    }
}
