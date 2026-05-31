using UnityEngine;

namespace FireExtinguisherTrainer
{
    public class FireSpawner : MonoBehaviour
    {
        [SerializeField] private FireTarget firePrefab;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private bool spawnOnStart = false;

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

            Transform spawnPoint = PickSpawnPoint();
            Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position + transform.forward * 2f;
            Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

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
