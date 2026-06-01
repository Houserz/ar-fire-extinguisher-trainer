using UnityEngine;

namespace FireExtinguisherTrainer
{
    public class ExtinguisherStation : MonoBehaviour
    {
        [SerializeField] private ExtinguisherController extinguisherPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private Transform rightHandAnchor;
        [SerializeField] private FireTrainingManager trainingManager;
        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private float replacementDelaySeconds = 5f;
        [SerializeField] private ExtinguisherController availableExtinguisher;

        private bool waitingForEmptyDrop;
        private bool replacementQueued;
        private float replacementTimerSeconds;

        public ExtinguisherController AvailableExtinguisher => availableExtinguisher;
        public bool WaitingForEmptyDrop => waitingForEmptyDrop;
        public bool ReplacementQueued => replacementQueued;
        public string LastStationMessage { get; private set; } = "Station has not spawned an extinguisher yet.";

        private void Start()
        {
            if (spawnOnStart)
            {
                EnsureAvailableExtinguisher();
            }
        }

        private void Update()
        {
            TickReplacementTimer(Time.deltaTime);
        }

        public void Configure(
            ExtinguisherController prefab,
            Transform spawn,
            Transform handAnchor,
            FireTrainingManager manager)
        {
            extinguisherPrefab = prefab;
            spawnPoint = spawn;
            rightHandAnchor = handAnchor;
            trainingManager = manager;
            BindTrainingManager(manager);
        }

        public void BindTrainingManager(FireTrainingManager manager)
        {
            trainingManager = manager;
            if (availableExtinguisher == null)
            {
                return;
            }

            ExtinguisherHoldTracker tracker = availableExtinguisher.GetComponent<ExtinguisherHoldTracker>();
            if (tracker != null)
            {
                tracker.Configure(trainingManager, this, rightHandAnchor);
            }
        }

        public void SetAvailableExtinguisher(
            ExtinguisherController extinguisher,
            bool resetToFull = false)
        {
            replacementQueued = false;
            replacementTimerSeconds = 0f;
            availableExtinguisher = extinguisher;
            if (availableExtinguisher == null)
            {
                return;
            }

            if (resetToFull)
            {
                availableExtinguisher.ReplaceWithFullExtinguisher();
            }

            ConfigureSpawnedExtinguisher(availableExtinguisher);
            if (!availableExtinguisher.IsHeld)
            {
                PlaceAtSpawn(availableExtinguisher);
            }
        }

        public ExtinguisherController EnsureAvailableExtinguisher()
        {
            replacementQueued = false;
            replacementTimerSeconds = 0f;

            if (availableExtinguisher != null && !availableExtinguisher.IsHeld && !availableExtinguisher.IsEmpty)
            {
                PlaceAtSpawn(availableExtinguisher);
                ConfigureSpawnedExtinguisher(availableExtinguisher);
                LastStationMessage = $"Extinguisher ready at station spawn point {SpawnPointName}.";
                return availableExtinguisher;
            }

            if (extinguisherPrefab == null)
            {
                LastStationMessage = "Extinguisher did not spawn: ExtinguisherStation needs an extinguisher prefab.";
                Debug.LogError(LastStationMessage, this);
                return null;
            }

            Transform spawn = spawnPoint != null ? spawnPoint : transform;
            ExtinguisherController spawned = Instantiate(extinguisherPrefab, spawn.position, spawn.rotation, transform);
            spawned.name = "Station Extinguisher";
            spawned.ReplaceWithFullExtinguisher();
            ConfigureSpawnedExtinguisher(spawned);
            PlaceAtSpawn(spawned);
            availableExtinguisher = spawned;
            LastStationMessage = $"Spawned extinguisher at station spawn point {SpawnPointName}.";
            return availableExtinguisher;
        }

        public void RequestReplacementAfterDrop()
        {
            waitingForEmptyDrop = true;
        }

        public void NotifyPickedUp(ExtinguisherController extinguisher)
        {
            if (extinguisher == availableExtinguisher)
            {
                waitingForEmptyDrop = false;
                extinguisher.transform.SetParent(null, true);
                availableExtinguisher = null;
                QueueReplacement();
            }
        }

        public void NotifyReleased(ExtinguisherController extinguisher)
        {
            if (extinguisher != null && extinguisher.IsEmpty)
            {
                waitingForEmptyDrop = false;
            }
        }

        public void MoveStationToPose(Pose pose)
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (availableExtinguisher != null && !availableExtinguisher.IsHeld)
            {
                PlaceAtSpawn(availableExtinguisher);
            }
        }

        private void QueueReplacement()
        {
            if (replacementQueued || availableExtinguisher != null)
            {
                return;
            }

            replacementQueued = true;
            replacementTimerSeconds = Mathf.Max(0f, replacementDelaySeconds);
            TickReplacementTimer(0f);
        }

        private void TickReplacementTimer(float deltaTime)
        {
            if (!replacementQueued)
            {
                return;
            }

            if (availableExtinguisher != null)
            {
                replacementQueued = false;
                replacementTimerSeconds = 0f;
                return;
            }

            replacementTimerSeconds -= Mathf.Max(0f, deltaTime);
            if (replacementTimerSeconds > 0f)
            {
                return;
            }

            EnsureAvailableExtinguisher();
        }

#if UNITY_EDITOR
        public void DebugAdvanceReplacementTimer(float deltaTime)
        {
            TickReplacementTimer(deltaTime);
        }
#endif

        private void ConfigureSpawnedExtinguisher(ExtinguisherController spawned)
        {
            if (spawned.GetComponent<Rigidbody>() == null)
            {
                spawned.gameObject.AddComponent<Rigidbody>();
            }

            spawned.ConfigureRigidbodyPhysics();
            ExtinguisherCapacityGauge gauge = spawned.GetComponent<ExtinguisherCapacityGauge>();
            if (gauge == null)
            {
                gauge = spawned.gameObject.AddComponent<ExtinguisherCapacityGauge>();
            }

            gauge.ForceRefresh();

            var tracker = spawned.GetComponent<ExtinguisherHoldTracker>();
            if (tracker == null)
            {
                tracker = spawned.gameObject.AddComponent<ExtinguisherHoldTracker>();
            }

            tracker.Configure(trainingManager, this, rightHandAnchor);
        }

        private void PlaceAtSpawn(ExtinguisherController extinguisher)
        {
            if (extinguisher == null || spawnPoint == null)
            {
                LastStationMessage = "Extinguisher placement skipped: missing extinguisher or station spawn point.";
                return;
            }

            extinguisher.transform.position = spawnPoint.position;
            extinguisher.transform.rotation = spawnPoint.rotation;

            Rigidbody rigidbody = extinguisher.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                LastStationMessage = $"Extinguisher placed at {spawnPoint.position:F2}, but no Rigidbody was found.";
                return;
            }

            extinguisher.SetDockedPhysicsState(true);
            LastStationMessage = $"Extinguisher placed at {spawnPoint.position:F2}.";
        }

        private string SpawnPointName => spawnPoint != null ? spawnPoint.name : transform.name;
    }
}
