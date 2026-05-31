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
                Debug.LogWarning("FireSpawner needs a fire prefab.", this);
                return null;
            }

            if (CurrentFire != null)
            {
                Destroy(CurrentFire.gameObject);
            }

            Pose spatialPose = default;
            bool useSpatialPose = spatialPlacement != null;
            if (useSpatialPose)
            {
                useSpatialPose = spatialPlacement.TryPrepareTrainingLayout(out spatialPose);
            }
            Transform spawnPoint = useSpatialPose ? null : PickSpawnPoint();
            Vector3 position = useSpatialPose
                ? spatialPose.position
                : spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 2f;
            Quaternion rotation = useSpatialPose
                ? spatialPose.rotation
                : spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

            CurrentFire = Instantiate(firePrefab, position, rotation, null);
            CurrentFire.name = "Active Training Fire";
            CurrentFire.transform.SetParent(null, true);
            CurrentFire.ResetFire();
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
