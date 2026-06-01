using UnityEngine;

namespace FireExtinguisherTrainer
{
    public enum PassStep
    {
        PullPin,
        AimAtBase,
        SqueezeHandle,
        SweepSideToSide,
        Completed
    }

    public enum TrainingOutcome
    {
        Running,
        Success,
        Failed
    }

    public enum SprayHitQuality
    {
        Miss,
        WrongArea,
        BaseHit
    }

    public enum SpatialPlacementSource
    {
        None,
        MetaSceneFloor,
        DetectedPlane,
        Fallback
    }

    public struct SpatialTrainingLayout
    {
        public Pose FirePose;
        public Pose StationPose;
        public SpatialPlacementSource Source;
        public string Message;

        public SpatialTrainingLayout(
            Pose firePose,
            Pose stationPose,
            SpatialPlacementSource source,
            string message = null)
        {
            FirePose = firePose;
            StationPose = stationPose;
            Source = source;
            Message = message;
        }
    }

    public struct TrainingSessionReport
    {
        public TrainingOutcome Outcome;
        public PassStep CurrentStep;
        public string Status;
        public string InstructionText;
        public string ResultReason;
        public string MistakeBreakdown;
        public int Mistakes;
        public float ElapsedSeconds;
        public float AimingAccuracy01;
        public float TotalSprayTimeSeconds;
        public float AccurateSprayTimeSeconds;
        public float ExtinguisherUsedSeconds;
        public int ExtinguishersUsed;
        public int SpareExtinguishers;
        public float FireHealth01;
        public float ExtinguisherCapacity01;
        public SprayHitQuality CurrentAimQuality;
        public float SweepDegrees;
        public bool UsedReplacement;
        public bool WaitingForReplacement;
        public bool HasHeldExtinguisher;
        public bool HeldExtinguisherIsEmpty;
        public bool NeedsExtinguisherPickup;
        public bool WaitingForSpatialPlacement;
        public SpatialPlacementSource PlacementSource;
    }
}
