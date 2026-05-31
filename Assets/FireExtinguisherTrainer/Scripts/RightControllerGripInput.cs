using UnityEngine;

namespace FireExtinguisherTrainer
{
    public static class RightControllerGripInput
    {
        public const float PressedThreshold = QuestExtinguisherInput.PressedThreshold;

#if META_MR_SDK_INSTALLED
        public const OVRInput.RawAxis1D QuestRightGripRawAxis = QuestExtinguisherInput.RightGripRawAxis;
        public const OVRInput.Axis1D QuestRightGripVirtualAxis = QuestExtinguisherInput.RightGripVirtualAxis;
        public const OVRInput.Button QuestRightGripVirtualButton = QuestExtinguisherInput.RightGripVirtualButton;
        public const OVRInput.Controller QuestRightController = QuestExtinguisherInput.RightController;
#endif

        public static bool IsHeld(float threshold = PressedThreshold)
        {
            return QuestExtinguisherInput.RightGripHeld(threshold);
        }

        public static float ControllerGripValue()
        {
            return QuestExtinguisherInput.RightGripValue();
        }
    }
}
