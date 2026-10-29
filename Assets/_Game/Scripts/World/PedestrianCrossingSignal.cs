using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeliveryRider
{
    // Signalized crossing on the main street. Cars clear the crossing before WALK starts.
    public sealed class PedestrianCrossingSignal : MonoBehaviour
    {
        [SerializeField] private DeliverySession session;
        [SerializeField] private TrafficNetwork traffic;
        [SerializeField] private GameObject[] pedestrianRed = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] pedestrianGreen = Array.Empty<GameObject>();
        [SerializeField] private Renderer[] vehicleHeads = Array.Empty<Renderer>();
        [Header("Cycle (test values, seconds)")]
        [SerializeField, Min(1f)] private float driveSeconds = 22f;
        [SerializeField, Min(1f)] private float amberSeconds = 3f;
        [SerializeField, Min(1f)] private float clearanceSeconds = 2f;
        [SerializeField, Min(1f)] private float walkSeconds = 8f;
        [SerializeField] private Vector2 crossingHalfSize = new Vector2(1.3f, 4.5f);

        private enum Phase { Drive, Amber, Clearing, Walk, AfterWalk }
        private Phase phase;
        private float elapsed;
        private struct Lens { public Renderer renderer; public int slot; public int color; }
        private readonly List<Lens> lenses = new List<Lens>();
        private MaterialPropertyBlock block;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Dark = new Color(.035f, .045f, .045f);
        private static readonly Color[] Colors = { new Color(1f,.04f,.015f), new Color(1f,.5f,.01f), new Color(.02f,1f,.16f) };
        public bool VehiclesMustStop => !isActiveAndEnabled || phase != Phase.Drive;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            foreach (var head in vehicleHeads)
            {
                if (head == null) continue;
                var materials = head.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    string name = materials[i].name;
                    int color = name.StartsWith("ST_SignalRed") ? 0 : name.StartsWith("ST_SignalAmber") ? 1 : name.StartsWith("ST_SignalGreen") ? 2 : -1;
                    if (color >= 0) lenses.Add(new Lens { renderer = head, slot = i, color = color });
                }
            }
            SetPhase(Phase.Drive);
        }

        private void Update()
        {
            if (session != null && (session.IsPaused || session.IsSettled)) return;
            if (phase == Phase.Clearing && CrossingOccupied()) { elapsed = 0f; return; }
            elapsed += Time.deltaTime;
            float duration = phase == Phase.Drive ? driveSeconds : phase == Phase.Amber ? amberSeconds : phase == Phase.Walk ? walkSeconds : clearanceSeconds;
            if (elapsed < duration) return;
            SetPhase(phase == Phase.AfterWalk ? Phase.Drive : (Phase)((int)phase + 1));
        }

        private bool CrossingOccupied()
        {
            if (traffic == null) return false;
            foreach (var car in traffic.Cars)
            {
                if (car == null || !car.isActiveAndEnabled) continue;
                Vector3 offset = car.transform.position - transform.position;
                // Conservative bounds include cars turning across the crossing.
                if (Mathf.Abs(offset.x) < crossingHalfSize.x + car.Length * .5f &&
                    Mathf.Abs(offset.z) < crossingHalfSize.y + 1.1f) return true;
            }
            return false;
        }

        private void SetPhase(Phase value)
        {
            phase = value;
            elapsed = 0f;
            ApplyLights(value == Phase.Walk, value == Phase.Drive ? 2 : value == Phase.Amber ? 1 : 0);
        }

        private void ApplyLights(bool walk, int vehicleColor)
        {
            foreach (var light in pedestrianRed) if (light != null) light.SetActive(!walk);
            foreach (var light in pedestrianGreen) if (light != null) light.SetActive(walk);
            if (block == null) return;
            foreach (var lens in lenses)
            {
                // On scene unload OnDisable can run after the signal heads are already destroyed.
                if (lens.renderer == null) continue;
                bool on = lens.color == vehicleColor;
                block.Clear();
                block.SetColor(BaseColor, on ? Colors[lens.color] : Dark);
                block.SetColor(EmissionColor, on ? Colors[lens.color] * 2f : Color.black);
                lens.renderer.SetPropertyBlock(block, lens.slot);
            }
        }

        private void OnDisable() => ApplyLights(false, 0);
    }
}
