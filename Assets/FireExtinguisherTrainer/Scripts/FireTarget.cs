using UnityEngine;

namespace FireExtinguisherTrainer
{
    public class FireTarget : MonoBehaviour
    {
        private const string MainFlamePlaceholderName = "Flame Placeholder";

        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float damagePerSecond = 32f;
        [SerializeField] private float baseRadius = 0.35f;
        [SerializeField] private float bodyRadius = 0.75f;
        [SerializeField] private float maxSprayDistance = 4f;
        [SerializeField] private Transform baseTarget;
        [SerializeField] private ParticleSystem flameParticles;
        [SerializeField] private ParticleSystem smokeParticles;
        [SerializeField] private Light fireLight;
        [SerializeField] private bool useParticleEffects = true;
        [SerializeField] private bool useSmokeParticles = true;
        [SerializeField] private bool useBaseAimFeedback = true;
        [SerializeField] private bool lockWorldRotation = true;
        [SerializeField] private Vector3 flameLocalPosition = new Vector3(0f, 0.55f, 0f);
        [SerializeField] private Vector3 flameFullScale = new Vector3(0.9f, 1.35f, 0.9f);
        [SerializeField] private Vector3 flameMinimumScale = new Vector3(0.34f, 0.45f, 0.34f);
        [SerializeField] private float baseHitPulseSeconds = 0.18f;

        private float currentHealth;
        private float hitPulseTimer;
        private Vector3 initialScale;
        private Quaternion lockedWorldRotation;
        private Transform flameVisual;
        private Renderer[] flameRenderers;
        private Material flameMaterial;
        private MaterialPropertyBlock flamePropertyBlock;
        private Renderer baseFeedbackRenderer;
        private Material baseFeedbackMaterial;
        private MaterialPropertyBlock baseFeedbackBlock;
        private SprayHitQuality currentAimFeedback = SprayHitQuality.Miss;
        private bool aimFeedbackActive;

        public bool IsExtinguished => currentHealth <= 0f;
        public float Health01 => maxHealth <= 0f ? 0f : Mathf.Clamp01(currentHealth / maxHealth);
        public Transform BaseTarget => baseTarget != null ? baseTarget : transform;
        public Renderer BaseFeedbackRenderer => baseFeedbackRenderer;
        public SprayHitQuality CurrentAimFeedback => currentAimFeedback;
        public bool BaseFeedbackVisible => baseFeedbackRenderer != null && baseFeedbackRenderer.enabled;

        private void Awake()
        {
            initialScale = transform.localScale;
            EnsureFlameVisual();
            EnsureBaseFeedbackVisual();
            LockToWorldIfNeeded();
            ResetFire();
        }

        private void LateUpdate()
        {
            if (lockWorldRotation)
            {
                transform.rotation = lockedWorldRotation;
            }

            if (hitPulseTimer > 0f)
            {
                hitPulseTimer = Mathf.Max(0f, hitPulseTimer - Time.deltaTime);
                ApplyBaseFeedbackColor();
            }
        }

        public void ResetFire()
        {
            if (initialScale == Vector3.zero)
            {
                initialScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
            }

            if (flameVisual == null)
            {
                EnsureFlameVisual();
            }

            EnsureBaseFeedbackVisual();
            ConfigureParticleRenderersForVrComfort();
            currentHealth = maxHealth;
            hitPulseTimer = 0f;
            transform.localScale = initialScale == Vector3.zero ? Vector3.one : initialScale;
            UpdateVisuals();
            SetBaseFeedbackVisible(true);
            SetAimFeedback(SprayHitQuality.Miss, false);

            if (useParticleEffects && flameParticles != null && !flameParticles.isPlaying)
            {
                flameParticles.Play();
            }

            if (useParticleEffects && useSmokeParticles && smokeParticles != null && !smokeParticles.isPlaying)
            {
                smokeParticles.Play();
            }
        }

        public SprayHitQuality EvaluateAim(Ray sprayRay, out float distanceToBase)
        {
            distanceToBase = float.PositiveInfinity;

            Vector3 direction = sprayRay.direction.normalized;
            Vector3 origin = sprayRay.origin;
            Vector3 basePoint = BaseTarget.position;
            float baseForwardDistance = Vector3.Dot(basePoint - origin, direction);

            if (baseForwardDistance < 0f || baseForwardDistance > maxSprayDistance)
            {
                return SprayHitQuality.Miss;
            }

            Vector3 closestBasePoint = origin + direction * baseForwardDistance;
            distanceToBase = Vector3.Distance(closestBasePoint, basePoint);

            if (distanceToBase <= baseRadius)
            {
                return SprayHitQuality.BaseHit;
            }

            Vector3 bodyPoint = transform.position + transform.up * 0.45f;
            float bodyForwardDistance = Vector3.Dot(bodyPoint - origin, direction);
            if (bodyForwardDistance >= 0f && bodyForwardDistance <= maxSprayDistance)
            {
                Vector3 closestBodyPoint = origin + direction * bodyForwardDistance;
                if (Vector3.Distance(closestBodyPoint, bodyPoint) <= bodyRadius)
                {
                    return SprayHitQuality.WrongArea;
                }
            }

            return SprayHitQuality.Miss;
        }

        public void SetAimFeedback(SprayHitQuality quality, bool active)
        {
            currentAimFeedback = quality;
            if (IsExtinguished)
            {
                aimFeedbackActive = false;
                hitPulseTimer = 0f;
                SetBaseFeedbackVisible(false);
                return;
            }

            aimFeedbackActive = active;
            EnsureBaseFeedbackVisual();
            SetBaseFeedbackVisible(true);
            ApplyBaseFeedbackColor();
        }

        public void SetBaseFeedbackVisible(bool visible)
        {
            if (!useBaseAimFeedback)
            {
                return;
            }

            EnsureBaseFeedbackVisual();
            if (baseFeedbackRenderer != null)
            {
                baseFeedbackRenderer.enabled = visible;
            }
        }

        public bool ApplySpray(Ray sprayRay, float deltaTime, out SprayHitQuality hitQuality)
        {
            hitQuality = EvaluateAim(sprayRay, out _);
            if (hitQuality != SprayHitQuality.BaseHit || IsExtinguished)
            {
                return false;
            }

            hitPulseTimer = baseHitPulseSeconds;
            currentHealth = Mathf.Max(0f, currentHealth - damagePerSecond * deltaTime);
            UpdateVisuals();
            ApplyBaseFeedbackColor();
            return true;
        }

        private void UpdateVisuals()
        {
            float health = Health01;
            transform.localScale = initialScale == Vector3.zero ? Vector3.one : initialScale;
            UpdateMeshFlame(health);

            if (useParticleEffects)
            {
                SetEmissionRate(flameParticles, Mathf.Lerp(0f, 24f, health));
                if (useSmokeParticles)
                {
                    SetEmissionRate(smokeParticles, Mathf.Lerp(0f, 8f, health));
                }
                else
                {
                    StopAndClear(smokeParticles);
                }
            }
            else
            {
                StopAndClear(flameParticles);
                StopAndClear(smokeParticles);
            }

            if (fireLight != null)
            {
                fireLight.intensity = Mathf.Lerp(0f, 3.4f, health);
                fireLight.range = Mathf.Lerp(0.4f, 2.8f, health);
            }

            if (IsExtinguished)
            {
                aimFeedbackActive = false;
                hitPulseTimer = 0f;
                SetBaseFeedbackVisible(false);

                if (flameVisual != null)
                {
                    flameVisual.gameObject.SetActive(false);
                }

                if (flameParticles != null)
                {
                    flameParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                if (smokeParticles != null)
                {
                    smokeParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private static void SetEmissionRate(ParticleSystem particleSystem, float rate)
        {
            if (particleSystem == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = rate;
        }

        private static void StopAndClear(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return;
            }

            if (particleSystem.isPlaying)
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ConfigureParticleRenderersForVrComfort()
        {
            VrStableParticleVisuals.ConfigureMeshParticleRenderer(flameParticles, "Capsule.fbx");
            VrStableParticleVisuals.ConfigureMeshParticleRenderer(smokeParticles, "Sphere.fbx");
        }

        private void LockToWorldIfNeeded()
        {
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            Vector3 euler = transform.rotation.eulerAngles;
            lockedWorldRotation = Quaternion.Euler(0f, euler.y, 0f);
        }

        private void EnsureFlameVisual()
        {
            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter filter in filters)
            {
                if (filter.name.Contains("Flame") && filter.sharedMesh != null)
                {
                    flameVisual = filter.transform;
                    break;
                }
            }

            if (flameVisual == null)
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fallback.name = "Flame Mesh";
                fallback.transform.SetParent(transform, false);
                fallback.transform.localPosition = flameLocalPosition;
                fallback.transform.localRotation = Quaternion.identity;
                fallback.transform.localScale = flameFullScale;

                Collider collider = fallback.GetComponent<Collider>();
                if (collider != null)
                {
                    DestroyComponent(collider);
                }

                flameVisual = fallback.transform;
            }

            flameRenderers = flameVisual != null
                ? flameVisual.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];

            flamePropertyBlock = new MaterialPropertyBlock();
            EnsureBrightFlameMaterial();
            ForceFlameRendererVisible();
        }

        private void EnsureBaseFeedbackVisual()
        {
            if (!useBaseAimFeedback)
            {
                return;
            }

            Transform target = BaseTarget;
            if (baseTarget != null &&
                baseFeedbackRenderer != null &&
                baseFeedbackRenderer.transform != baseTarget &&
                !baseFeedbackRenderer.transform.IsChildOf(baseTarget))
            {
                baseFeedbackRenderer = null;
                baseFeedbackMaterial = null;
            }

            if (baseFeedbackRenderer == null && baseTarget != null)
            {
                baseFeedbackRenderer = baseTarget.GetComponent<Renderer>();
            }

            if (baseFeedbackRenderer == null)
            {
                Transform feedback = target.Find("Base Aim Feedback");
                if (feedback == null)
                {
                    GameObject feedbackObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    feedbackObject.name = "Base Aim Feedback";
                    feedback = feedbackObject.transform;
                    feedback.SetParent(target, false);
                }

                feedback.localPosition = Vector3.zero;
                feedback.localRotation = Quaternion.identity;
                feedback.localScale = new Vector3(0.75f, 0.02f, 0.75f);

                Collider collider = feedback.GetComponent<Collider>();
                if (collider != null)
                {
                    DestroyComponent(collider);
                }

                baseFeedbackRenderer = feedback.GetComponent<Renderer>();
            }

            if (baseFeedbackRenderer == null)
            {
                return;
            }

            baseFeedbackRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            baseFeedbackRenderer.receiveShadows = false;

            if (baseFeedbackMaterial == null)
            {
                baseFeedbackMaterial = CreateBaseFeedbackMaterial(BaseFeedbackColor(SprayHitQuality.Miss, false, false));
            }

            if (baseFeedbackMaterial != null && baseFeedbackRenderer.sharedMaterial != baseFeedbackMaterial)
            {
                baseFeedbackRenderer.sharedMaterial = baseFeedbackMaterial;
            }

            if (baseFeedbackBlock == null)
            {
                baseFeedbackBlock = new MaterialPropertyBlock();
            }
        }

        private void ApplyBaseFeedbackColor()
        {
            if (!useBaseAimFeedback || baseFeedbackRenderer == null)
            {
                return;
            }

            bool pulse = hitPulseTimer > 0f && currentAimFeedback == SprayHitQuality.BaseHit && aimFeedbackActive;
            Color color = BaseFeedbackColor(currentAimFeedback, aimFeedbackActive, pulse);
            if (baseFeedbackMaterial != null)
            {
                baseFeedbackMaterial.color = color;
                if (baseFeedbackMaterial.HasProperty("_BaseColor"))
                {
                    baseFeedbackMaterial.SetColor("_BaseColor", color);
                }
            }

            baseFeedbackRenderer.GetPropertyBlock(baseFeedbackBlock);
            baseFeedbackBlock.SetColor("_BaseColor", color);
            baseFeedbackBlock.SetColor("_Color", color);
            baseFeedbackBlock.SetColor("_EmissionColor", color * (pulse ? 2.6f : 1.4f));
            baseFeedbackRenderer.SetPropertyBlock(baseFeedbackBlock);
        }

        private static Color BaseFeedbackColor(SprayHitQuality quality, bool active, bool pulse)
        {
            if (!active)
            {
                return new Color(0.05f, 0.9f, 1f, 0.45f);
            }

            if (pulse)
            {
                return new Color(0.8f, 1f, 0.92f, 0.9f);
            }

            switch (quality)
            {
                case SprayHitQuality.BaseHit:
                    return new Color(0.14f, 1f, 0.22f, 0.78f);
                case SprayHitQuality.WrongArea:
                    return new Color(1f, 0.32f, 0.05f, 0.78f);
                default:
                    return new Color(0.05f, 0.9f, 1f, 0.55f);
            }
        }

        private static Material CreateBaseFeedbackMaterial(Color color)
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
                name = "Fire_Base_Aim_Feedback",
                color = color,
                hideFlags = HideFlags.DontSave,
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        private void UpdateMeshFlame(float health)
        {
            if (flameVisual == null)
            {
                return;
            }

            flameVisual.gameObject.SetActive(health > 0f);
            flameVisual.localPosition = flameLocalPosition;
            flameVisual.localRotation = Quaternion.identity;
            flameVisual.localScale = Vector3.Lerp(flameMinimumScale, flameFullScale, health);
            ForceFlameRendererVisible();

            if (flamePropertyBlock == null)
            {
                flamePropertyBlock = new MaterialPropertyBlock();
            }

            Color color = Color.Lerp(
                new Color(1f, 0.82f, 0.08f, 1f),
                new Color(1f, 0.12f, 0.01f, 1f),
                health);

            foreach (Renderer renderer in flameRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(flamePropertyBlock);
                flamePropertyBlock.SetColor("_BaseColor", color);
                flamePropertyBlock.SetColor("_Color", color);
                flamePropertyBlock.SetColor("_EmissionColor", color * 2.4f);
                renderer.SetPropertyBlock(flamePropertyBlock);
            }
        }

        private void EnsureBrightFlameMaterial()
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Standard");

            if (shader != null)
            {
                flameMaterial = new Material(shader)
                {
                    color = new Color(1f, 0.18f, 0.01f, 1f),
                    hideFlags = HideFlags.DontSave,
                };

                if (flameMaterial.HasProperty("_BaseColor"))
                {
                    flameMaterial.SetColor("_BaseColor", new Color(1f, 0.18f, 0.01f, 1f));
                }
            }

            foreach (Renderer renderer in flameRenderers)
            {
                if (renderer != null && flameMaterial != null)
                {
                    renderer.sharedMaterial = flameMaterial;
                }
            }
        }

        private void ForceFlameRendererVisible()
        {
            foreach (Renderer renderer in flameRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = !IsMainFlamePlaceholderRenderer(renderer);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private bool IsMainFlamePlaceholderRenderer(Renderer renderer)
        {
            return renderer.transform == flameVisual &&
                renderer.name == MainFlamePlaceholderName;
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

        private void OnDrawGizmosSelected()
        {
            Transform target = BaseTarget;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(target.position, baseRadius);

            Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position + transform.up * 0.45f, bodyRadius);
        }
    }
}
