#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FireExtinguisherTrainer;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace FireExtinguisherTrainerTests
{
    public class FireTrainingSmokeTests
    {
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject createdObject in createdObjects)
            {
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void DebugFlowCompletesSuccessfulPassRun()
        {
            FireTarget fire = CreateComponent<FireTarget>("Training Fire");
            ExtinguisherController extinguisher = CreateComponent<ExtinguisherController>("Training Extinguisher");
            FireTrainingManager manager = CreateManager(fire, extinguisher);

            PullAndAimAtBase(manager, fire);

            for (int i = 0; i < 80 && manager.CurrentReport.Outcome == TrainingOutcome.Running; i++)
            {
                float xOffset = Mathf.Sin(i * 0.65f) * 0.3f;
                manager.DebugRunFrame(BaseRay(fire, new Vector3(xOffset, 0f, -2f)), 0.15f, sprayHeld: true);
            }

            TrainingSessionReport report = manager.CurrentReport;
            Assert.AreEqual(TrainingOutcome.Success, report.Outcome);
            Assert.LessOrEqual(report.FireHealth01, 0.01f);
            Assert.Greater(report.AimingAccuracy01, 0.75f);
            Assert.GreaterOrEqual(report.SweepDegrees, 18f);
            Assert.IsTrue(report.ResultReason.Contains("PASS") || report.ResultReason.Contains("extinguished"));
        }

        [Test]
        public void DebugFlowRequiresReplacementWhenBottleRunsEmpty()
        {
            FireTarget fire = CreateComponent<FireTarget>("Training Fire");
            ExtinguisherController extinguisher = CreateComponent<ExtinguisherController>("Training Extinguisher");
            SetPrivateFloat(extinguisher, "capacitySeconds", 0.2f);
            extinguisher.ResetExtinguisher();

            FireTrainingManager manager = CreateManager(fire, extinguisher);
            PullAndAimAtBase(manager, fire);

            for (int i = 0; i < 10 && !manager.CurrentReport.WaitingForReplacement; i++)
            {
                manager.DebugRunFrame(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.12f, sprayHeld: true);
            }

            TrainingSessionReport emptyReport = manager.CurrentReport;
            Assert.IsTrue(emptyReport.WaitingForReplacement);
            Assert.AreEqual(TrainingOutcome.Running, emptyReport.Outcome);
            Assert.That(emptyReport.MistakeBreakdown, Does.Contain("Empty extinguisher"));

            manager.DebugRunFrame(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.1f, replacePressed: true);

            TrainingSessionReport replacementReport = manager.CurrentReport;
            Assert.IsFalse(replacementReport.WaitingForReplacement);
            Assert.IsTrue(replacementReport.UsedReplacement);
            Assert.AreEqual(2, replacementReport.ExtinguishersUsed);
            Assert.AreEqual(PassStep.PullPin, replacementReport.CurrentStep);
        }

        [Test]
        public void DebugFlowRecordsExpectedMistakeTypes()
        {
            FireTarget fire = CreateComponent<FireTarget>("Training Fire");
            ExtinguisherController extinguisher = CreateComponent<ExtinguisherController>("Training Extinguisher");
            FireTrainingManager manager = CreateManager(fire, extinguisher);

            Ray baseRay = BaseRay(fire, new Vector3(0f, 0f, -2f));
            manager.DebugRunFrame(baseRay, 0.1f, sprayHeld: true);
            manager.DebugRunFrame(baseRay, 0.1f);
            manager.DebugRunFrame(baseRay, 0.1f, pullPressed: true);

            for (int i = 0; i < 5; i++)
            {
                manager.DebugRunFrame(baseRay, 0.1f);
            }

            manager.DebugRunFrame(baseRay, 0.1f);
            manager.DebugRunFrame(BodyRay(fire), 0.1f, sprayHeld: true);
            manager.DebugRunFrame(BaseRay(fire, new Vector3(0f, 0f, -0.35f)), 0.3f, sprayHeld: true);
            manager.DebugRunFrame(BaseRay(fire, new Vector3(0f, 0f, -5f)), 0.1f, sprayHeld: true);

            TrainingSessionReport report = manager.CurrentReport;
            Assert.GreaterOrEqual(report.Mistakes, 4);
            Assert.That(report.MistakeBreakdown, Does.Contain("Early spray"));
            Assert.That(report.MistakeBreakdown, Does.Contain("Wrong aim"));
            Assert.That(report.MistakeBreakdown, Does.Contain("Too close"));
            Assert.That(report.MistakeBreakdown, Does.Contain("Too far"));
        }

        [Test]
        public void TrainingHudShowsPassChecklistAndStepInstruction()
        {
            TrainingHUD hud = CreateComponent<TrainingHUD>("Training HUD");
            TextMeshProUGUI stepText = CreateHudText(hud.transform, "Step Text");
            TextMeshProUGUI checklistText = CreateHudText(hud.transform, "Checklist Text");
            TextMeshProUGUI statusText = CreateHudText(hud.transform, "Status Text");
            SetPrivateObject(hud, "stepText", stepText);
            SetPrivateObject(hud, "checklistText", checklistText);
            SetPrivateObject(hud, "statusText", statusText);

            hud.SetRunning(new TrainingSessionReport
            {
                Outcome = TrainingOutcome.Running,
                CurrentStep = PassStep.AimAtBase,
                Status = "Aim at the base of the fire.",
                InstructionText = "Point the nozzle at the base of the fire.",
                CurrentAimQuality = SprayHitQuality.WrongArea,
                HasHeldExtinguisher = true,
                ExtinguisherCapacity01 = 0.8f,
                FireHealth01 = 1f,
                SpareExtinguishers = 1,
            });

            Assert.That(stepText.text, Does.Contain("Aim at base"));
            Assert.That(checklistText.text, Does.Contain("PASS CHECKLIST"));
            Assert.That(checklistText.text, Does.Contain("[x] Pick up"));
            Assert.That(checklistText.text, Does.Contain("[>] Aim at base: Aim lower"));
            Assert.That(statusText.text, Does.Contain("Point the nozzle at the base"));
        }

        [Test]
        public void IntroPanelRequiresMinimumReadTimeThenDismissesOnce()
        {
            FireTarget fire = CreateComponent<FireTarget>("Intro Fire");
            ExtinguisherController extinguisher = CreateComponent<ExtinguisherController>("Intro Extinguisher");
            TrainingHUD hud = CreateComponent<TrainingHUD>("Intro HUD");
            GameObject introPanel = CreateHudPanel(hud.transform, "Intro Panel");
            TextMeshProUGUI introText = CreateHudText(introPanel.transform, "Intro Text");
            SetPrivateObject(hud, "introPanel", introPanel);
            SetPrivateObject(hud, "introText", introText);

            FireTrainingManager manager = CreateManager(fire, extinguisher);
            SetPrivateObject(manager, "hud", hud);
            manager.DebugShowIntro();
            Assert.IsTrue(manager.IntroVisible);
            Assert.IsTrue(hud.IntroVisible);
            Assert.That(introText.text, Does.Contain("Pull the yellow safety ring"));

            Ray baseRay = BaseRay(fire, new Vector3(0f, 0f, -2f));
            manager.DebugRunFrame(baseRay, 1f, restartPressed: true);
            Assert.IsTrue(manager.IntroVisible);

            manager.DebugRunFrame(baseRay, 1.6f, restartPressed: true);
            Assert.IsFalse(manager.IntroVisible);
            Assert.IsFalse(hud.IntroVisible);

            FireTarget secondFire = CreateComponent<FireTarget>("Second Intro Fire");
            manager.DebugBeginTraining(secondFire, extinguisher, hud);
            Assert.IsFalse(manager.IntroVisible);

            manager.DebugShowIntro();
            Assert.IsTrue(manager.IntroVisible);
            manager.DebugRunFrame(BaseRay(secondFire, new Vector3(0f, 0f, -2f)), 18.1f);
            Assert.IsFalse(manager.IntroVisible);
        }

        [Test]
        public void TrainingHudCreatesIntroPanelWhenSceneHasOldHud()
        {
            TrainingHUD hud = CreateComponent<TrainingHUD>("Legacy Intro HUD");

            hud.ShowIntro();

            Transform introPanel = hud.transform.Find("Intro Panel");
            Assert.IsNotNull(introPanel);
            Assert.IsTrue(hud.IntroVisible);
            TextMeshProUGUI introText = introPanel.GetComponentInChildren<TextMeshProUGUI>();
            Assert.IsNotNull(introText);
            Assert.That(introText.text, Does.Contain("Aim at the base"));

            hud.HideIntro();
            Assert.IsFalse(hud.IntroVisible);
        }

        [Test]
        public void StationFlowRequiresPickupBeforeSpraying()
        {
            FireTarget fire = CreateComponent<FireTarget>("Training Fire");
            ExtinguisherController prefab = CreateComponent<ExtinguisherController>("Extinguisher Prefab");
            Transform handAnchor = CreateTransform("Right Hand Anchor", new Vector3(0f, 0f, -2f));
            ExtinguisherStation station = CreateStation(prefab, handAnchor);
            FireTrainingManager manager = CreateComponent<FireTrainingManager>("Training Manager");
            manager.DebugBeginTrainingWithStation(fire, station);

            Ray baseRay = BaseRay(fire, new Vector3(0f, 0f, -2f));
            manager.DebugRunFrame(baseRay, 0.1f, pullPressed: true, sprayHeld: true);

            TrainingSessionReport noBottleReport = manager.CurrentReport;
            Assert.IsTrue(noBottleReport.NeedsExtinguisherPickup);
            Assert.IsFalse(noBottleReport.HasHeldExtinguisher);
            Assert.AreEqual(PassStep.PullPin, noBottleReport.CurrentStep);

            ExtinguisherHoldTracker tracker = station.AvailableExtinguisher.GetComponent<ExtinguisherHoldTracker>();
            tracker.DebugPickUp();
            TrackStationAvailable(station);
            manager.DebugRunFrame(baseRay, 0.1f, pullPressed: true);

            TrainingSessionReport pickedUpReport = manager.CurrentReport;
            Assert.IsTrue(pickedUpReport.HasHeldExtinguisher);
            Assert.IsFalse(pickedUpReport.NeedsExtinguisherPickup);
            Assert.AreEqual(PassStep.AimAtBase, pickedUpReport.CurrentStep);
        }

        [Test]
        public void StationFlowRefreshesAfterDelayAndKeepsIndependentBottleCapacity()
        {
            FireTarget fire = CreateComponent<FireTarget>("Training Fire");
            ExtinguisherController prefab = CreateComponent<ExtinguisherController>("Extinguisher Prefab");
            SetPrivateFloat(prefab, "capacitySeconds", 0.2f);
            prefab.ResetExtinguisher();

            Transform handAnchor = CreateTransform("Right Hand Anchor", new Vector3(0f, 0f, -2f));
            ExtinguisherStation station = CreateStation(prefab, handAnchor);
            FireTrainingManager manager = CreateComponent<FireTrainingManager>("Training Manager");
            manager.DebugBeginTrainingWithStation(fire, station);

            ExtinguisherController firstBottle = station.AvailableExtinguisher;
            firstBottle.GetComponent<ExtinguisherHoldTracker>().DebugPickUp();
            Assert.IsNull(station.AvailableExtinguisher);
            Assert.IsTrue(station.ReplacementQueued);

            station.DebugAdvanceReplacementTimer(4.9f);
            Assert.IsNull(station.AvailableExtinguisher);
            Assert.IsTrue(station.ReplacementQueued);

            station.DebugAdvanceReplacementTimer(0.1f);
            ExtinguisherController replacementBottle = station.AvailableExtinguisher;
            TrackStationAvailable(station);
            Assert.IsNotNull(replacementBottle);
            Assert.IsFalse(station.ReplacementQueued);
            Assert.AreNotSame(firstBottle, replacementBottle);
            Assert.IsTrue(firstBottle.IsHeld);
            Assert.IsFalse(replacementBottle.IsHeld);

            PullAndAimAtBase(manager, fire);

            for (int i = 0; i < 10 && !manager.CurrentReport.WaitingForReplacement; i++)
            {
                manager.DebugRunFrame(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.12f, sprayHeld: true);
            }

            Assert.IsTrue(manager.CurrentReport.WaitingForReplacement);
            Assert.IsTrue(firstBottle.IsHeld);
            Assert.Less(firstBottle.RemainingCapacity, replacementBottle.RemainingCapacity);

            firstBottle.GetComponent<ExtinguisherHoldTracker>().DebugRelease();

            TrainingSessionReport droppedReport = manager.CurrentReport;
            Assert.IsTrue(droppedReport.NeedsExtinguisherPickup);
            Assert.IsFalse(droppedReport.HasHeldExtinguisher);
            Assert.IsNotNull(firstBottle);
            Assert.IsNotNull(station.AvailableExtinguisher);
            Assert.AreSame(replacementBottle, station.AvailableExtinguisher);

            station.AvailableExtinguisher.GetComponent<ExtinguisherHoldTracker>().DebugPickUp();
            TrackStationAvailable(station);

            TrainingSessionReport replacementReport = manager.CurrentReport;
            Assert.IsTrue(replacementReport.HasHeldExtinguisher);
            Assert.IsTrue(replacementReport.UsedReplacement);
            Assert.AreEqual(2, replacementReport.ExtinguishersUsed);
            Assert.AreEqual(PassStep.PullPin, replacementReport.CurrentStep);
        }

        [Test]
        public void StationUsesPreplacedExtinguisherBeforeSpawningReplacement()
        {
            ExtinguisherController prefab = CreateComponent<ExtinguisherController>("Extinguisher Prefab");
            Transform handAnchor = CreateTransform("Right Hand Anchor", new Vector3(0f, 1f, -1f));
            ExtinguisherStation station = CreateComponent<ExtinguisherStation>("Extinguisher Station");
            Transform spawn = CreateTransform("Station Spawn", new Vector3(-1f, 1.15f, -1f));
            ExtinguisherController preplaced = CreateComponent<ExtinguisherController>("Station Extinguisher");

            preplaced.transform.SetParent(station.transform, false);
            preplaced.transform.position = spawn.position;
            station.Configure(prefab, spawn, handAnchor, null);
            station.SetAvailableExtinguisher(preplaced, true);

            Assert.AreSame(preplaced, station.EnsureAvailableExtinguisher());
            Assert.IsNotNull(preplaced.GetComponent<ExtinguisherHoldTracker>());
            Assert.IsFalse(preplaced.IsHeld);
            Assert.IsFalse(preplaced.IsEmpty);

            preplaced.GetComponent<ExtinguisherHoldTracker>().DebugPickUp();
            TrackStationAvailable(station);

            Assert.IsTrue(preplaced.IsHeld);
            Assert.IsNull(preplaced.transform.parent);
            Assert.IsNull(station.AvailableExtinguisher);
            Assert.IsTrue(station.ReplacementQueued);

            station.DebugAdvanceReplacementTimer(5f);
            TrackStationAvailable(station);

            Assert.IsNotNull(station.AvailableExtinguisher);
            Assert.IsFalse(station.ReplacementQueued);
            Assert.AreNotSame(preplaced, station.AvailableExtinguisher);
            Assert.IsFalse(station.AvailableExtinguisher.IsHeld);
            Assert.IsFalse(station.AvailableExtinguisher.IsEmpty);
        }

        [Test]
        public void PhysicalGrabKeepsRigidbodyDynamicAndAllowsThrowAndRegrab()
        {
            Transform handAnchor = CreateTransform("Right Hand Anchor", Vector3.zero);
            ExtinguisherPhysicalGrabber grabber = CreatePhysicalGrabber(handAnchor);
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Physical Extinguisher",
                new Vector3(0.05f, 0f, 0.12f));
            var tracker = extinguisher.gameObject.AddComponent<ExtinguisherHoldTracker>();
            tracker.Configure(null, null, handAnchor);
            tracker.SetGripFallbackEnabled(false);

            Rigidbody rigidbody = extinguisher.GetComponent<Rigidbody>();
            Collider collider = extinguisher.GetComponent<Collider>();

            Assert.IsTrue(grabber.DebugTryGrab(extinguisher));
            Assert.IsTrue(extinguisher.IsHeld);
            Assert.IsFalse(rigidbody.isKinematic);
            Assert.IsTrue(rigidbody.useGravity);
            Assert.IsFalse(collider.isTrigger);
            Assert.IsNotNull(extinguisher.GetComponent<FixedJoint>());

            Vector3 throwVelocity = new Vector3(1.2f, 0.1f, -0.3f);
            grabber.DebugRelease(throwVelocity);

            Assert.IsFalse(extinguisher.IsHeld);
            Assert.IsNull(extinguisher.GetComponent<FixedJoint>());
            Assert.That(rigidbody.linearVelocity.x, Is.EqualTo(throwVelocity.x).Within(0.001f));
            Assert.That(rigidbody.linearVelocity.y, Is.EqualTo(throwVelocity.y).Within(0.001f));
            Assert.That(rigidbody.linearVelocity.z, Is.EqualTo(throwVelocity.z).Within(0.001f));

            Assert.IsTrue(grabber.DebugTryGrab(extinguisher));
            Assert.IsTrue(extinguisher.IsHeld);
        }

        [Test]
        public void PhysicalGrabCanUseHandOriginWhenHoldOffsetMisses()
        {
            Transform handAnchor = CreateTransform("Right Hand Anchor", Vector3.zero);
            ExtinguisherPhysicalGrabber grabber = CreatePhysicalGrabber(handAnchor);
            SetPrivateFloat(grabber, "pickupRadius", 0.12f);
            SetPrivateVector3(grabber, "holdOffset", new Vector3(2f, 0f, 0f));

            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Origin Reachable Extinguisher",
                new Vector3(0.06f, 0f, 0f));
            Physics.SyncTransforms();

            Assert.IsTrue(grabber.DebugTryGrab());
            Assert.IsTrue(extinguisher.IsHeld);
            Assert.AreEqual("Grabbed Origin Reachable Extinguisher.", grabber.LastGrabStatus);
            Assert.GreaterOrEqual(grabber.LastOverlapCount, 1);
        }

        [Test]
        public void RightGripInputUsesQuestRightHandTriggerMapping()
        {
#if META_MR_SDK_INSTALLED
            Assert.AreEqual(OVRInput.RawAxis1D.RHandTrigger, RightControllerGripInput.QuestRightGripRawAxis);
            Assert.AreEqual(OVRInput.Axis1D.PrimaryHandTrigger, RightControllerGripInput.QuestRightGripVirtualAxis);
            Assert.AreEqual(OVRInput.Button.PrimaryHandTrigger, RightControllerGripInput.QuestRightGripVirtualButton);
            Assert.AreEqual(OVRInput.Controller.RTouch, RightControllerGripInput.QuestRightController);
            Assert.AreNotEqual(OVRInput.Axis1D.SecondaryHandTrigger, RightControllerGripInput.QuestRightGripVirtualAxis);
#else
            Assert.Pass("Meta input mapping constants compile only when META_MR_SDK_INSTALLED is defined.");
#endif
        }

        [Test]
        public void InteractionDriverRightGripOwnsPickupReleaseThrowAndRegrab()
        {
            Transform rightHand = CreateTransform("Right Hand Anchor", Vector3.zero);
            Transform leftHand = CreateTransform("Left Hand Anchor", new Vector3(-0.25f, 0f, 0f));
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand);
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Driver Extinguisher",
                new Vector3(0.05f, 0f, 0.12f));
            Rigidbody rigidbody = extinguisher.GetComponent<Rigidbody>();

            Assert.IsTrue(driver.DebugTryGrab(extinguisher));
            Assert.IsTrue(driver.IsHeld);
            Assert.AreSame(extinguisher, driver.HeldExtinguisher);
            Assert.IsTrue(extinguisher.IsHeld);
            Assert.IsFalse(rigidbody.isKinematic);

            Vector3 throwVelocity = new Vector3(1.2f, 0.1f, -0.3f);
            driver.DebugRelease(throwVelocity);

            Assert.IsFalse(driver.IsHeld);
            Assert.IsFalse(extinguisher.IsHeld);
            Assert.IsFalse(rigidbody.isKinematic);
            Assert.IsTrue(rigidbody.useGravity);
            Assert.That(rigidbody.linearVelocity.x, Is.EqualTo(throwVelocity.x).Within(0.001f));
            Assert.That(rigidbody.linearVelocity.y, Is.EqualTo(throwVelocity.y).Within(0.001f));
            Assert.That(rigidbody.linearVelocity.z, Is.EqualTo(throwVelocity.z).Within(0.001f));

            Assert.IsTrue(driver.DebugTryGrab(extinguisher));
            Assert.IsTrue(driver.IsHeld);
            Assert.IsFalse(driver.IsSupportedByLeftHand);
        }

        [Test]
        public void InteractionDriverIgnoresPlayerCollisionOnlyWhileHeld()
        {
            Transform rightHand = CreateTransform("Right Hand Anchor", Vector3.zero);
            Transform leftHand = CreateTransform("Left Hand Anchor", new Vector3(-0.25f, 0f, 0f));
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand);
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Collision Extinguisher",
                new Vector3(0.05f, 0f, 0.12f));

            var player = new GameObject("FireTrainerPlayer");
            createdObjects.Add(player);
            CharacterController characterController = player.AddComponent<CharacterController>();
            driver.SetPlayerCollisionRoot(player.transform);

            Collider extinguisherCollider = extinguisher.GetComponent<Collider>();
            Assert.IsFalse(Physics.GetIgnoreCollision(extinguisherCollider, characterController));

            Assert.IsTrue(driver.DebugTryGrab(extinguisher));
            Assert.IsTrue(Physics.GetIgnoreCollision(extinguisherCollider, characterController));

            driver.DebugRelease(Vector3.zero);
            Assert.IsFalse(Physics.GetIgnoreCollision(extinguisherCollider, characterController));
        }

        [Test]
        public void InteractionDriverLeftSupportChangesNozzleDirectionWithoutTakingOwnership()
        {
            Transform rightHand = CreateTransform("Right Hand Anchor", Vector3.zero);
            Transform leftHand = CreateTransform("Left Hand Anchor", new Vector3(0f, 0.62f, 0.18f));
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand);
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Two Hand Extinguisher",
                new Vector3(0.05f, 0f, 0.12f));

            Assert.IsTrue(driver.DebugTryGrab(extinguisher));
            driver.DebugSnapHeldPose();
            Vector3 initialNozzleDirection = extinguisher.Nozzle.forward;

            leftHand.position = extinguisher.transform.Find(ExtinguisherInteractionDriver.LeftSupportPoseName).position;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            Assert.IsTrue(driver.IsSupportedByLeftHand);

            leftHand.position = rightHand.position + new Vector3(0.45f, 0.05f, 0.05f);
            driver.DebugSnapHeldPose();

            Assert.AreSame(extinguisher, driver.HeldExtinguisher);
            Assert.IsTrue(extinguisher.IsHeld);
            Assert.IsTrue(driver.IsSupportedByLeftHand);
            Assert.That(Vector3.Angle(initialNozzleDirection, extinguisher.Nozzle.forward), Is.GreaterThan(10f));
            Assert.That(extinguisher.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(extinguisher.transform.localScale.y, Is.EqualTo(1f).Within(0.001f));
            Assert.That(extinguisher.transform.localScale.z, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void InteractionDriverQueuesPinOnlyAfterLeftHandGrabsAndPullsSafetyPin()
        {
            Transform rightHand = CreateTransform("Right Hand Anchor", Vector3.zero);
            Transform leftHand = CreateTransform("Left Hand Anchor", Vector3.zero);
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand);
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Pin Extinguisher",
                new Vector3(0.05f, 0f, 0.12f));

            driver.DebugSetInputState(rightGripHeld: false, leftGripHeld: true);
            Assert.IsFalse(driver.PinPullRequestedThisFrame);

            Assert.IsTrue(driver.DebugTryGrab(extinguisher));
            Transform pinZone = extinguisher.transform.Find(ExtinguisherInteractionDriver.PinPullZoneName);
            leftHand.position = extinguisher.transform.position + Vector3.left;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            Assert.IsFalse(driver.PinPullRequestedThisFrame);

            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: false);
            leftHand.position = pinZone.position;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            Assert.IsFalse(driver.PinPullRequestedThisFrame);

            leftHand.position = pinZone.position + Vector3.left * 0.08f;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            Assert.IsFalse(driver.PinPullRequestedThisFrame);

            leftHand.position = pinZone.position + Vector3.left * 0.25f;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            Assert.IsTrue(driver.ConsumePinPullRequested());

            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: false, pullPressed: true);
            Assert.IsFalse(driver.ConsumePinPullRequested());
        }

        [Test]
        public void InteractionDriverCancelsPinDragWhenLeftGripReleasedBeforePullDistance()
        {
            Transform rightHand = CreateTransform("Right Hand Anchor", Vector3.zero);
            Transform leftHand = CreateTransform("Left Hand Anchor", Vector3.zero);
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand);
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Cancel Pin Extinguisher",
                new Vector3(0.05f, 0f, 0.12f));

            Assert.IsTrue(driver.DebugTryGrab(extinguisher));
            Transform pinZone = extinguisher.transform.Find(ExtinguisherInteractionDriver.PinPullZoneName);
            leftHand.position = pinZone.position;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            leftHand.position = pinZone.position + Vector3.left * 0.08f;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            Assert.IsFalse(driver.PinPullRequestedThisFrame);

            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: false);
            Assert.IsFalse(driver.ConsumePinPullRequested());
            Transform safetyPin = extinguisher.transform.Find(ExtinguisherController.SafetyPinName);
            Assert.IsNotNull(safetyPin);
            Assert.IsTrue(safetyPin.gameObject.activeSelf);
            AssertVectorEqual(new Vector3(0f, 1.04f, 0f), safetyPin.localPosition);
        }

        [Test]
        public void ExtinguisherSafetyPinVisualHidesWhenPulledAndRestoresOnReset()
        {
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Safety Pin Extinguisher",
                Vector3.zero);

            extinguisher.ResetExtinguisher();
            Transform safetyPin = extinguisher.transform.Find(ExtinguisherController.SafetyPinName);
            Assert.IsNotNull(safetyPin);
            Assert.IsTrue(safetyPin.gameObject.activeSelf);
            Assert.IsNotNull(safetyPin.Find(ExtinguisherController.SafetyPinRingName));
            Assert.IsNotNull(safetyPin.Find(ExtinguisherController.SafetyPinShaftName));
            Transform label = safetyPin.Find(ExtinguisherController.SafetyPinLabelName);
            Assert.IsNotNull(label);
            Assert.AreEqual("PULL PIN", label.GetComponent<TextMeshPro>().text);

            extinguisher.PullPin();
            Assert.IsFalse(safetyPin.gameObject.activeSelf);

            extinguisher.ResetExtinguisher();
            Assert.IsTrue(safetyPin.gameObject.activeSelf);
        }

        [Test]
        public void TrainingManagerConsumesInteractionDriverPinAndSprayState()
        {
            FireTarget fire = CreateComponent<FireTarget>("Training Fire");
            ExtinguisherController prefab = CreatePhysicalExtinguisher("Extinguisher Prefab", Vector3.zero);
            Transform rightHand = CreateTransform("Right Hand Anchor", new Vector3(0f, 0f, -2f));
            Transform leftHand = CreateTransform("Left Hand Anchor", new Vector3(0f, 0f, -2f));
            ExtinguisherStation station = CreateStation(prefab, rightHand);
            FireTrainingManager manager = CreateComponent<FireTrainingManager>("Training Manager");
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand, manager, station);
            manager.DebugSetInteractionDriver(driver);
            manager.DebugBeginTrainingWithStation(fire, station);

            ExtinguisherController firstBottle = station.AvailableExtinguisher;
            Assert.IsTrue(driver.DebugTryGrab(firstBottle));
            TrackStationAvailable(station);

            Ray baseRay = BaseRay(fire, new Vector3(0f, 0f, -2f));
            driver.DebugSetInputState(rightGripHeld: true, sprayHeld: true);
            manager.DebugRunFrameWithInteraction(baseRay, 0.1f);
            Assert.IsFalse(firstBottle.IsSpraying);
            Assert.That(manager.CurrentReport.MistakeBreakdown, Does.Contain("Early spray"));

            DragSafetyPinFarEnough(driver, leftHand, firstBottle);
            manager.DebugRunFrameWithInteraction(baseRay, 0.1f);
            Assert.IsTrue(firstBottle.IsPinPulled);
            Assert.AreEqual(PassStep.AimAtBase, manager.CurrentReport.CurrentStep);

            driver.DebugSetInputState(rightGripHeld: true, sprayHeld: true);
            manager.DebugRunFrameWithInteraction(baseRay, 0.1f);
            Assert.IsTrue(firstBottle.IsSpraying);
            Assert.Greater(manager.CurrentReport.TotalSprayTimeSeconds, 0f);

            driver.DebugSetInputState(rightGripHeld: true);
            for (int i = 0; i < 5; i++)
            {
                manager.DebugRunFrameWithInteraction(baseRay, 0.1f);
            }

            Assert.AreEqual(PassStep.SqueezeHandle, manager.CurrentReport.CurrentStep);

            driver.DebugSetInputState(rightGripHeld: true);
            manager.DebugRunFrameWithInteraction(baseRay, 0.1f);

            driver.DebugSetInputState(rightGripHeld: true, sprayHeld: true);
            manager.DebugRunFrameWithInteraction(baseRay, 0.3f);

            Assert.IsTrue(firstBottle.IsSpraying);
            Assert.Greater(manager.CurrentReport.TotalSprayTimeSeconds, 0f);
        }

        [Test]
        public void RestartKeepsHeldPulledExtinguisherReadyForNewFire()
        {
            FireTarget initialFire = CreateComponent<FireTarget>("Initial Fire");
            FireTarget firePrefab = CreateComponent<FireTarget>("Fire Prefab");
            FireSpawner spawner = CreateComponent<FireSpawner>("Fire Spawner");
            SetPrivateObject(spawner, "firePrefab", firePrefab);

            ExtinguisherController prefab = CreatePhysicalExtinguisher("Extinguisher Prefab", Vector3.zero);
            Transform rightHand = CreateTransform("Right Hand Anchor", new Vector3(0f, 0f, -2f));
            Transform leftHand = CreateTransform("Left Hand Anchor", new Vector3(0f, 0f, -2f));
            ExtinguisherStation station = CreateStation(prefab, rightHand);
            FireTrainingManager manager = CreateComponent<FireTrainingManager>("Training Manager");
            SetPrivateObject(manager, "fireSpawner", spawner);

            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand, manager, station);
            manager.DebugSetInteractionDriver(driver);
            manager.DebugBeginTrainingWithStation(initialFire, station);

            ExtinguisherController firstBottle = station.AvailableExtinguisher;
            Assert.IsTrue(driver.DebugTryGrab(firstBottle));
            TrackStationAvailable(station);

            Ray baseRay = BaseRay(initialFire, new Vector3(0f, 0f, -2f));
            DragSafetyPinFarEnough(driver, leftHand, firstBottle);
            manager.DebugRunFrameWithInteraction(baseRay, 0.1f);
            Assert.IsTrue(firstBottle.IsPinPulled);

            driver.DebugSetInputState(rightGripHeld: true, sprayHeld: true);
            for (int i = 0; i < 80 && manager.CurrentReport.Outcome == TrainingOutcome.Running; i++)
            {
                float xOffset = Mathf.Sin(i * 0.65f) * 0.3f;
                manager.DebugRunFrameWithInteraction(BaseRay(initialFire, new Vector3(xOffset, 0f, -2f)), 0.15f);
            }

            Assert.AreEqual(TrainingOutcome.Success, manager.CurrentReport.Outcome);

            manager.DebugRunFrame(baseRay, 0.1f, restartPressed: true);
            FireTarget newFire = spawner.CurrentFire;
            Assert.IsNotNull(newFire);
            createdObjects.Add(newFire.gameObject);
            Assert.IsTrue(manager.CurrentReport.HasHeldExtinguisher);
            Assert.AreEqual(PassStep.AimAtBase, manager.CurrentReport.CurrentStep);

            driver.DebugSetInputState(rightGripHeld: true, sprayHeld: true);
            manager.DebugRunFrameWithInteraction(BaseRay(newFire, new Vector3(0f, 0f, -2f)), 0.1f);
            Assert.IsTrue(firstBottle.IsSpraying);
        }

        [Test]
        public void CompletedTrainingStillConsumesPinPullForNewHeldBottle()
        {
            FireTarget fire = CreateComponent<FireTarget>("Completed Fire");
            ExtinguisherController prefab = CreatePhysicalExtinguisher("Completed Extinguisher Prefab", Vector3.zero);
            Transform rightHand = CreateTransform("Completed Right Hand", new Vector3(0f, 0f, -2f));
            Transform leftHand = CreateTransform("Completed Left Hand", new Vector3(0f, 0f, -2f));
            ExtinguisherStation station = CreateStation(prefab, rightHand);
            FireTrainingManager manager = CreateComponent<FireTrainingManager>("Completed Training Manager");
            ExtinguisherInteractionDriver driver = CreateInteractionDriver(rightHand, leftHand, manager, station);
            manager.DebugSetInteractionDriver(driver);
            manager.DebugBeginTrainingWithStation(fire, station);

            ExtinguisherController firstBottle = station.AvailableExtinguisher;
            Assert.IsTrue(driver.DebugTryGrab(firstBottle));
            TrackStationAvailable(station);
            DragSafetyPinFarEnough(driver, leftHand, firstBottle);
            manager.DebugRunFrameWithInteraction(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.1f);

            driver.DebugSetInputState(rightGripHeld: true, sprayHeld: true);
            for (int i = 0; i < 80 && manager.CurrentReport.Outcome == TrainingOutcome.Running; i++)
            {
                float xOffset = Mathf.Sin(i * 0.65f) * 0.3f;
                manager.DebugRunFrameWithInteraction(BaseRay(fire, new Vector3(xOffset, 0f, -2f)), 0.15f);
            }

            Assert.AreEqual(TrainingOutcome.Success, manager.CurrentReport.Outcome);

            driver.DebugSetInputState(rightGripHeld: false);
            station.DebugAdvanceReplacementTimer(5f);
            ExtinguisherController replacementBottle = station.AvailableExtinguisher;
            TrackStationAvailable(station);
            Assert.IsNotNull(replacementBottle);
            Assert.IsTrue(driver.DebugTryGrab(replacementBottle));
            Assert.IsFalse(replacementBottle.IsPinPulled);

            DragSafetyPinFarEnough(driver, leftHand, replacementBottle);
            manager.DebugRunFrameWithInteraction(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.1f);

            Assert.AreEqual(TrainingOutcome.Success, manager.CurrentReport.Outcome);
            Assert.IsTrue(replacementBottle.IsPinPulled);
            Assert.IsFalse(driver.ConsumePinPullRequested());
        }

        [Test]
        public void FireTargetKeepsBaseAndRootScaleWhileFlameShrinks()
        {
            var root = new GameObject("Visual Fire");
            createdObjects.Add(root);

            GameObject flameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameObject.name = "Flame Placeholder";
            flameObject.transform.SetParent(root.transform, false);
            flameObject.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            Transform flame = flameObject.transform;
            flame.localScale = new Vector3(0.55f, 0.85f, 0.55f);
            GameObject flameTongue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameTongue.name = "Flame Core Tongue";
            flameTongue.transform.SetParent(flame, false);
            flameTongue.transform.localScale = new Vector3(0.36f, 0.82f, 0.36f);
            Transform ember = CreateChild(root.transform, "Base Ember", new Vector3(0f, 0.12f, 0f));
            ember.localScale = new Vector3(0.7f, 0.18f, 0.7f);
            Transform baseTarget = CreateChild(root.transform, "Base Target Zone", new Vector3(0f, 0.1f, 0f));
            baseTarget.localScale = new Vector3(0.75f, 0.03f, 0.75f);

            FireTarget fire = root.AddComponent<FireTarget>();
            SetPrivateObject(fire, "baseTarget", baseTarget);
            fire.ResetFire();

            Vector3 rootScale = root.transform.localScale;
            Vector3 emberScale = ember.localScale;
            Vector3 baseTargetScale = baseTarget.localScale;
            Vector3 flameScale = flame.localScale;

            Assert.IsTrue(fire.ApplySpray(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.5f, out _));

            AssertVectorEqual(rootScale, root.transform.localScale);
            AssertVectorEqual(emberScale, ember.localScale);
            AssertVectorEqual(baseTargetScale, baseTarget.localScale);
            Assert.That(flame.localScale.y, Is.LessThan(flameScale.y));
            Assert.IsFalse(flameObject.GetComponent<Renderer>().enabled);
            Assert.IsTrue(flameTongue.GetComponent<Renderer>().enabled);
        }

        [Test]
        public void FireTargetBaseAimFeedbackChangesColorWithoutScalingBase()
        {
            var root = new GameObject("Feedback Fire");
            createdObjects.Add(root);

            GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Base Target Zone";
            baseObject.transform.SetParent(root.transform, false);
            baseObject.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            baseObject.transform.localScale = new Vector3(0.75f, 0.03f, 0.75f);
            Transform baseTarget = baseObject.transform;

            FireTarget fire = root.AddComponent<FireTarget>();
            SetPrivateObject(fire, "baseTarget", baseTarget);
            fire.ResetFire();

            Vector3 baseScale = baseTarget.localScale;
            fire.SetAimFeedback(SprayHitQuality.WrongArea, true);
            Color wrongAreaColor = fire.BaseFeedbackRenderer.sharedMaterial.color;

            fire.SetAimFeedback(SprayHitQuality.BaseHit, true);
            Color baseHitColor = fire.BaseFeedbackRenderer.sharedMaterial.color;

            fire.SetAimFeedback(SprayHitQuality.Miss, false);
            Color inactiveColor = fire.BaseFeedbackRenderer.sharedMaterial.color;

            Assert.AreEqual(SprayHitQuality.Miss, fire.CurrentAimFeedback);
            Assert.Greater(wrongAreaColor.r, wrongAreaColor.g);
            Assert.Greater(baseHitColor.g, baseHitColor.r);
            Assert.Greater(inactiveColor.b, inactiveColor.r);
            AssertVectorEqual(baseScale, baseTarget.localScale);

            Assert.IsTrue(fire.ApplySpray(BaseRay(fire, new Vector3(0f, 0f, -2f)), 4f, out _));
            Assert.IsTrue(fire.IsExtinguished);
            Assert.IsFalse(fire.BaseFeedbackVisible);

            fire.SetAimFeedback(SprayHitQuality.BaseHit, true);
            Assert.IsFalse(fire.BaseFeedbackVisible);

            fire.ResetFire();
            Assert.IsTrue(fire.BaseFeedbackVisible);
            AssertVectorEqual(baseScale, baseTarget.localScale);
        }

        [Test]
        public void StationRailsAreShorterInSceneAndSetup()
        {
            string scenePath = Path.Combine(Application.dataPath, "Scenes/FireTrainerWeek1.unity");
            string sceneText = File.ReadAllText(scenePath);
            Assert.That(
                CountOccurrences(sceneText, "m_LocalScale: {x: 0.06, y: 0.26, z: 0.34}"),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(sceneText, Does.Contain("m_LocalPosition: {x: 0, y: 0.84, z: -0.100000"));
            Assert.That(sceneText, Does.Contain("m_LocalPosition: {x: 0, y: 0.9, z: -0.08}"));

            string setupPath = Path.Combine(
                Application.dataPath,
                "FireExtinguisherTrainer/Editor/PlatformSceneSetup.cs");
            string setupText = File.ReadAllText(setupPath);
            Assert.That(setupText, Does.Contain("new Vector3(0.06f, 0.26f, 0.34f)"));
            Assert.That(setupText, Does.Contain("new Vector3(0f, 0.84f, -0.1f)"));
            Assert.That(setupText, Does.Contain("new Vector3(-0.37f, 1.09f, -0.1f)"));
            Assert.That(setupText, Does.Contain("new Vector3(0f, 0.93f, -0.46f)"));
            Assert.That(setupText, Does.Contain("new Vector3(0f, 0.9f, -0.08f)"));
            Assert.That(setupText, Does.Contain("\"playerCollisionRoot\""));
            Assert.That(setupText, Does.Contain("ExtinguisherCapacityGauge"));
            Assert.That(setupText, Does.Contain("SafetyPinRingName"));
            Assert.That(setupText, Does.Contain("SafetyPinLabelName"));

            string weekSetupPath = Path.Combine(
                Application.dataPath,
                "FireExtinguisherTrainer/Editor/FireTrainerWeek1Setup.cs");
            string weekSetupText = File.ReadAllText(weekSetupPath);
            Assert.That(weekSetupText, Does.Contain("SetBool(fireTarget, \"useParticleEffects\", true)"));
            Assert.That(weekSetupText, Does.Contain("SetBool(fireTarget, \"useSmokeParticles\", true)"));
            Assert.That(weekSetupText, Does.Contain("ConfigureMeshParticleRenderer"));
            Assert.That(weekSetupText, Does.Contain("flameBodyRenderer.enabled = false"));
            Assert.That(weekSetupText, Does.Contain("SetBool(extinguisher, \"showSprayGuideLine\", false)"));
            Assert.That(weekSetupText, Does.Contain("SetBool(fireTarget, \"useBaseAimFeedback\", true)"));
            Assert.That(weekSetupText, Does.Contain("\"Checklist Text\""));
            Assert.That(weekSetupText, Does.Contain("\"Intro Panel\""));
            Assert.That(weekSetupText, Does.Contain("introMinimumSeconds\", 2.5f"));
            Assert.That(weekSetupText, Does.Contain("introAutoDismissSeconds\", 18f"));
            Assert.That(weekSetupText, Does.Contain("SafetyPinRingName"));
        }

        [Test]
        public void ExtinguisherCapacityGaugeTracksEachBottleCapacity()
        {
            ExtinguisherController firstBottle = CreatePhysicalExtinguisher(
                "Gauge First Bottle",
                Vector3.zero);
            ExtinguisherController replacementBottle = CreatePhysicalExtinguisher(
                "Gauge Replacement Bottle",
                new Vector3(1f, 0f, 0f));
            firstBottle.ResetExtinguisher();
            replacementBottle.ResetExtinguisher();
            ExtinguisherCapacityGauge firstGauge = EnsureCapacityGauge(firstBottle);
            ExtinguisherCapacityGauge replacementGauge = EnsureCapacityGauge(replacementBottle);
            SetPrivateVector3(firstGauge, "localPosition", new Vector3(-0.094f, 0.58f, 0f));
            firstGauge.ForceRefresh();

            AssertVectorEqual(new Vector3(-0.135f, 0.58f, 0f), firstGauge.GaugeRoot.localPosition);
            Assert.That(Mathf.DeltaAngle(0f, firstGauge.GaugeRoot.localEulerAngles.y), Is.EqualTo(-90f).Within(1f));
            Assert.IsNotNull(firstGauge.MountRenderer);
            Assert.That(firstGauge.MountRenderer.GetComponent<MeshFilter>().sharedMesh.name, Does.Contain("Cylinder"));
            Assert.That(firstGauge.MountRenderer.transform.localPosition.z, Is.LessThan(firstGauge.DialRenderer.transform.localPosition.z));
            Assert.That(firstGauge.MountRenderer.transform.localScale.x, Is.EqualTo(firstGauge.MountRenderer.transform.localScale.z).Within(0.001f));
            Assert.That(firstGauge.DialRenderer.GetComponent<MeshFilter>().sharedMesh.name, Does.Contain("Cylinder"));
            Assert.That(firstGauge.DialRenderer.transform.localScale.x, Is.EqualTo(firstGauge.DialRenderer.transform.localScale.z).Within(0.001f));
            Assert.That(firstGauge.DialRenderer.transform.localScale.y, Is.LessThan(firstGauge.DialRenderer.transform.localScale.x));
            Assert.That(firstGauge.NeedlePivot.localPosition.z, Is.GreaterThan(firstGauge.DialRenderer.transform.localPosition.z + 0.004f));
            Assert.That(firstGauge.NeedleRenderer.transform.localScale.z, Is.LessThan(0.003f));
            Assert.That(SignedLocalZAngle(firstGauge.NeedlePivot), Is.EqualTo(115f).Within(1f));
            Assert.That(SignedLocalZAngle(replacementGauge.NeedlePivot), Is.EqualTo(115f).Within(1f));

            Object.DestroyImmediate(firstGauge.DialRenderer.gameObject);
            GameObject oldSquareDial = GameObject.CreatePrimitive(PrimitiveType.Quad);
            oldSquareDial.name = ExtinguisherCapacityGauge.DialName;
            oldSquareDial.transform.SetParent(firstGauge.GaugeRoot, false);
            firstGauge.ForceRefresh();
            Assert.That(firstGauge.DialRenderer.GetComponent<MeshFilter>().sharedMesh.name, Does.Contain("Cylinder"));

            firstBottle.MarkPickedUp(firstBottle.transform, false);
            firstBottle.PullPin();
            Assert.IsTrue(firstBottle.ConsumeSpray(8f));
            firstGauge.ForceRefresh();
            replacementGauge.ForceRefresh();

            Assert.That(firstBottle.Capacity01, Is.EqualTo(0f).Within(0.001f));
            Assert.That(replacementBottle.Capacity01, Is.EqualTo(1f).Within(0.001f));
            Assert.That(SignedLocalZAngle(firstGauge.NeedlePivot), Is.EqualTo(-115f).Within(1f));
            Assert.That(SignedLocalZAngle(replacementGauge.NeedlePivot), Is.EqualTo(115f).Within(1f));
            Assert.Greater(firstGauge.NeedleRenderer.sharedMaterial.color.r, firstGauge.NeedleRenderer.sharedMaterial.color.g);
        }

        [Test]
        public void StableSprayLinePlaysWorldSpaceMistParticles()
        {
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Mist Extinguisher",
                Vector3.zero);
            ParticleSystem particles = CreateTestSprayParticles(extinguisher.Nozzle);
            LineRenderer sprayLine = extinguisher.Nozzle.gameObject.AddComponent<LineRenderer>();
            SetPrivateObject(extinguisher, "sprayParticles", particles);
            SetPrivateObject(extinguisher, "sprayLine", sprayLine);

            Assert.IsNotNull(sprayLine);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, particles.main.simulationSpace);

            extinguisher.ResetExtinguisher();
            extinguisher.MarkPickedUp(extinguisher.transform, false);
            extinguisher.PullPin();
            Assert.IsTrue(extinguisher.ConsumeSpray(0.1f));
            AssertMeshParticleRenderer(particles);

            Assert.IsFalse(sprayLine.enabled);
            Assert.IsTrue(particles.isPlaying);

            extinguisher.StopSpray();

            Assert.IsFalse(sprayLine.enabled);
            Assert.IsFalse(particles.isPlaying);

            SetPrivateBool(extinguisher, "showSprayGuideLine", true);
            Assert.IsTrue(extinguisher.ConsumeSpray(0.1f));
            Assert.IsTrue(sprayLine.enabled);
            Assert.IsTrue(particles.isPlaying);
            extinguisher.StopSpray();
            Assert.IsFalse(sprayLine.enabled);
            Assert.IsFalse(particles.isPlaying);
        }

        [Test]
        public void FireTargetComfortParticlesDoNotScaleBase()
        {
            var root = new GameObject("Comfort Particle Fire");
            createdObjects.Add(root);

            GameObject flameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameObject.name = "Flame Placeholder";
            flameObject.transform.SetParent(root.transform, false);
            flameObject.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            GameObject flameTongue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flameTongue.name = "Flame Core Tongue";
            flameTongue.transform.SetParent(flameObject.transform, false);
            flameTongue.transform.localScale = new Vector3(0.36f, 0.82f, 0.36f);
            Transform baseTarget = CreateChild(root.transform, "Base Target Zone", new Vector3(0f, 0.1f, 0f));
            baseTarget.localScale = new Vector3(0.75f, 0.03f, 0.75f);
            ParticleSystem flameParticles = CreateTestFireParticles(root.transform, "Flame Particles");
            ParticleSystem smokeParticles = CreateTestFireParticles(root.transform, "Smoke Particles");

            FireTarget fire = root.AddComponent<FireTarget>();
            SetPrivateObject(fire, "baseTarget", baseTarget);
            SetPrivateObject(fire, "flameParticles", flameParticles);
            SetPrivateObject(fire, "smokeParticles", smokeParticles);
            fire.ResetFire();

            Vector3 rootScale = root.transform.localScale;
            Vector3 baseTargetScale = baseTarget.localScale;

            Assert.AreEqual(ParticleSystemSimulationSpace.World, flameParticles.main.simulationSpace);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, smokeParticles.main.simulationSpace);
            AssertMeshParticleRenderer(flameParticles);
            AssertMeshParticleRenderer(smokeParticles);
            Assert.IsFalse(flameObject.GetComponent<Renderer>().enabled);
            Assert.IsTrue(flameTongue.GetComponent<Renderer>().enabled);
            Assert.IsTrue(flameParticles.isPlaying);
            Assert.IsTrue(smokeParticles.isPlaying);

            Assert.IsTrue(fire.ApplySpray(BaseRay(fire, new Vector3(0f, 0f, -2f)), 0.5f, out _));

            AssertVectorEqual(rootScale, root.transform.localScale);
            AssertVectorEqual(baseTargetScale, baseTarget.localScale);
            Assert.LessOrEqual(flameParticles.emission.rateOverTime.constant, 24f);
            Assert.LessOrEqual(smokeParticles.emission.rateOverTime.constant, 8f);
        }

        [Test]
        public void ExtinguisherPhysicsStabilizationLowersCenterOfMass()
        {
            ExtinguisherController extinguisher = CreatePhysicalExtinguisher(
                "Stable Extinguisher",
                Vector3.zero);
            Rigidbody rigidbody = extinguisher.GetComponent<Rigidbody>();
            rigidbody.centerOfMass = Vector3.zero;
            rigidbody.angularDamping = 0.05f;
            rigidbody.maxAngularVelocity = 7f;

            extinguisher.ConfigureRigidbodyPhysics();

            Assert.That(rigidbody.centerOfMass.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(rigidbody.centerOfMass.y, Is.EqualTo(0.22f).Within(0.001f));
            Assert.That(rigidbody.centerOfMass.z, Is.EqualTo(0f).Within(0.001f));
            Assert.GreaterOrEqual(rigidbody.angularDamping, 1.25f);
            Assert.LessOrEqual(rigidbody.maxAngularVelocity, 6f);
        }

        private FireTrainingManager CreateManager(FireTarget fire, ExtinguisherController extinguisher)
        {
            FireTrainingManager manager = CreateComponent<FireTrainingManager>("Training Manager");
            manager.DebugBeginTraining(fire, extinguisher);
            return manager;
        }

        private void PullAndAimAtBase(FireTrainingManager manager, FireTarget fire)
        {
            Ray baseRay = BaseRay(fire, new Vector3(0f, 0f, -2f));
            manager.DebugRunFrame(baseRay, 0.1f, pullPressed: true);
            for (int i = 0; i < 5; i++)
            {
                manager.DebugRunFrame(baseRay, 0.1f);
            }

            Assert.AreEqual(PassStep.SqueezeHandle, manager.CurrentReport.CurrentStep);
        }

        private static void DragSafetyPinFarEnough(
            ExtinguisherInteractionDriver driver,
            Transform leftHand,
            ExtinguisherController extinguisher)
        {
            Transform pinZone = extinguisher.transform.Find(ExtinguisherInteractionDriver.PinPullZoneName);
            leftHand.position = pinZone.position;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
            leftHand.position = pinZone.position + Vector3.left * 0.25f;
            driver.DebugSetInputState(rightGripHeld: true, leftGripHeld: true);
        }

        private T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name);
            createdObjects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private Transform CreateTransform(string name, Vector3 position)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = position;
            createdObjects.Add(gameObject);
            return gameObject.transform;
        }

        private ExtinguisherStation CreateStation(ExtinguisherController prefab, Transform handAnchor)
        {
            ExtinguisherStation station = CreateComponent<ExtinguisherStation>("Extinguisher Station");
            Transform spawn = CreateTransform("Station Spawn", new Vector3(-1f, 0f, -1f));
            station.Configure(prefab, spawn, handAnchor, null);
            station.EnsureAvailableExtinguisher();
            createdObjects.Add(station.AvailableExtinguisher.gameObject);
            return station;
        }

        private void TrackStationAvailable(ExtinguisherStation station)
        {
            if (station.AvailableExtinguisher != null &&
                !createdObjects.Contains(station.AvailableExtinguisher.gameObject))
            {
                createdObjects.Add(station.AvailableExtinguisher.gameObject);
            }
        }

        private ExtinguisherPhysicalGrabber CreatePhysicalGrabber(Transform handAnchor)
        {
            var gameObject = new GameObject("Right Physics Grab Handle");
            gameObject.transform.SetParent(handAnchor, false);
            createdObjects.Add(gameObject);

            Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            ExtinguisherPhysicalGrabber grabber = gameObject.AddComponent<ExtinguisherPhysicalGrabber>();
            grabber.Configure(null, null, handAnchor);
            return grabber;
        }

        private ExtinguisherInteractionDriver CreateInteractionDriver(
            Transform rightHand,
            Transform leftHand,
            FireTrainingManager manager = null,
            ExtinguisherStation station = null)
        {
            var gameObject = new GameObject("Extinguisher Interaction Driver");
            gameObject.transform.SetParent(rightHand, false);
            createdObjects.Add(gameObject);

            ExtinguisherInteractionDriver driver = gameObject.AddComponent<ExtinguisherInteractionDriver>();
            driver.Configure(manager, station, rightHand, leftHand);
            return driver;
        }

        private ExtinguisherController CreatePhysicalExtinguisher(string name, Vector3 position)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.position = position;
            createdObjects.Add(gameObject);

            Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = false;
            rigidbody.useGravity = true;

            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.24f, 0.75f, 0.24f);
            collider.center = new Vector3(0f, 0.35f, 0f);

            CreateChild(gameObject.transform, "Handle", new Vector3(0f, 0.98f, 0f));
            Transform nozzle = CreateChild(gameObject.transform, "Nozzle", new Vector3(0f, 0.92f, 0.28f));
            CreateChild(gameObject.transform, ExtinguisherInteractionDriver.RightGripPoseName, new Vector3(0f, 0.92f, -0.06f));
            CreateChild(gameObject.transform, ExtinguisherInteractionDriver.LeftSupportPoseName, new Vector3(0f, 0.62f, 0.18f));
            CreateChild(gameObject.transform, ExtinguisherInteractionDriver.PinPullZoneName, new Vector3(0f, 1.02f, 0f));

            ExtinguisherController extinguisher = gameObject.AddComponent<ExtinguisherController>();
            SetPrivateObject(extinguisher, "nozzle", nozzle);
            return extinguisher;
        }

        private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        private static TextMeshProUGUI CreateHudText(Transform parent, string name)
        {
            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            return textObject.AddComponent<TextMeshProUGUI>();
        }

        private static GameObject CreateHudPanel(Transform parent, string name)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            return panel;
        }

        private static ParticleSystem CreateTestSprayParticles(Transform nozzle)
        {
            GameObject particleObject = new GameObject("Spray Particles");
            particleObject.transform.SetParent(nozzle, false);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 70f;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static ParticleSystem CreateTestFireParticles(Transform parent, string name)
        {
            GameObject particleObject = new GameObject(name);
            particleObject.transform.SetParent(parent, false);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return particles;
        }

        private static void AssertMeshParticleRenderer(ParticleSystem particleSystem)
        {
            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            Assert.IsNotNull(renderer);
            Assert.AreEqual(ParticleSystemRenderMode.Mesh, renderer.renderMode);
            Assert.AreEqual(ParticleSystemRenderSpace.Local, renderer.alignment);
            Assert.IsNotNull(renderer.mesh);
            Assert.IsFalse(renderer.allowRoll);
            Assert.IsTrue(renderer.enableGPUInstancing);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode);
            Assert.IsFalse(renderer.receiveShadows);
        }

        private static Ray BaseRay(FireTarget fire, Vector3 originOffset)
        {
            Vector3 basePosition = fire.BaseTarget.position;
            Vector3 origin = basePosition + originOffset;
            return new Ray(origin, (basePosition - origin).normalized);
        }

        private static Ray BodyRay(FireTarget fire)
        {
            Vector3 target = fire.transform.position + Vector3.up * 0.45f;
            Vector3 origin = fire.transform.position + new Vector3(0f, 0f, -2f);
            return new Ray(origin, (target - origin).normalized);
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void SetPrivateVector3(object target, string fieldName, Vector3 value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void SetPrivateObject(object target, string fieldName, Object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void SetPrivateBool(object target, string fieldName, bool value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field {fieldName} on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void AssertVectorEqual(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        private static ExtinguisherCapacityGauge EnsureCapacityGauge(ExtinguisherController extinguisher)
        {
            ExtinguisherCapacityGauge gauge = extinguisher.GetComponent<ExtinguisherCapacityGauge>();
            if (gauge == null)
            {
                gauge = extinguisher.gameObject.AddComponent<ExtinguisherCapacityGauge>();
            }

            gauge.ForceRefresh();
            return gauge;
        }

        private static float SignedLocalZAngle(Transform transform)
        {
            return Mathf.DeltaAngle(0f, transform.localEulerAngles.z);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
#endif
