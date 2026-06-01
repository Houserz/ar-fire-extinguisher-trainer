using UnityEngine;

namespace FireExtinguisherTrainer
{
    public class FireSpawner : MonoBehaviour
    {
        [SerializeField] private FireTarget firePrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private bool spawnOnStart = false;
        [SerializeField] private SpatialTrainingPlacementManager spatialPlacement;

        public FireTarget CurrentFire { get; private set; }
        public SpatialTrainingPlacementManager SpatialPlacement => spatialPlacement;
        public SpatialPlacementSource LastPlacementSource { get; private set; } = SpatialPlacementSource.None;
        public string LastPlacementMessage { get; private set; } = "Fire has not spawned yet.";

        private void Start()
        {
            if (spawnOnStart)
            {
                SpawnRandomFire();
            }
        }

        public FireTarget SpawnRandomFire()
        {
            if (firePrefab == null)
            {
                LastPlacementMessage = "Fire did not spawn: FireSpawner needs a fire prefab.";
                Debug.LogError(LastPlacementMessage, this);
                return null;
            }

            if (spatialPlacement != null &&
                spatialPlacement.TryPrepareTrainingLayout(out SpatialTrainingLayout layout))
            {
                return SpawnFireAt(layout);
            }

            Transform spawnPoint = PickSpawnPoint();
            Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 2f;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            LastPlacementMessage = spawnPoint != null
                ? $"Using fixed fire spawn point {spawnPoint.name}."
                : "Using fixed fire fallback from FireSpawner transform.";

            return SpawnFireAt(position, rotation, SpatialPlacementSource.None);
        }

        public FireTarget SpawnFireAt(SpatialTrainingLayout layout)
        {
            spatialPlacement?.ApplyLayout(layout);
            LastPlacementMessage = string.IsNullOrWhiteSpace(layout.Message)
                ? $"Spatial training placement source: {layout.Source}."
                : layout.Message;
            float fireStationDistance = Vector3.Distance(
                new Vector3(layout.FirePose.position.x, 0f, layout.FirePose.position.z),
                new Vector3(layout.StationPose.position.x, 0f, layout.StationPose.position.z));
            Debug.Log(
                $"Spatial training placement {layout.Source}: fire={layout.FirePose.position:F2}, station={layout.StationPose.position:F2}, fireStationDistance={fireStationDistance:F2}m, message={LastPlacementMessage}",
                this);
            return SpawnFireAt(layout.FirePose.position, layout.FirePose.rotation, layout.Source);
        }

        public FireTarget SpawnFireAt(Pose pose)
        {
            return SpawnFireAt(pose.position, pose.rotation, SpatialPlacementSource.None);
        }

        private FireTarget SpawnFireAt(Vector3 position, Quaternion rotation, SpatialPlacementSource placementSource)
        {
            if (firePrefab == null)
            {
                LastPlacementMessage = "Fire did not spawn: FireSpawner needs a fire prefab.";
                Debug.LogError(LastPlacementMessage, this);
                return null;
            }

            if (CurrentFire != null)
            {
                Destroy(CurrentFire.gameObject);
            }

            LastPlacementSource = placementSource;
            CurrentFire = Instantiate(firePrefab, position, rotation, null);
            CurrentFire.name = "Active Training Fire";
            CurrentFire.transform.SetParent(null, true);
            CurrentFire.ResetFire();
            Debug.Log($"Spawned training fire at {position:F2}, source={placementSource}.", this);
            return CurrentFire;
        }

        private Transform PickSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return null;
            }

            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }
    }
}
