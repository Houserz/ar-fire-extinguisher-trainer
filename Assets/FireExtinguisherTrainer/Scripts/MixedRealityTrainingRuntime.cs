using UnityEngine;

namespace FireExtinguisherTrainer
{
    [DisallowMultipleComponent]
    public class MixedRealityTrainingRuntime : MonoBehaviour
    {
        [SerializeField] private GameObject platformRoot;
        [SerializeField] private Camera centerEyeCamera;
        [SerializeField] private bool hidePlatformInMrRuntime = true;

        private void Awake()
        {
            ConfigurePassthrough();
            ApplyRuntimePlatformVisibility();
        }

        public void Configure(GameObject fixedPlatform, Camera passthroughCamera = null)
        {
            platformRoot = fixedPlatform;
            centerEyeCamera = passthroughCamera;
        }

        public void ApplyPlatformVisibility(bool hide)
        {
            if (platformRoot == null)
            {
                return;
            }

            foreach (Renderer renderer in platformRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (IsFixedPlatformSurface(renderer.transform))
                {
                    renderer.enabled = !hide;
                }
            }

            foreach (Collider collider in platformRoot.GetComponentsInChildren<Collider>(true))
            {
                if (IsFixedPlatformSurface(collider.transform))
                {
                    collider.enabled = !hide;
                }
            }
        }

        private void ApplyRuntimePlatformVisibility()
        {
            ApplyPlatformVisibility(hidePlatformInMrRuntime && ShouldUseMrRuntimeView());
        }

        private void ConfigurePassthrough()
        {
            if (centerEyeCamera != null)
            {
                centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                centerEyeCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

#if META_MR_SDK_INSTALLED
            OVRManager manager = FindFirstObjectByType<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
            }

            OVRPassthroughLayer passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();
            if (passthroughLayer != null)
            {
                passthroughLayer.hidden = false;
                passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
                passthroughLayer.textureOpacity = 1f;
            }
#endif
        }

        private static bool ShouldUseMrRuntimeView()
        {
#if UNITY_EDITOR
            return false;
#elif UNITY_ANDROID
            return true;
#else
            return UnityEngine.XR.XRSettings.isDeviceActive;
#endif
        }

        private static bool IsFixedPlatformSurface(Transform candidate)
        {
            return candidate != null &&
                   (candidate.name == "Platform Floor" ||
                    candidate.name.StartsWith("Wall ", System.StringComparison.Ordinal));
        }
    }
}
