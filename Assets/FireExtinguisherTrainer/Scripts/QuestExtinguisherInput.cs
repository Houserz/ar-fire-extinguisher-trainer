using UnityEngine;
using UnityEngine.InputSystem;

namespace FireExtinguisherTrainer
{
    public static class QuestExtinguisherInput
    {
        public const float PressedThreshold = 0.35f;

#if META_MR_SDK_INSTALLED
        public const OVRInput.RawAxis1D RightGripRawAxis = OVRInput.RawAxis1D.RHandTrigger;
        public const OVRInput.Axis1D RightGripVirtualAxis = OVRInput.Axis1D.PrimaryHandTrigger;
        public const OVRInput.Button RightGripVirtualButton = OVRInput.Button.PrimaryHandTrigger;
        public const OVRInput.Controller RightController = OVRInput.Controller.RTouch;

        public const OVRInput.RawAxis1D LeftGripRawAxis = OVRInput.RawAxis1D.LHandTrigger;
        public const OVRInput.Axis1D LeftGripVirtualAxis = OVRInput.Axis1D.PrimaryHandTrigger;
        public const OVRInput.Button LeftGripVirtualButton = OVRInput.Button.PrimaryHandTrigger;
        public const OVRInput.Controller LeftController = OVRInput.Controller.LTouch;

        public const OVRInput.RawAxis1D RightTriggerRawAxis = OVRInput.RawAxis1D.RIndexTrigger;
        public const OVRInput.Axis1D RightTriggerVirtualAxis = OVRInput.Axis1D.PrimaryIndexTrigger;
        public const OVRInput.Button RightTriggerVirtualButton = OVRInput.Button.PrimaryIndexTrigger;
#endif

        public static bool RightGripHeld(float threshold = PressedThreshold)
        {
            return KeyboardHeld(Key.G) || RightGripValue() > threshold || RightGripButtonHeld();
        }

        public static bool LeftGripHeld(float threshold = PressedThreshold)
        {
            return KeyboardHeld(Key.H) || LeftGripValue() > threshold || LeftGripButtonHeld();
        }

        public static bool RightTriggerHeld(float threshold = PressedThreshold)
        {
            return KeyboardHeld(Key.Space) ||
                   MouseHeld() ||
                   GamepadRightTriggerValue() > threshold ||
                   RightTriggerValue() > threshold ||
                   RightTriggerButtonHeld();
        }

        public static float RightGripValue()
        {
#if META_MR_SDK_INSTALLED
            return Mathf.Max(
                OVRInput.Get(RightGripRawAxis, RightController),
                OVRInput.Get(RightGripVirtualAxis, RightController));
#else
            return 0f;
#endif
        }

        public static float LeftGripValue()
        {
#if META_MR_SDK_INSTALLED
            return Mathf.Max(
                OVRInput.Get(LeftGripRawAxis, LeftController),
                OVRInput.Get(LeftGripVirtualAxis, LeftController));
#else
            return 0f;
#endif
        }

        public static float RightTriggerValue()
        {
#if META_MR_SDK_INSTALLED
            return Mathf.Max(
                OVRInput.Get(RightTriggerRawAxis, RightController),
                OVRInput.Get(RightTriggerVirtualAxis, RightController));
#else
            return 0f;
#endif
        }

        private static bool RightGripButtonHeld()
        {
#if META_MR_SDK_INSTALLED
            return OVRInput.Get(RightGripVirtualButton, RightController);
#else
            return false;
#endif
        }

        private static bool LeftGripButtonHeld()
        {
#if META_MR_SDK_INSTALLED
            return OVRInput.Get(LeftGripVirtualButton, LeftController);
#else
            return false;
#endif
        }

        private static bool RightTriggerButtonHeld()
        {
#if META_MR_SDK_INSTALLED
            return OVRInput.Get(RightTriggerVirtualButton, RightController);
#else
            return false;
#endif
        }

        private static bool KeyboardHeld(Key key)
        {
            return Keyboard.current != null && Keyboard.current[key].isPressed;
        }

        private static bool MouseHeld()
        {
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
        }

        private static float GamepadRightTriggerValue()
        {
            return Gamepad.current != null ? Gamepad.current.rightTrigger.ReadValue() : 0f;
        }
    }
}
