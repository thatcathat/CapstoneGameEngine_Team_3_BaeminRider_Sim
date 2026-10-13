using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace DeliveryRider.Editor
{
    // A bounded live-editor check: keyboard input and real physics collisions, no gameplay test hooks.
    public static class PrototypePlayChecks
    {
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string error;
            public List<string> checks = new List<string>();
        }
        private static Report report;
        private static int step;
        private static double due;
        private static double timeout;
        private static Keyboard keyboard;
        private static DeliverySession session;
        private static RiderMotor motor;
        private static Rigidbody body;
        private static DeliveryResident customer;
        private static Vector3 before;
        private static bool previousBackgroundMode;
        public static string Status { get; private set; } = "Not run";

        public static void Run()
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "TitleScene")
                throw new InvalidOperationException("Run checks in Play Mode from TitleScene.");
            if (Status == "Running") throw new InvalidOperationException("Checks already running.");
            previousBackgroundMode = Application.runInBackground;
            Application.runInBackground = true;
            report = new Report(); step = 0; due = 0; timeout = EditorApplication.timeSinceStartup + 25;
            Status = "Running";
            var gameView = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(gameView).Focus();
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (!EditorApplication.isPlaying) throw new Exception("Play Mode ended during checks.");
                if (EditorApplication.timeSinceStartup > timeout) throw new Exception("Checks timed out.");
                if (EditorApplication.timeSinceStartup < due) return;
                switch (step)
                {
                    case 0:
                        Click("StartButton"); Next(1); break;
                    case 1:
                        Check(SceneManager.GetActiveScene().name == "FourDistrictPrototype", "Title button loads the four-district game scene");
                        session = UnityEngine.Object.FindFirstObjectByType<DeliverySession>();
                        motor = UnityEngine.Object.FindFirstObjectByType<RiderMotor>(); body = motor.GetComponent<Rigidbody>();
                        Check(session.Stage == DeliveryStage.Available && !session.IsMapOpen, "Fresh game starts without an open call screen");
                        Check(session.OpenCall() && session.IsCallOpen && Time.timeScale == 0, "Key-1 call screen opens and stops time");
                        Click("Accept");
                        Check(session.Stage == DeliveryStage.Pickup && Time.timeScale == 1, "Accepting a call closes the call screen");
                        Check(!session.HasPickedUp && session.OrderRemainingSeconds > 0, "Delivery clock waits for pickup");
                        session.AcceptOrder();
                        Check(session.Stage == DeliveryStage.Pickup, "Repeated accept does not advance the order");
                        customer = session.ActiveOrder.Recipient;
                        Check(!session.TryPickup(), "Pickup rejected outside restaurant range");
                        Check(!session.TryDeliver(customer), "Delivery rejected without food");
                        keyboard = InputSystem.AddDevice<Keyboard>();
                        before = body.position; Keys(Key.W); Next(1); break;
                    case 2:
                        Keys();
                        Check(Vector3.Distance(body.position, before) > 2f, "W input moves the physical player");
                        body.position = session.ActiveOrder.PickupPoint.position + Vector3.right * 0.8f;
                        body.linearVelocity = Vector3.zero; Next(0.3); break;
                    case 3:
                        Next(0.15); break;
                    case 4:
                        Check(session.Stage == DeliveryStage.Carrying && session.HasPickedUp, "Entering the shop range picks up food and starts the clock");
                        DeliveryResident other = null;
                        foreach (var resident in UnityEngine.Object.FindObjectsByType<DeliveryResident>(FindObjectsSortMode.None))
                            if (resident != customer) { other = resident; break; }
                        if (other != null) Check(!other.ReceivePunch(Vector3.forward) && session.Stage == DeliveryStage.Carrying, "Punching another resident does not complete the order");
                        session.TogglePause();
                        Check(Time.timeScale == 0 && !session.CanMove && !session.TryDeliver(customer), "Pause blocks movement and delivery");
                        session.TogglePause();
                        Check(Time.timeScale == 1 && session.CanMove, "Resume restores gameplay");
                        body.position = customer.transform.position + Vector3.back * 2.1f;
                        body.linearVelocity = Vector3.zero; Next(0.25); break;
                    case 5:
                        Keys(Key.W); Next(1); break;
                    case 6:
                        Keys();
                        Check(session.Stage == DeliveryStage.Carrying, "Walking into the recipient does not complete the order");
                        Check(customer.ReceivePunch(Vector3.forward) && session.Stage == DeliveryStage.Complete && session.CompletedCount == 1, "Punching the recipient completes one delivery");
                        Check(!customer.ReceivePunch(Vector3.forward) && session.CompletedCount == 1, "Repeated punches cannot double count");
                        Check(!session.IsMapOpen, "Completion does not open the call screen automatically");
                        session.HandleCallKey(0); session.HandleCallKey(0);
                        Check(session.Stage == DeliveryStage.Pickup && session.CompletedCount == 1, "Key 1 opens and accepts the next call");
                        body.position = new Vector3(0, -12, 0); Next(0.3); break;
                    case 7:
                        Check(body.position.y > 0 && session.Stage == DeliveryStage.Pickup, "Out-of-bounds recovery preserves the order");
                        session.TogglePause(); Click("Title"); Next(1); break;
                    case 8:
                        Check(SceneManager.GetActiveScene().name == "TitleScene" && Time.timeScale == 1, "Pause menu returns to title and restores time");
                        Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Title restores the cursor");
                        Finish(null); break;
                }
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }

        private static void Next(double delay) { step++; due = EditorApplication.timeSinceStartup + delay; }
        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            report.checks.Add(description);
        }
        private static void Click(string name)
        {
            var button = GameObject.Find(name).GetComponent<UnityEngine.UI.Button>();
            var canvas = button.GetComponentInParent<Canvas>();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, button.transform.position),
                button = PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>() == button, name + " is reachable by UI raycast");
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            Application.runInBackground = previousBackgroundMode;
            report.passed = error == null; report.error = error; Status = report.passed ? "Passed" : "Failed";
            Directory.CreateDirectory("Docs/Verification");
            File.WriteAllText("Docs/Verification/first-delivery-checks.json", JsonUtility.ToJson(report, true));
            if (report.passed) Debug.Log("Prototype play checks passed: " + report.checks.Count);
            else Debug.LogError("Prototype play checks failed: " + error);
        }
    }
}
