using UnityEngine;

namespace FireExtinguisherTrainer
{
    [DisallowMultipleComponent]
    public class ExtinguisherCapacityGauge : MonoBehaviour
    {
        public const string GaugeRootName = "CapacityGauge";
        public const string NeedlePivotName = "CapacityGaugeNeedlePivot";
        public const string DialName = "Dial";
        public const string MountName = "GaugeMount";
        public const string NeedleName = "Needle";
        private static readonly Vector3 DefaultLocalPosition = new Vector3(-0.135f, 0.58f, 0f);
        private const string DialMaterialName = "Extinguisher_Gauge_Dial";
        private const string MountMaterialName = "Extinguisher_Gauge_Mount";
        private const string NeedleMaterialName = "Extinguisher_Gauge_Needle";

        [SerializeField] private ExtinguisherController controller;
        [SerializeField] private Transform gaugeRoot;
        [SerializeField] private Transform needlePivot;
        [SerializeField] private Renderer mountRenderer;
        [SerializeField] private Renderer dialRenderer;
        [SerializeField] private Renderer needleRenderer;
        [SerializeField] private Vector3 localPosition = new Vector3(-0.135f, 0.58f, 0f);
        [SerializeField] private Vector3 localRotationEuler = new Vector3(0f, -90f, 0f);
        [SerializeField] private float emptyAngleDegrees = -115f;
        [SerializeField] private float fullAngleDegrees = 115f;

        public Transform GaugeRoot => gaugeRoot;
        public Transform NeedlePivot => needlePivot;
        public Renderer MountRenderer => mountRenderer;
        public Renderer DialRenderer => dialRenderer;
        public Renderer NeedleRenderer => needleRenderer;

        private void Awake()
        {
            EnsureVisuals();
            UpdateGauge();
        }

        private void LateUpdate()
        {
            UpdateGauge();
        }

        public void ForceRefresh()
        {
            EnsureVisuals();
            UpdateGauge();
        }

        private void EnsureVisuals()
        {
            MigrateLegacySerializedValues();

            if (controller == null)
            {
                controller = GetComponent<ExtinguisherController>();
            }

            if (gaugeRoot == null)
            {
                Transform existing = transform.Find(GaugeRootName);
                gaugeRoot = existing != null ? existing : new GameObject(GaugeRootName).transform;
            }

            gaugeRoot.name = GaugeRootName;
            gaugeRoot.SetParent(transform, false);
            gaugeRoot.localPosition = localPosition;
            gaugeRoot.localRotation = Quaternion.Euler(localRotationEuler);
            gaugeRoot.localScale = Vector3.one;

            Transform mount = EnsureRoundDisc(MountName);
            mount.localPosition = new Vector3(0f, 0f, -0.006f);
            mount.localRotation = Quaternion.Euler(90f, 0f, 0f);
            mount.localScale = new Vector3(0.18f, 0.003f, 0.18f);
            DestroyCollider(mount.GetComponent<Collider>());
            mountRenderer = mount.GetComponent<Renderer>();
            if (mountRenderer != null &&
                (mountRenderer.sharedMaterial == null || mountRenderer.sharedMaterial.name != MountMaterialName))
            {
                mountRenderer.sharedMaterial = CreateMaterial(MountMaterialName, new Color(0.012f, 0.014f, 0.016f, 1f));
            }

            Transform dial = EnsureRoundDisc(DialName);
            dial.localPosition = new Vector3(0f, 0f, 0.001f);
            dial.localRotation = Quaternion.Euler(90f, 0f, 0f);
            dial.localScale = new Vector3(0.155f, 0.0015f, 0.155f);
            DestroyCollider(dial.GetComponent<Collider>());
            dialRenderer = dial.GetComponent<Renderer>();
            if (dialRenderer != null &&
                (dialRenderer.sharedMaterial == null || dialRenderer.sharedMaterial.name != DialMaterialName))
            {
                dialRenderer.sharedMaterial = CreateMaterial(DialMaterialName, new Color(0.035f, 0.04f, 0.045f, 1f));
            }

            if (needlePivot == null)
            {
                Transform existingNeedlePivot = gaugeRoot.Find(NeedlePivotName);
                needlePivot = existingNeedlePivot != null
                    ? existingNeedlePivot
                    : new GameObject(NeedlePivotName).transform;
            }

            needlePivot.name = NeedlePivotName;
            needlePivot.SetParent(gaugeRoot, false);
            needlePivot.localPosition = new Vector3(0f, 0f, 0.006f);
            needlePivot.localScale = Vector3.one;

            Transform needle = needlePivot.Find(NeedleName);
            if (needle == null)
            {
                GameObject needleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                needleObject.name = NeedleName;
                needle = needleObject.transform;
                needle.SetParent(needlePivot, false);
            }

            needle.localPosition = new Vector3(0f, 0.04f, 0f);
            needle.localRotation = Quaternion.identity;
            needle.localScale = new Vector3(0.012f, 0.082f, 0.0015f);
            DestroyCollider(needle.GetComponent<Collider>());
            needleRenderer = needle.GetComponent<Renderer>();
            if (needleRenderer != null &&
                (needleRenderer.sharedMaterial == null || needleRenderer.sharedMaterial.name != NeedleMaterialName))
            {
                needleRenderer.sharedMaterial = CreateMaterial(NeedleMaterialName, Color.green);
            }
        }

        private void MigrateLegacySerializedValues()
        {
            if (localPosition.x > -0.12f)
            {
                localPosition = DefaultLocalPosition;
            }
        }

        private Transform EnsureRoundDisc(string childName)
        {
            Transform child = gaugeRoot.Find(childName);
            if (child != null && !UsesPrimitiveMesh(child, "Cylinder"))
            {
                DestroyGameObject(child.gameObject);
                child = null;
            }

            if (child == null)
            {
                GameObject childObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                childObject.name = childName;
                child = childObject.transform;
                child.SetParent(gaugeRoot, false);
            }

            return child;
        }

        private static bool UsesPrimitiveMesh(Transform transform, string meshName)
        {
            MeshFilter meshFilter = transform.GetComponent<MeshFilter>();
            return meshFilter != null &&
                meshFilter.sharedMesh != null &&
                meshFilter.sharedMesh.name.Contains(meshName);
        }

        private void UpdateGauge()
        {
            if (needlePivot == null || needleRenderer == null)
            {
                EnsureVisuals();
            }

            float capacity = controller != null ? controller.Capacity01 : 0f;
            float angle = Mathf.Lerp(emptyAngleDegrees, fullAngleDegrees, capacity);
            if (needlePivot != null)
            {
                needlePivot.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (needleRenderer != null)
            {
                Color color = CapacityColor(capacity);
                Material material = needleRenderer.sharedMaterial;
                if (material == null)
                {
                    material = CreateMaterial(NeedleMaterialName, color);
                    needleRenderer.sharedMaterial = material;
                }

                if (material == null)
                {
                    return;
                }

                material.color = color;
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }
            }
        }

        private static Color CapacityColor(float capacity)
        {
            if (capacity < 0.5f)
            {
                return Color.Lerp(new Color(0.95f, 0.1f, 0.05f, 1f), new Color(1f, 0.82f, 0.08f, 1f), capacity / 0.5f);
            }

            return Color.Lerp(new Color(1f, 0.82f, 0.08f, 1f), new Color(0.1f, 0.85f, 0.22f, 1f), (capacity - 0.5f) / 0.5f);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.DontSave,
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        private static void DestroyCollider(Collider collider)
        {
            if (collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        private static void DestroyGameObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }
    }
}
