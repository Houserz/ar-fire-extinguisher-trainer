using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FireExtinguisherTrainer
{
    public enum HudAnchorMode
    {
        WorldLocked = 0,
        HeadLocked = 1,
    }

    public class TrainingHUD : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI stepText;
        [SerializeField] private TextMeshProUGUI checklistText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI resultText;
        [SerializeField] private TextMeshProUGUI introText;
        [SerializeField] private Slider extinguisherSlider;
        [SerializeField] private Slider fireSlider;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private GameObject introPanel;
        [SerializeField] private HudAnchorMode anchorMode = HudAnchorMode.WorldLocked;
        [SerializeField] private bool detachFromCameraRigOnStart = false;
        [SerializeField] private bool followHeadsetInRuntime = false;
        [SerializeField] private Transform headsetAnchor;
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, -0.18f, 1.25f);
        [SerializeField] private Vector3 headsetLocalEulerAngles = Vector3.zero;
        [SerializeField] private float headsetLocalScale = 0.00125f;

        public bool IntroVisible => introPanel != null && introPanel.activeSelf;
        public HudAnchorMode AnchorMode => anchorMode;

        private bool worldLockedPoseInitialized;

        private void Start()
        {
            ResolveReferencesIfNeeded();
            if (anchorMode == HudAnchorMode.HeadLocked)
            {
                if (followHeadsetInRuntime)
                {
                    ApplyHeadLockedPose();
                }
                else
                {
                    DetachFromHeadsetIfNeeded();
                }
            }
            else
            {
                ApplyWorldLockedPose();
            }
        }

        private void LateUpdate()
        {
            if (anchorMode == HudAnchorMode.HeadLocked && followHeadsetInRuntime)
            {
                ApplyHeadLockedPose();
            }
            else if (anchorMode == HudAnchorMode.WorldLocked && !worldLockedPoseInitialized)
            {
                ApplyWorldLockedPose();
            }
        }

        public void ConfigureHeadLocked(Transform anchor, Camera worldCamera = null)
        {
            anchorMode = HudAnchorMode.HeadLocked;
            headsetAnchor = anchor;
            followHeadsetInRuntime = true;
            detachFromCameraRigOnStart = false;
            worldLockedPoseInitialized = false;
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null && worldCamera != null)
            {
                canvas.worldCamera = worldCamera;
            }

            ApplyHeadLockedPose();
        }

        public void ConfigureWorldLocked(Transform reference, Camera worldCamera = null)
        {
            anchorMode = HudAnchorMode.WorldLocked;
            Transform resolvedReference = reference != null
                ? reference
                : worldCamera != null
                    ? worldCamera.transform
                    : null;
            headsetAnchor = resolvedReference;
            followHeadsetInRuntime = false;
            detachFromCameraRigOnStart = false;
            worldLockedPoseInitialized = false;
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null && worldCamera != null)
            {
                canvas.worldCamera = worldCamera;
            }

            ApplyWorldLockedPose(resolvedReference);
        }

        public bool ApplyWorldLockedPose(Transform reference = null)
        {
            Transform anchor = reference != null ? reference : ResolveHeadsetAnchor();
            if (anchor == null)
            {
                return false;
            }

            Vector3 forward = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 position = anchor.position +
                right * headsetLocalPosition.x +
                Vector3.up * headsetLocalPosition.y +
                forward * Mathf.Max(0.01f, headsetLocalPosition.z);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) *
                Quaternion.Euler(headsetLocalEulerAngles);

            transform.SetParent(null, true);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = Vector3.one * Mathf.Max(0.0001f, headsetLocalScale);
            worldLockedPoseInitialized = true;
            return true;
        }

        public void ApplyHeadLockedPose()
        {
            Transform anchor = ResolveHeadsetAnchor();
            if (anchor == null)
            {
                return;
            }

            if (transform.parent != anchor)
            {
                transform.SetParent(anchor, false);
            }

            transform.localPosition = headsetLocalPosition;
            transform.localRotation = Quaternion.Euler(headsetLocalEulerAngles);
            transform.localScale = Vector3.one * Mathf.Max(0.0001f, headsetLocalScale);
            worldLockedPoseInitialized = false;
        }

        public void SetRunning(
            PassStep currentStep,
            string status,
            float extinguisherCapacity01,
            float fireHealth01,
            int mistakes,
            int spareExtinguishers,
            float elapsedSeconds)
        {
            var report = new TrainingSessionReport
            {
                CurrentStep = currentStep,
                Status = status,
                ExtinguisherCapacity01 = extinguisherCapacity01,
                FireHealth01 = fireHealth01,
                Mistakes = mistakes,
                SpareExtinguishers = spareExtinguishers,
                ElapsedSeconds = elapsedSeconds,
                InstructionText = status,
            };

            SetRunning(report);
        }

        public void SetRunning(TrainingSessionReport report)
        {
            ResolveReferencesIfNeeded();

            if (stepText != null)
            {
                PrepareText(stepText, 18f, 32f);
                stepText.text = $"PASS: {StepLabel(report.CurrentStep)}";
            }

            if (checklistText != null)
            {
                PrepareText(checklistText, 13f, 23f);
                checklistText.richText = true;
                checklistText.text = BuildChecklist(report);
            }

            if (statusText != null)
            {
                PrepareText(statusText, 14f, 22f);
                string instruction = string.IsNullOrEmpty(report.InstructionText)
                    ? report.Status
                    : report.InstructionText;
                string statusBlock = instruction == report.Status
                    ? report.Status
                    : $"{instruction}\n{report.Status}";
                statusText.text =
                    $"{statusBlock}\n" +
                    $"Time {report.ElapsedSeconds:0.0}s  Mistakes {report.Mistakes}  " +
                    $"Fire {report.FireHealth01 * 100f:0}%  Bottle {report.ExtinguisherCapacity01 * 100f:0}%  " +
                    $"Spare {report.SpareExtinguishers}";
            }

            if (extinguisherSlider != null)
            {
                extinguisherSlider.value = report.ExtinguisherCapacity01;
            }

            if (fireSlider != null)
            {
                fireSlider.value = report.FireHealth01;
            }

            if (resultPanel != null)
            {
                resultPanel.SetActive(false);
            }
        }

        public void ShowResult(
            TrainingOutcome outcome,
            float elapsedSeconds,
            int mistakes,
            float accuracy01,
            float extinguisherUsedSeconds,
            int extinguishersUsed)
        {
            var report = new TrainingSessionReport
            {
                Outcome = outcome,
                ResultReason = outcome == TrainingOutcome.Success
                    ? "Fire extinguished."
                    : "Training ended before the fire was extinguished.",
                ElapsedSeconds = elapsedSeconds,
                Mistakes = mistakes,
                AimingAccuracy01 = accuracy01,
                ExtinguisherUsedSeconds = extinguisherUsedSeconds,
                ExtinguishersUsed = extinguishersUsed,
                MistakeBreakdown = mistakes == 0 ? "No recorded mistakes" : "See run feedback",
            };

            ShowResult(report);
        }

        public void ShowResult(TrainingSessionReport report)
        {
            ResolveReferencesIfNeeded();

            string title = report.Outcome == TrainingOutcome.Success ? "Training Complete" : "Training Failed";
            string reasonLabel = report.Outcome == TrainingOutcome.Success ? "Result" : "Failure reason";
            string mistakeBreakdown = string.IsNullOrEmpty(report.MistakeBreakdown)
                ? "No recorded mistakes"
                : report.MistakeBreakdown;
            string resultMessage =
                $"{title}\n" +
                $"{reasonLabel}: {report.ResultReason}\n" +
                $"Completion time: {report.ElapsedSeconds:0.0}s\n" +
                $"Aiming accuracy: {report.AimingAccuracy01 * 100f:0}%\n" +
                $"Mistakes: {report.Mistakes}\n" +
                $"Mistake types: {mistakeBreakdown}\n" +
                $"Spray time: {report.TotalSprayTimeSeconds:0.0}s total / {report.AccurateSprayTimeSeconds:0.0}s accurate\n" +
                $"Extinguisher used: {report.ExtinguisherUsedSeconds:0.0}s across {report.ExtinguishersUsed} bottle(s)\n" +
                $"Replacement used: {(report.UsedReplacement ? "Yes" : "No")}\n" +
                "Press A / Enter to restart.";

            if (resultPanel != null)
            {
                resultPanel.SetActive(true);
            }

            if (stepText != null)
            {
                PrepareText(stepText, 18f, 30f);
                stepText.text = title;
            }

            if (statusText != null)
            {
                PrepareText(statusText, 14f, 20f);
                statusText.text = $"{report.ResultReason}\nPress A / Enter to restart.";
            }

            if (resultText != null)
            {
                PrepareText(resultText, 13f, 21f);
                resultText.gameObject.SetActive(true);
                resultText.enabled = true;
                resultText.text = resultMessage;
                resultText.ForceMeshUpdate();
            }
            else if (statusText != null)
            {
                statusText.text = resultMessage;
            }
        }

        public void ShowIntro()
        {
            ResolveReferencesIfNeeded();
            EnsureIntroPanel();

            if (introPanel != null)
            {
                introPanel.SetActive(true);
            }

            if (introText != null)
            {
                PrepareText(introText, 16f, 28f);
                introText.alignment = TextAlignmentOptions.Center;
                introText.text =
                    "Fire Extinguisher Trainer\n\n" +
                    "1. Pick up the extinguisher with the right grip.\n" +
                    "2. Pull the yellow safety ring with the left grip.\n" +
                    "3. Aim at the base until the target turns green.\n" +
                    "4. Hold trigger and sweep side to side.\n\n" +
                    "Press A / trigger / Enter to begin.";
            }

            if (resultPanel != null)
            {
                resultPanel.SetActive(false);
            }
        }

        public void HideIntro()
        {
            ResolveReferencesIfNeeded();
            if (introPanel != null)
            {
                introPanel.SetActive(false);
            }
        }

        private void EnsureIntroPanel()
        {
            if (introPanel == null)
            {
                var panelObject = new GameObject("Intro Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panelObject.transform.SetParent(transform, false);

                RectTransform panelRect = panelObject.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.anchoredPosition = Vector2.zero;
                panelRect.sizeDelta = new Vector2(760f, 390f);

                Image background = panelObject.GetComponent<Image>();
                background.color = new Color(0.02f, 0.025f, 0.03f, 0.86f);
                background.raycastTarget = false;
                introPanel = panelObject;
            }

            if (introText == null && introPanel != null)
            {
                var textObject = new GameObject("Intro Text", typeof(RectTransform));
                textObject.transform.SetParent(introPanel.transform, false);

                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(32f, 28f);
                textRect.offsetMax = new Vector2(-32f, -28f);

                introText = textObject.AddComponent<TextMeshProUGUI>();
                introText.color = Color.white;
                introText.raycastTarget = false;
                AssignDefaultFontIfAvailable(introText);
            }
        }

        private static void PrepareText(TextMeshProUGUI text, float minimumSize, float maximumSize)
        {
            AssignDefaultFontIfAvailable(text);
            text.enableAutoSizing = true;
            text.fontSizeMin = minimumSize;
            text.fontSizeMax = maximumSize;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
        }

        private static string StepLabel(PassStep step)
        {
            switch (step)
            {
                case PassStep.PullPin:
                    return "Pull";
                case PassStep.AimAtBase:
                    return "Aim at base";
                case PassStep.SqueezeHandle:
                    return "Squeeze";
                case PassStep.SweepSideToSide:
                    return "Sweep";
                case PassStep.Completed:
                    return "Complete";
                default:
                    return step.ToString();
            }
        }

        private static string BuildChecklist(TrainingSessionReport report)
        {
            int activeIndex = ActiveChecklistIndex(report);
            return
                "PASS CHECKLIST\n" +
                ChecklistLine(0, activeIndex, activeIndex > 0, "Pick up", "Right grip") + "\n" +
                ChecklistLine(1, activeIndex, IsStepComplete(report, PassStep.PullPin), "Pull pin", "Left grip ring") + "\n" +
                ChecklistLine(2, activeIndex, IsStepComplete(report, PassStep.AimAtBase), "Aim at base", AimHint(report.CurrentAimQuality)) + "\n" +
                ChecklistLine(3, activeIndex, IsStepComplete(report, PassStep.SqueezeHandle), "Squeeze", "Hold trigger") + "\n" +
                ChecklistLine(4, activeIndex, IsStepComplete(report, PassStep.SweepSideToSide), "Sweep", "Side to side");
        }

        private static string ChecklistLine(
            int index,
            int activeIndex,
            bool complete,
            string label,
            string hint)
        {
            string marker = complete ? "[x]" : index == activeIndex ? "[>]" : "[ ]";
            string line = $"{marker} {label}: {hint}";
            if (index == activeIndex)
            {
                return $"<color=#7CFF72>{line}</color>";
            }

            if (complete)
            {
                return $"<color=#C9D6D6>{line}</color>";
            }

            return $"<color=#FFFFFF>{line}</color>";
        }

        private static int ActiveChecklistIndex(TrainingSessionReport report)
        {
            if (report.Outcome != TrainingOutcome.Running || report.CurrentStep == PassStep.Completed)
            {
                return 5;
            }

            if (report.NeedsExtinguisherPickup)
            {
                return 0;
            }

            switch (report.CurrentStep)
            {
                case PassStep.PullPin:
                    return 1;
                case PassStep.AimAtBase:
                    return 2;
                case PassStep.SqueezeHandle:
                    return 3;
                case PassStep.SweepSideToSide:
                    return 4;
                default:
                    return 0;
            }
        }

        private static bool IsStepComplete(TrainingSessionReport report, PassStep step)
        {
            return report.CurrentStep > step || report.CurrentStep == PassStep.Completed;
        }

        private static string AimHint(SprayHitQuality quality)
        {
            switch (quality)
            {
                case SprayHitQuality.BaseHit:
                    return "Green target";
                case SprayHitQuality.WrongArea:
                    return "Aim lower";
                default:
                    return "Find base";
            }
        }

        private void DetachFromHeadsetIfNeeded()
        {
            if (!detachFromCameraRigOnStart || transform.parent == null)
            {
                return;
            }

            Transform current = transform.parent;
            while (current != null)
            {
                if (current.GetComponent<Camera>() != null ||
                    current.name.Contains("EyeAnchor") ||
                    current.name.Contains("OVRCameraRig"))
                {
                    transform.SetParent(null, true);
                    return;
                }

                current = current.parent;
            }
        }

        private Transform ResolveHeadsetAnchor()
        {
            if (headsetAnchor != null)
            {
                return headsetAnchor;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                headsetAnchor = mainCamera.transform;
                return headsetAnchor;
            }

            return null;
        }

        private static void AssignDefaultFontIfAvailable(TextMeshProUGUI text)
        {
            if (text == null || text.font != null)
            {
                return;
            }

            TMP_FontAsset fontAsset = TMP_Settings.GetFontAsset();
            if (fontAsset != null)
            {
                text.font = fontAsset;
            }
        }

        private void ResolveReferencesIfNeeded()
        {
            if (stepText == null)
            {
                stepText = FindText("Step Text");
            }

            if (checklistText == null)
            {
                checklistText = FindText("Checklist Text");
            }

            if (statusText == null)
            {
                statusText = FindText("Status Text");
            }

            if (resultText == null)
            {
                resultText = FindText("Result Text");
            }

            if (introText == null)
            {
                introText = FindText("Intro Text");
            }

            if (resultPanel == null)
            {
                Transform panel = FindChildByName(transform, "Result Panel");
                if (panel != null)
                {
                    resultPanel = panel.gameObject;
                }
            }

            if (introPanel == null)
            {
                Transform panel = FindChildByName(transform, "Intro Panel");
                if (panel != null)
                {
                    introPanel = panel.gameObject;
                }
            }
        }

        private TextMeshProUGUI FindText(string childName)
        {
            Transform child = FindChildByName(transform, childName);
            return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (Transform child in root)
            {
                if (child.name == childName)
                {
                    return child;
                }

                Transform match = FindChildByName(child, childName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }
    }
}
