using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Test of procedural animation without rigs: breathing, walking bounce, attack lunge, hit squash and death.
    /// It animates the model's direct children inside the unit root (the root itself is moved and turned by the
    /// battle), scaling about the model's feet so the model stays on the ground. Child rotations are never touched
    /// while the resonant is alive, so authored model corrections are preserved.
    /// </summary>
    public sealed class ProceduralAnimator : MonoBehaviour
    {
        private sealed class Part
        {
            public Transform Transform;
            public Vector3 Position;
            public Vector3 Scale;
        }

        private readonly List<Part> parts = new List<Part>();
        private float footY;
        private float phase;
        private float breathAmplitude = 0.025f;
        private float hover;
        private Vector3 lastRootPosition;
        private float walkBlend;
        private float walkCycle;
        private Vector3 actionOffset;
        private float actionSquash;

        public bool IsPlayingAction { get; private set; }
        public bool IsDying { get; private set; }

        /// <summary>Captures the model parts present now; call before auras and markers are added.</summary>
        public void Initialize(float levitation = 0f)
        {
            parts.Clear();
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            footY = 0f;
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                footY = transform.InverseTransformPoint(new Vector3(0f, bounds.min.y, 0f)).y;
            }

            foreach (Transform child in transform)
                parts.Add(new Part { Transform = child, Position = child.localPosition, Scale = child.localScale });
            phase = Random.value * Mathf.PI * 2f;
            hover = levitation;
            breathAmplitude = levitation > 0f ? 0.012f : 0.025f;
            lastRootPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (parts.Count == 0) return;
            float time = Time.time;
            Vector3 rootDelta = transform.position - lastRootPosition;
            lastRootPosition = transform.position;
            rootDelta.y = 0f;
            float speed = Time.deltaTime > 0f ? rootDelta.magnitude / Time.deltaTime : 0f;
            walkBlend = Mathf.MoveTowards(walkBlend, speed > 0.4f ? 1f : 0f, Time.deltaTime * 6f);
            walkCycle += Time.deltaTime * 11f * walkBlend;

            // Breathing about the feet; walking adds a stepping bounce; the idol floats.
            float breath = 1f + Mathf.Sin(time * 2.6f + phase) * breathAmplitude * (1f - walkBlend);
            float squash = 1f - actionSquash;
            float bounce = Mathf.Abs(Mathf.Sin(walkCycle)) * 0.07f * walkBlend;
            float floatY = hover > 0f ? hover + Mathf.Sin(time * 1.7f + phase) * hover * 0.6f : 0f;
            var scale = new Vector3(breath / Mathf.Sqrt(squash), breath * squash, breath / Mathf.Sqrt(squash));
            Vector3 feet = new Vector3(0f, footY, 0f);
            foreach (Part part in parts)
            {
                if (part.Transform == null) continue;
                Vector3 fromFeet = part.Position - feet;
                part.Transform.localPosition = feet + Vector3.Scale(fromFeet, scale) +
                                               Vector3.up * (bounce + floatY) + actionOffset;
                part.Transform.localScale = Vector3.Scale(part.Scale, scale);
            }
        }

        /// <summary>Melee: rear back, lunge forward along the facing, return. Ranged: a short recoil.</summary>
        public IEnumerator Lunge(bool melee)
        {
            IsPlayingAction = true;
            if (melee)
            {
                yield return Offset(new Vector3(0f, 0.04f, -0.12f), 0.1f);
                yield return Offset(new Vector3(0f, 0f, 0.38f), 0.07f);
                yield return Offset(Vector3.zero, 0.16f);
            }
            else
            {
                actionSquash = 0.12f;
                yield return Offset(new Vector3(0f, 0.02f, -0.14f), 0.06f);
                actionSquash = 0f;
                yield return Offset(Vector3.zero, 0.18f);
            }

            IsPlayingAction = false;
        }

        /// <summary>Hit: squash and knock back away from the facing, then spring back.</summary>
        public IEnumerator Hit()
        {
            float elapsed = 0f;
            const float duration = 0.32f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float spring = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t);
                actionSquash = 0.18f * spring;
                actionOffset = new Vector3(0f, 0f, -0.12f * Mathf.Max(0f, spring));
                yield return null;
            }

            actionSquash = 0f;
            actionOffset = Vector3.zero;
        }

        /// <summary>Death: topple to the side and sink into the ground, then hide the unit.</summary>
        public IEnumerator Die()
        {
            IsDying = true;
            enabled = false;
            Vector3 start = transform.position;
            Quaternion from = transform.rotation;
            Quaternion to = from * Quaternion.Euler(0f, 0f, 80f);
            float elapsed = 0f;
            const float duration = 0.7f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.6f)));
                transform.position = start + Vector3.down * Mathf.Max(0f, t - 0.5f) * 1.6f;
                yield return null;
            }

            gameObject.SetActive(false);
            transform.position = start;
            transform.rotation = from;
            enabled = true;
            IsDying = false;
        }

        private IEnumerator Offset(Vector3 target, float duration)
        {
            Vector3 from = actionOffset;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                actionOffset = Vector3.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }

            actionOffset = target;
        }
    }
}
