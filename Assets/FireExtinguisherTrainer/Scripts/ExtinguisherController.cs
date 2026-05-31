using TMPro;
using UnityEngine;

namespace FireExtinguisherTrainer
{
    public class ExtinguisherController : MonoBehaviour
    {
        public const string SafetyPinName = "SafetyPin";
        public const string SafetyPinRingName = "SafetyPinRing";
        public const string SafetyPinShaftName = "SafetyPinShaft";
        public const string SafetyPinLabelName = "SafetyPinLabel";

        private static readonly Vector3 SafetyPinHomeLocalPosition = new Vector3(0f, 1.04f, 0f);
        private static readonly Vector3 SafetyPinHomeLocalScale = Vector3.one;

        [SerializeField] private float capacitySeconds = 8f;
        [SerializeField] private Transform nozzle;
        [SerializeField] private ParticleSystem sprayParticles;
        [SerializeField] private LineRenderer sprayLine;
        [SerializeField] private Transform safetyPinVisual;
        [SerializeField] private Transform safetyPinLabel;
        [SerializeField] private bool useVrStableSprayLine = true;
        [SerializeField] private bool showSprayGuideLine = false;
        [SerializeField] private float sprayVisualLength = 3f;
        [SerializeField] private float sprayStartWidth = 0.05f;
        [SerializeField] private float sprayEndWidth = 0.24f;
        [SerializeField] private bool stabilizeRigidbody = true;
        [SerializeField] private Vector3 stableCenterOfMass = new Vector3(0f, 0.22f, 0f);
        [SerializeField] private float stableAngularDamping = 1.25f;
        [SerializeField] private float stableMaxAngularVelocity = 6f;

        private float remainingCapacity;
        private ParticleSystem configuredSprayParticles;

        public bool IsPinPulled { get; private set; }
        public bool IsSpraying { get; private set; }
        public bool IsHeld { get; private set; }
        public float RemainingCapacity => remainingCapacity;
        public float Capacity01 => capacitySeconds <= 0f ? 0f : Mathf.Clamp01(remainingCapacity / capacitySeconds);
        public float UsedCapacity => Mathf.Max(0f, capacitySeconds - remainingCapacity);
        public Transform Nozzle => nozzle != null ? nozzle : transform;
        public Transform SafetyPinVisual => safetyPinVisual;
        public Transform SafetyPinLabel => safetyPinLabel;
        public bool HasCapacity => remainingCapacity > 0f;
        public bool IsEmpty => !HasCapacity;

        private void Awake()
        {
            ConfigureRigidbodyPhysics();
            EnsureSafetyPinVisual();
            EnsureCapacityGauge();
            ConfigureSprayVisual();
            ResetExtinguisher();
        }

        private void LateUpdate()
        {
            if (IsSpraying)
            {
                UpdateSprayLinePositions();
            }
        }

        public void PullPin()
        {
            ResetSafetyPinTransform();
            IsPinPulled = true;
            SetSafetyPinVisible(false);
        }

        public void ResetExtinguisher()
        {
            IsPinPulled = false;
            remainingCapacity = capacitySeconds;
            ResetSafetyPinTransform();
            SetSafetyPinVisible(true);
            StopSpray();
        }

        public void ReplaceWithFullExtinguisher()
        {
            ResetExtinguisher();
        }

        public void ConfigureRigidbodyPhysics()
        {
            if (!stabilizeRigidbody)
            {
                return;
            }

            Rigidbody rigidbody = GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                return;
            }

            rigidbody.centerOfMass = stableCenterOfMass;
            rigidbody.angularDamping = Mathf.Max(rigidbody.angularDamping, stableAngularDamping);
            rigidbody.maxAngularVelocity = Mathf.Min(rigidbody.maxAngularVelocity, stableMaxAngularVelocity);
        }

        public void MarkPickedUp(Transform holdAnchor, bool snapToAnchor = true)
        {
            IsHeld = true;
        }

        public void MarkReleased(bool keepWorldTransform = true)
        {
            IsHeld = false;
            StopSpray();
        }

        public bool ConsumeSpray(float deltaTime)
        {
            if (!IsHeld || !IsPinPulled || remainingCapacity <= 0f)
            {
                StopSpray();
                return false;
            }

            remainingCapacity = Mathf.Max(0f, remainingCapacity - deltaTime);
            IsSpraying = true;
            UpdateSprayParticles(true);
            return true;
        }

        public void StopSpray()
        {
            IsSpraying = false;
            UpdateSprayParticles(false);
        }

        public void BeginSafetyPinDrag(Vector3 worldPosition)
        {
            EnsureSafetyPinVisual();
            SetSafetyPinVisible(true);
            UpdateSafetyPinDrag(worldPosition);
        }

        public void UpdateSafetyPinDrag(Vector3 worldPosition)
        {
            EnsureSafetyPinVisual();
            if (safetyPinVisual == null)
            {
                return;
            }

            safetyPinVisual.position = worldPosition;
            safetyPinVisual.rotation = transform.rotation;
        }

        public void CancelSafetyPinDrag()
        {
            if (IsPinPulled)
            {
                return;
            }

            ResetSafetyPinTransform();
            SetSafetyPinVisible(true);
        }

        private void EnsureSafetyPinVisual()
        {
            if (safetyPinVisual == null)
            {
                safetyPinVisual = transform.Find(SafetyPinName);
            }

            if (safetyPinVisual == null)
            {
                safetyPinVisual = new GameObject(SafetyPinName).transform;
                safetyPinVisual.SetParent(transform, false);
            }

            safetyPinVisual.name = SafetyPinName;
            safetyPinVisual.SetParent(transform, false);
            safetyPinVisual.localPosition = SafetyPinHomeLocalPosition;
            safetyPinVisual.localRotation = Quaternion.identity;
            safetyPinVisual.localScale = SafetyPinHomeLocalScale;
            DestroyComponent(safetyPinVisual.GetComponent<Collider>());
            DestroyComponent(safetyPinVisual.GetComponent<Renderer>());
            DestroyComponent(safetyPinVisual.GetComponent<MeshFilter>());

            Material pinMaterial = CreateSafetyPinMaterial();
            EnsureSafetyPinShaft(pinMaterial);
            EnsureSafetyPinRing(pinMaterial);
            EnsureSafetyPinLabel();
        }

        public void RefreshSafetyPinCue()
        {
            EnsureSafetyPinVisual();
        }

        private void EnsureSafetyPinShaft(Material material)
        {
            Transform shaft = safetyPinVisual.Find(SafetyPinShaftName);
            if (shaft == null)
            {
                GameObject shaftObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shaftObject.name = SafetyPinShaftName;
                shaft = shaftObject.transform;
                shaft.SetParent(safetyPinVisual, false);
            }

            shaft.localPosition = new Vector3(0.05f, 0f, 0f);
            shaft.localRotation = Quaternion.identity;
            shaft.localScale = new Vector3(0.42f, 0.026f, 0.026f);
            DestroyComponent(shaft.GetComponent<Collider>());
            Renderer renderer = shaft.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private void EnsureSafetyPinRing(Material material)
        {
            Transform ring = safetyPinVisual.Find(SafetyPinRingName);
            if (ring == null)
            {
                ring = new GameObject(SafetyPinRingName).transform;
                ring.SetParent(safetyPinVisual, false);
            }

            ring.localPosition = new Vector3(-0.28f, 0f, 0f);
            ring.localRotation = Quaternion.identity;
            ring.localScale = Vector3.one;

            const int segmentCount = 12;
            const float radius = 0.085f;
            for (int i = 0; i < segmentCount; i++)
            {
                string segmentName = $"RingSegment_{i:00}";
                Transform segment = ring.Find(segmentName);
                if (segment == null)
                {
                    GameObject segmentObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    segmentObject.name = segmentName;
                    segment = segmentObject.transform;
                    segment.SetParent(ring, false);
                }

                float angle = i * Mathf.PI * 2f / segmentCount;
                segment.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f);
                segment.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
                segment.localScale = new Vector3(0.05f, 0.016f, 0.018f);
                DestroyComponent(segment.GetComponent<Collider>());
                Renderer renderer = segment.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                }
            }
        }

        private void EnsureSafetyPinLabel()
        {
            Transform label = safetyPinVisual.Find(SafetyPinLabelName);
            if (label == null)
            {
                label = new GameObject(SafetyPinLabelName).transform;
                label.SetParent(safetyPinVisual, false);
            }

            label.localPosition = new Vector3(-0.28f, 0.135f, -0.015f);
            label.localRotation = Quaternion.Euler(65f, 0f, 0f);
            label.localScale = Vector3.one * 0.12f;
            TextMeshPro text = label.GetComponent<TextMeshPro>();
            if (text == null)
            {
                text = label.gameObject.AddComponent<TextMeshPro>();
            }

            text.text = "PULL PIN";
            text.fontSize = 0.34f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.95f, 0.25f, 1f);
            text.enableWordWrapping = false;
            text.richText = false;
            safetyPinLabel = label;
        }

        private void ResetSafetyPinTransform()
        {
            EnsureSafetyPinVisual();
            if (safetyPinVisual == null)
            {
                return;
            }

            safetyPinVisual.SetParent(transform, false);
            safetyPinVisual.localPosition = SafetyPinHomeLocalPosition;
            safetyPinVisual.localRotation = Quaternion.identity;
            safetyPinVisual.localScale = SafetyPinHomeLocalScale;
        }

        private void SetSafetyPinVisible(bool visible)
        {
            EnsureSafetyPinVisual();
            if (safetyPinVisual != null)
            {
                safetyPinVisual.gameObject.SetActive(visible);
            }
        }

        private void EnsureCapacityGauge()
        {
            if (GetComponent<ExtinguisherCapacityGauge>() == null)
            {
                gameObject.AddComponent<ExtinguisherCapacityGauge>();
            }
        }

        private Material FindSharedMaterial(string childName)
        {
            Transform child = transform.Find(childName);
            Renderer renderer = child != null ? child.GetComponent<Renderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }

        private static Material CreateSafetyPinMaterial()
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                color = new Color(0.92f, 0.86f, 0.18f, 1f),
                hideFlags = HideFlags.DontSave,
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", material.color);
            }

            return material;
        }

        private static void DestroyComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                DestroyImmediate(component);
            }
        }

        private void UpdateSprayParticles(bool shouldPlay)
        {
            if (useVrStableSprayLine)
            {
                if (sprayLine != null)
                {
                    sprayLine.enabled = shouldPlay && showSprayGuideLine;
                    if (shouldPlay)
                    {
                        UpdateSprayLinePositions();
                    }
                }

                SetSprayParticlesPlaying(shouldPlay);
                return;
            }

            if (sprayLine != null)
            {
                sprayLine.enabled = false;
            }

            SetSprayParticlesPlaying(shouldPlay);
        }

        private void SetSprayParticlesPlaying(bool shouldPlay)
        {
            if (sprayParticles == null)
            {
                return;
            }

            if (configuredSprayParticles != sprayParticles)
            {
                ConfigureSprayParticlesForVrComfort();
            }

            if (shouldPlay && !sprayParticles.isPlaying)
            {
                sprayParticles.Play();
            }
            else if (!shouldPlay && sprayParticles.isPlaying)
            {
                sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ConfigureSprayVisual()
        {
            ConfigureSprayParticlesForVrComfort();

            if (!useVrStableSprayLine)
            {
                return;
            }

            Transform lineParent = Nozzle;
            if (sprayLine == null)
            {
                sprayLine = lineParent.GetComponent<LineRenderer>();
                if (sprayLine == null)
                {
                    sprayLine = lineParent.gameObject.AddComponent<LineRenderer>();
                }
            }

            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Standard");

            if (sprayLine.sharedMaterial == null && shader != null)
            {
                Material material = new Material(shader)
                {
                    color = new Color(0.82f, 0.96f, 1f, 0.92f),
                    hideFlags = HideFlags.DontSave,
                };
                sprayLine.sharedMaterial = material;
            }

            sprayLine.useWorldSpace = true;
            sprayLine.positionCount = 2;
            sprayLine.startWidth = sprayStartWidth;
            sprayLine.endWidth = sprayEndWidth;
            sprayLine.startColor = new Color(0.92f, 0.99f, 1f, 1f);
            sprayLine.endColor = new Color(0.65f, 0.88f, 1f, 0.32f);
            sprayLine.numCapVertices = 4;
            sprayLine.numCornerVertices = 2;
            sprayLine.alignment = LineAlignment.TransformZ;
            sprayLine.textureMode = LineTextureMode.Stretch;
            sprayLine.enabled = false;
        }

        private void ConfigureSprayParticlesForVrComfort()
        {
            if (sprayParticles == null)
            {
                return;
            }

            ParticleSystem.MainModule main = sprayParticles.main;
            main.playOnAwake = false;
            main.startColor = new Color(0.82f, 0.96f, 1f, 0.56f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            main.maxParticles = 96;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = sprayParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 70f;

            ParticleSystem.ShapeModule shape = sprayParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.025f;

            VrStableParticleVisuals.ConfigureMeshParticleRenderer(sprayParticles, "Sphere.fbx");
            configuredSprayParticles = sprayParticles;

            if (!sprayParticles.isPlaying)
            {
                sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void UpdateSprayLinePositions()
        {
            if (sprayLine == null)
            {
                return;
            }

            Transform nozzleTransform = Nozzle;
            Vector3 start = nozzleTransform.position;
            Vector3 end = start + nozzleTransform.forward * sprayVisualLength;
            sprayLine.SetPosition(0, start);
            sprayLine.SetPosition(1, end);
        }
    }
}
