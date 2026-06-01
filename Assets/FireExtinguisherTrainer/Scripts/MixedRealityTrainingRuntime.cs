using UnityEngine;

namespace FireExtinguisherTrainer
{
    [DisallowMultipleComponent]
    public class MixedRealityTrainingRuntime : MonoBehaviour
    {
        [SerializeField] private GameObject platformRoot;
        [SerializeField] private Camera centerEyeCamera;
        [SerializeField] private bool hidePlatformInMrRuntime = true;
        [SerializeField] private bool hideHandIndicatorsInMrRuntime = true;

        private void Awake()
        {
            ApplyTrackingStability();
            ConfigurePassthrough();
            ApplyRuntimePlatformVisibility();
        }

        private void Start()
        {
            ApplyTrackingStability();
        }

        public void Configure(GameObject fixedPlatform, Camera passthroughCamera = null)
        {
            platformRoot = fixedPlatform;
            centerEyeCamera = passthroughCamera;
        }

        public void ApplyTrackingStability()
        {
#if META_MR_SDK_INSTALLED
            foreach (OVRPlayerController playerController in FindObjectsByType<OVRPlayerController>(FindObjectsSortMode.None))
            {
                playerController.EnableLinearMovement = false;
                playerController.EnableRotation = false;
                playerController.HmdResetsY = false;
                playerController.HmdRotatesY = false;
                playerController.SetHaltUpdateMovement(true);
                playerController.SetMoveScaleMultiplier(0f);
            }

            OVRManager manager = FindFirstObjectByType<OVRManager>();
            if (manager != null)
            {
                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            }
#endif
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
            if (hideHandIndicatorsInMrRuntime && ShouldUseMrRuntimeView())
            {
                ApplyHandIndicatorVisibility(false);
            }
        }

        private void ConfigurePassthrough()
        {
            if (centerEyeCamera != null)
            {
                centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                centerEyeCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

#if META_MR_SDK_INSTALLED
            Meta.XR.MRUtilityKit.MRUK mruk = FindFirstObjectByType<Meta.XR.MRUtilityKit.MRUK>();
            if (mruk != null && mruk.EnableWorldLock)
            {
                mruk.EnableWorldLock = false;
                Debug.LogWarning("Disabled MRUK EnableWorldLock so it cannot move the OVRCameraRig tracking space.", this);
            }

            OVRManager manager = FindFirstObjectByType<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
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

        private static void ApplyHandIndicatorVisibility(bool visible)
        {
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!IsHandIndicator(renderer.transform))
                {
                    continue;
                }

                renderer.enabled = visible;
            }

            foreach (Collider collider in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (IsHandIndicator(collider.transform))
                {
                    collider.enabled = visible;
                }
            }
        }

        private static bool IsHandIndicator(Transform candidate)
        {
            return candidate != null &&
                   (candidate.name == "Left Hand Ball" ||
                    candidate.name == "Right Hand Ball");
        }
    }
}
