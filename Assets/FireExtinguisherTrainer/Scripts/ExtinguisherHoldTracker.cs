using Oculus.Interaction;
using UnityEngine;

namespace FireExtinguisherTrainer
{
    [RequireComponent(typeof(ExtinguisherController))]
    public class ExtinguisherHoldTracker : MonoBehaviour
    {
        [SerializeField] private FireTrainingManager trainingManager;
        [SerializeField] private ExtinguisherStation station;
        [SerializeField] private Transform holdAnchor;
        [SerializeField] private float fallbackPickupRadius = 0.55f;
        [SerializeField] private bool enableGripFallback = true;

        private ExtinguisherController extinguisher;
        private Grabbable grabbable;
        private GrabInteractable grabInteractable;
        private bool pointerSelected;
        private bool fallbackGripWasHeld;

        public ExtinguisherController Extinguisher => extinguisher;

        private void Awake()
        {
            EnsureComponentReferences();
        }

        private void OnEnable()
        {
            EnsureComponentReferences();
            if (grabInteractable != null)
            {
                grabInteractable.WhenPointerEventRaised += HandlePointerEventRaised;
            }
            else if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += HandlePointerEventRaised;
            }
        }

        private void OnDisable()
        {
            EnsureComponentReferences();
            if (grabInteractable != null)
            {
                grabInteractable.WhenPointerEventRaised -= HandlePointerEventRaised;
            }
            else if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= HandlePointerEventRaised;
            }
        }

        private void Update()
        {
            if (!enableGripFallback || pointerSelected || holdAnchor == null || extinguisher == null)
            {
                return;
            }

            bool gripHeld = GripHeld();
            if (gripHeld && !fallbackGripWasHeld && !extinguisher.IsHeld && IsWithinPickupRange())
            {
                PickUp(true);
            }
            else if (!gripHeld && fallbackGripWasHeld && extinguisher.IsHeld)
            {
                Release();
            }

            fallbackGripWasHeld = gripHeld;
        }

        public void Configure(
            FireTrainingManager manager,
            ExtinguisherStation owningStation,
            Transform rightHandAnchor)
        {
            EnsureComponentReferences();
            trainingManager = manager;
            station = owningStation;
            holdAnchor = rightHandAnchor;
        }

        public void SetGripFallbackEnabled(bool enabled)
        {
            enableGripFallback = enabled;
        }

        public void NotifyPhysicalGrabbed(Transform physicalHoldAnchor)
        {
            EnsureComponentReferences();
            holdAnchor = physicalHoldAnchor != null ? physicalHoldAnchor : holdAnchor;
            PickUp(false);
        }

        public void NotifyPhysicalReleased()
        {
            EnsureComponentReferences();
            Release();
        }

        public void DebugPickUp()
        {
            EnsureComponentReferences();
            PickUp(true);
        }

        public void DebugRelease()
        {
            EnsureComponentReferences();
            Release();
        }

        private void EnsureComponentReferences()
        {
            if (extinguisher == null)
            {
                extinguisher = GetComponent<ExtinguisherController>();
            }

            if (grabbable == null)
            {
                grabbable = GetComponent<Grabbable>();
            }

            if (grabInteractable == null)
            {
                grabInteractable = GetComponent<GrabInteractable>();
            }
        }

        private void HandlePointerEventRaised(PointerEvent pointerEvent)
        {
            if (pointerEvent.Type == PointerEventType.Select)
            {
                pointerSelected = true;
                PickUp(true);
            }
            else if (pointerEvent.Type == PointerEventType.Unselect ||
                     pointerEvent.Type == PointerEventType.Cancel)
            {
                pointerSelected = false;
                Release();
            }
        }

        private void PickUp(bool snapToAnchor)
        {
            if (extinguisher == null)
            {
                return;
            }

            extinguisher.MarkPickedUp(holdAnchor, snapToAnchor);
            trainingManager?.RegisterHeldExtinguisher(extinguisher);
            station?.NotifyPickedUp(extinguisher);
        }

        private void Release()
        {
            if (extinguisher == null)
            {
                return;
            }

            extinguisher.MarkReleased();
            trainingManager?.ReleaseHeldExtinguisher(extinguisher);
            station?.NotifyReleased(extinguisher);
        }

        private bool IsWithinPickupRange()
        {
            return Vector3.Distance(holdAnchor.position, transform.position) <= fallbackPickupRadius;
        }

        private static bool GripHeld()
        {
            return RightControllerGripInput.IsHeld();
        }
    }
}
