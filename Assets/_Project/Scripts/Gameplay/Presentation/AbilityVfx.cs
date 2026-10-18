using System.Collections;
using System.Collections.Generic;
using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Everything an ability effect needs to know about where it plays. Presentation data only.</summary>
    public sealed class AbilityVfxContext
    {
        public Transform SourceView;
        public Vector3 Source;
        public Vector3 Target;
        public Transform TargetView;
        public IReadOnlyList<Vector3> ImpactCells;
        public Color CloudColor = Color.green;
        /// <summary>The chosen surface point (Primal Idol abilities target a point, not a unit).</summary>
        public Vector3 Point;
    }

    /// <summary>
    /// Signature effects for the Fire Worm and Blight Gullet abilities. Abilities without a signature effect
    /// keep the generic BattleFeedbackView animation.
    /// </summary>
    public static class AbilityVfx
    {
        private static readonly Color MudColor = new Color(0.36f, 0.25f, 0.13f, 0.95f);
        private static readonly Color DustColor = new Color(0.82f, 0.74f, 0.62f, 0.8f);
        private static readonly Color RockColor = new Color(0.42f, 0.36f, 0.3f);

        public static bool TryCreate(string abilityId, AbilityVfxContext context, out IEnumerator routine)
        {
            switch (abilityId)
            {
                case "blaze-fiery-spit":
                    routine = FireSpit(context);
                    return true;
                case "blaze-long-bite":
                    routine = LongBite(context);
                    return true;
                case "blaze-fiery-volley":
                    routine = FieryVolley(context);
                    return true;
                case "MudSpit":
                    routine = MudSpit(context);
                    return true;
                case "ReelingPull":
                    routine = ReelingPull(context);
                    return true;
                case "PoisonCloud":
                case "CorrosionCloud":
                case "OilCloud":
                    routine = ExhaleCloud(context);
                    return true;
                case "StormMagnet":
                    routine = StormMagnet(context);
                    return true;
                case "FireGeyser":
                    routine = RuneCast(context, new Color(1f, 0.45f, 0.12f), 0.35f);
                    return true;
                case "EarthPillar":
                    routine = RuneCast(context, new Color(0.95f, 0.65f, 0.25f), 0.2f);
                    return true;
                case "LiftTrap":
                    routine = LiftTrapCast(context);
                    return true;
                case "PurifyingFlame":
                    routine = PurifyingFlameCast(context);
                    return true;
                case "FlameBurst":
                    routine = FlameBurstCast(context);
                    return true;
                default:
                    routine = null;
                    return false;
            }
        }

        private static IEnumerator FireSpit(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 mouth = context.Source + Vector3.up * 0.2f;
            vfx.Burst(mouth, 10, ParticleSpec.Flame(0.25f));
            yield return Travel(mouth, context.Target, 0.42f, 0.55f, position =>
            {
                vfx.Emit(position, ParticleSpec.Flame(0.38f), Random.insideUnitSphere * 0.3f);
                if (Random.value < 0.5f) vfx.Emit(position, ParticleSpec.Sparks(new Color(1f, 0.7f, 0.3f), 0.8f, 0.06f));
                vfx.Emit(position, ParticleSpec.Smoke(new Color(0.18f, 0.16f, 0.15f, 0.35f), 0.35f, 0.8f));
            });
            FireImpact(context.Target, 1f);
            yield return new WaitForSeconds(0.25f);
        }

        private static IEnumerator FieryVolley(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Burst(context.Source + Vector3.up * 0.3f, 16, ParticleSpec.Flame(0.3f));
            var routines = new List<IEnumerator>();
            foreach (Vector3 cell in context.ImpactCells)
            {
                Vector3 start = cell + Vector3.up * 3.2f + Random.insideUnitSphere * 0.3f;
                routines.Add(Travel(start, cell + Vector3.up * 0.1f, 0.38f + Random.Range(0f, 0.15f), 0f, position =>
                    vfx.Emit(position, ParticleSpec.Flame(0.35f), Random.insideUnitSphere * 0.2f)));
            }

            yield return RunTogether(routines);
            foreach (Vector3 cell in context.ImpactCells) FireImpact(cell, 0.7f);
            yield return new WaitForSeconds(0.3f);
        }

        private static void FireImpact(Vector3 position, float strength)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Burst(position, Mathf.RoundToInt(22 * strength), ParticleSpec.Flame(0.55f), 0.2f);
            vfx.Burst(position, Mathf.RoundToInt(26 * strength), ParticleSpec.Sparks(new Color(1f, 0.75f, 0.3f), 3.4f));
            vfx.Burst(position + Vector3.up * 0.2f, Mathf.RoundToInt(8 * strength),
                ParticleSpec.Smoke(new Color(0.2f, 0.18f, 0.17f, 0.5f), 0.8f, 1.4f), 0.25f);
            vfx.Shake(0.07f * strength, 0.2f);
        }

        private static IEnumerator LongBite(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Transform body = context.SourceView;
            Vector3 origin = body != null ? body.position : context.Source;
            Vector3 direction = context.Target - context.Source;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
            Vector3 lunge = origin + direction * Mathf.Min(0.75f, Vector3.Distance(context.Source, context.Target) * 0.55f);

            // Wind-up: rear back, then strike forward fast.
            yield return MoveBody(body, origin, origin - direction * 0.12f + Vector3.up * 0.06f, 0.12f);
            yield return MoveBody(body, origin - direction * 0.12f, lunge, 0.08f);
            Vector3 bite = context.Target + Vector3.up * 0.15f;
            for (int jaw = -1; jaw <= 1; jaw += 2)
            {
                Vector3 side = Vector3.Cross(direction, Vector3.up) * jaw;
                for (int index = 0; index < 10; index++)
                {
                    float arc = index / 9f * Mathf.PI;
                    Vector3 point = bite + side * Mathf.Sin(arc) * 0.32f + Vector3.up * (Mathf.Cos(arc) * 0.28f * jaw);
                    vfx.Emit(point, ParticleSpec.Sparks(new Color(1f, 0.95f, 0.8f), 0.6f, 0.11f), -side * 1.2f);
                }
            }

            vfx.Burst(bite, 14, ParticleSpec.Sparks(new Color(1f, 0.45f, 0.15f), 2.6f));
            vfx.Ring(context.Target - Vector3.up * 0.25f, 14, 1.4f, ParticleSpec.Smoke(DustColor, 0.4f, 0.7f));
            vfx.Shake(0.09f, 0.18f);
            yield return new WaitForSeconds(0.08f);
            yield return MoveBody(body, lunge, origin, 0.18f);
        }

        private static IEnumerator MudSpit(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 mouth = context.Source + Vector3.up * 0.15f;
            var blob = new ParticleSpec
            {
                StartColor = MudColor,
                EndColor = new Color(MudColor.r, MudColor.g, MudColor.b, 0f),
                StartSize = 0.34f,
                EndSize = 0.2f,
                Lifetime = 0.25f,
                Hardness = 0.65f
            };
            var drip = new ParticleSpec
            {
                StartColor = MudColor,
                EndColor = new Color(MudColor.r, MudColor.g, MudColor.b, 0f),
                StartSize = 0.1f,
                EndSize = 0.06f,
                Lifetime = 0.5f,
                Gravity = Vector3.down * 5f,
                Hardness = 0.7f,
                Spread = 0.2f
            };
            yield return Travel(mouth, context.Target, 0.5f, 0.9f, position =>
            {
                vfx.Emit(position, blob, Vector3.zero);
                if (Random.value < 0.6f) vfx.Emit(position, drip, Vector3.zero);
            });
            vfx.Chunks(context.Target, 9, new Color(0.32f, 0.22f, 0.12f), 2.4f, 0.08f);
            vfx.Ring(context.Target - Vector3.up * 0.2f, 18, 1.8f, drip);
            var puddle = new ParticleSpec
            {
                StartColor = new Color(0.28f, 0.19f, 0.1f, 0.85f),
                EndColor = new Color(0.28f, 0.19f, 0.1f, 0f),
                StartSize = 0.5f,
                EndSize = 0.9f,
                Lifetime = 1.6f,
                Hardness = 0.45f
            };
            vfx.Burst(context.Target - Vector3.up * 0.25f, 5, puddle, 0.2f);
            vfx.Shake(0.04f, 0.12f);
            yield return new WaitForSeconds(0.25f);
        }

        private static IEnumerator ReelingPull(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            var tongueObject = new GameObject("VFX Tongue");
            LineRenderer tongue = tongueObject.AddComponent<LineRenderer>();
            tongue.material = new Material(Shader.Find("Sprites/Default"));
            tongue.startColor = new Color(0.95f, 0.45f, 0.55f);
            tongue.endColor = new Color(0.8f, 0.25f, 0.4f);
            tongue.startWidth = 0.13f;
            tongue.endWidth = 0.08f;
            tongue.positionCount = 12;
            tongue.numCapVertices = 4;
            Vector3 mouth = context.Source + Vector3.up * 0.18f;
            Vector3 tip = context.Target + Vector3.up * 0.25f;

            yield return Animate(0.16f, t => DrawTongue(tongue, mouth, Vector3.Lerp(mouth, tip, t), 1f - t));
            vfx.Burst(tip, 12, new ParticleSpec
            {
                StartColor = new Color(0.9f, 0.6f, 0.7f, 0.9f),
                EndColor = new Color(0.9f, 0.6f, 0.7f, 0f),
                StartSize = 0.09f,
                EndSize = 0.04f,
                Lifetime = 0.45f,
                Spread = 1.6f,
                Gravity = Vector3.down * 4f,
                Hardness = 0.7f
            });
            vfx.Shake(0.05f, 0.1f);
            yield return new WaitForSeconds(0.12f);
            yield return Animate(0.22f, t => DrawTongue(tongue, mouth, Vector3.Lerp(tip, mouth, t * t), 0f));
            Object.Destroy(tongue.material);
            Object.Destroy(tongueObject);
        }

        private static void DrawTongue(LineRenderer tongue, Vector3 from, Vector3 to, float wobble)
        {
            Vector3 side = Vector3.Cross((to - from).normalized, Vector3.up);
            for (int index = 0; index < tongue.positionCount; index++)
            {
                float t = index / (tongue.positionCount - 1f);
                Vector3 point = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.18f;
                point += side * Mathf.Sin(t * Mathf.PI * 3f + Time.time * 30f) * 0.05f * wobble;
                tongue.SetPosition(index, point);
            }
        }

        private static IEnumerator ExhaleCloud(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Color color = context.CloudColor;
            color.a = 0.8f;
            ParticleSpec puff = ParticleSpec.Smoke(color, 0.55f, 0.9f);
            puff.Velocity = Vector3.zero;
            Vector3 mouth = context.Source + Vector3.up * 0.2f;
            // Cheeks inflate: a short pulse of the toad body, then the gas streams to the target.
            if (context.SourceView != null)
            {
                Vector3 scale = context.SourceView.localScale;
                yield return Animate(0.18f, t => context.SourceView.localScale = scale * (1f + Mathf.Sin(t * Mathf.PI) * 0.12f));
                context.SourceView.localScale = scale;
            }

            float elapsed = 0f;
            const float duration = 0.5f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                for (int index = 0; index < 4; index++)
                {
                    Vector3 direction = context.Target - mouth;
                    vfx.Emit(mouth, puff, direction / 0.7f + Random.insideUnitSphere * 0.4f);
                }

                yield return null;
            }

            ParticleSpec bloom = ParticleSpec.Smoke(color, 0.9f, 1.4f);
            bloom.Velocity = Vector3.up * 0.15f;
            foreach (Vector3 cell in context.ImpactCells) vfx.Burst(cell + Vector3.up * 0.25f, 6, bloom, 0.35f);
            yield return new WaitForSeconds(0.2f);
        }

        private static readonly Color LightningColor = new Color(0.62f, 0.5f, 1f);
        private static readonly Color WindColor = new Color(0.35f, 0.95f, 0.9f, 0.7f);

        /// <summary>The totem's mask glows and a rune flares on the chosen point.</summary>
        private static IEnumerator RuneCast(AbilityVfxContext context, Color color, float hold)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 mask = context.Source + Vector3.up * 0.5f;
            ParticleSpec glow = ParticleSpec.Sparks(color, 0.8f, 0.12f);
            glow.Gravity = Vector3.up * 0.6f;
            vfx.Burst(mask, 18, glow, 0.15f);
            yield return new WaitForSeconds(0.15f);
            Rune(context.Point, color, 22);
            yield return new WaitForSeconds(hold);
        }

        private static void Rune(Vector3 point, Color color, int count)
        {
            var spark = new ParticleSpec
            {
                StartColor = color,
                EndColor = new Color(color.r, color.g, color.b, 0f),
                StartSize = 0.14f,
                EndSize = 0.05f,
                Lifetime = 0.7f,
                Hardness = 0.7f,
                Additive = true
            };
            for (int index = 0; index < count; index++)
            {
                float angle = index * Mathf.PI * 2f / count;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.38f;
                VfxSystem.Instance.Emit(point + offset + Vector3.up * 0.05f, spark, Vector3.up * 0.4f);
            }
        }

        /// <summary>Storm Magnet: a violet chain of lightning from the point to the first resonant on its line.</summary>
        private static IEnumerator StormMagnet(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Rune(context.Point, LightningColor, 20);
            if (context.TargetView == null)
            {
                yield return new WaitForSeconds(0.3f);
                yield break;
            }

            var boltObject = new GameObject("VFX Lightning");
            LineRenderer bolt = boltObject.AddComponent<LineRenderer>();
            bolt.material = new Material(Shader.Find("Sprites/Default"));
            bolt.startColor = new Color(0.85f, 0.8f, 1f);
            bolt.endColor = LightningColor;
            bolt.startWidth = 0.09f;
            bolt.endWidth = 0.05f;
            bolt.positionCount = 10;
            Vector3 from = context.Point + Vector3.up * 0.3f;
            yield return Animate(0.45f, t =>
            {
                Vector3 to = context.TargetView.position + Vector3.up * 0.3f;
                for (int index = 0; index < bolt.positionCount; index++)
                {
                    float k = index / (bolt.positionCount - 1f);
                    Vector3 jitter = index == 0 || index == bolt.positionCount - 1 ? Vector3.zero : Random.insideUnitSphere * 0.14f;
                    bolt.SetPosition(index, Vector3.Lerp(from, to, k) + jitter);
                }

                if (Random.value < 0.5f)
                    vfx.Emit(Vector3.Lerp(from, to, Random.value), ParticleSpec.Sparks(LightningColor, 1.4f, 0.07f));
            });
            vfx.Burst(context.TargetView.position, 16, ParticleSpec.Sparks(new Color(0.8f, 0.75f, 1f), 2.4f, 0.08f));
            vfx.Shake(0.05f, 0.12f);
            Object.Destroy(bolt.material);
            Object.Destroy(boltObject);
        }

        private static IEnumerator LiftTrapCast(AbilityVfxContext context)
        {
            yield return RuneCast(context, WindColor, 0.05f);
            ParticleSpec swirl = ParticleSpec.Smoke(WindColor, 0.35f, 0.8f);
            swirl.Velocity = Vector3.up * 1.4f;
            for (int index = 0; index < 24; index++)
            {
                float angle = index * 0.6f;
                VfxSystem.Instance.Emit(context.Point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.3f, swirl,
                    new Vector3(-Mathf.Sin(angle), 1.2f, Mathf.Cos(angle)));
            }

            yield return new WaitForSeconds(0.25f);
        }

        /// <summary>Pending Fire Geyser mark: a smouldering ring that everyone can see.</summary>
        /// <summary>Purifying Flame: a ring of white-gold fire climbs the resonant and burns its afflictions away.</summary>
        private static IEnumerator PurifyingFlameCast(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 feet = context.Source - Vector3.up * 0.45f;
            for (int wave = 0; wave < 6; wave++)
            {
                ParticleSpec flame = ParticleSpec.Flame(0.3f);
                flame.StartColor = new Color(1f, 0.92f, 0.6f, 1f);
                vfx.Ring(feet + Vector3.up * wave * 0.16f, 16, 0.8f, flame);
                yield return new WaitForSeconds(0.05f);
            }

            vfx.Burst(context.Source + Vector3.up * 0.4f, 20, ParticleSpec.Sparks(new Color(1f, 0.95f, 0.7f), 2.2f));
            yield return new WaitForSeconds(0.2f);
        }

        /// <summary>Flame Burst: the worm opens its maw and a cone of fire rolls over the cells.</summary>
        private static IEnumerator FlameBurstCast(AbilityVfxContext context)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 mouth = context.Source + Vector3.up * 0.2f;
            vfx.Burst(mouth, 14, ParticleSpec.Flame(0.35f));
            float elapsed = 0f;
            while (elapsed < 0.45f)
            {
                elapsed += Time.deltaTime;
                foreach (Vector3 cell in context.ImpactCells)
                {
                    Vector3 toCell = cell + Vector3.up * 0.25f - mouth;
                    vfx.Emit(mouth + toCell * Random.Range(0.1f, 1f), ParticleSpec.Flame(0.42f), toCell.normalized * 1.4f);
                }

                vfx.Shake(0.02f, 0.05f);
                yield return null;
            }

            foreach (Vector3 cell in context.ImpactCells) FireImpact(cell, 0.45f);
            yield return new WaitForSeconds(0.2f);
        }

        private static readonly Color StoneLight = new Color(1f, 0.85f, 0.45f, 1f);

        /// <summary>The resonance stone answers a summon or recall: a short golden flare at the active resonant.</summary>
        public static void StoneCommand(Vector3 position)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Burst(position + Vector3.up * 0.7f, 18, ParticleSpec.Sparks(StoneLight, 1.8f, 0.07f));
            vfx.Ring(position - Vector3.up * 0.5f, 18, 1.1f, ParticleSpec.Smoke(new Color(1f, 0.8f, 0.4f, 0.35f), 0.25f, 0.6f));
        }

        /// <summary>Recall: the resonant dissolves into light that streams up into the stone.</summary>
        public static IEnumerator RecallBeam(Transform body)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 start = body.position;
            Vector3 scale = body.localScale;
            float elapsed = 0f;
            const float duration = 0.6f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                body.localScale = new Vector3(scale.x * (1f - t * 0.8f), scale.y * (1f + t * 0.6f), scale.z * (1f - t * 0.8f));
                body.position = start + Vector3.up * t * 0.4f;
                ParticleSpec mote = ParticleSpec.Sparks(StoneLight, 0.4f, 0.06f);
                mote.Velocity = Vector3.up * 3.5f;
                for (int index = 0; index < 4; index++)
                    vfx.Emit(start + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.5f, 0.6f), Random.Range(-0.3f, 0.3f)), mote);
                yield return null;
            }

            vfx.Burst(start + Vector3.up * 0.5f, 24, ParticleSpec.Sparks(StoneLight, 2.5f, 0.07f));
        }

        /// <summary>Summon: a ring of light opens on the cell and the resonant rises out of it.</summary>
        public static IEnumerator SummonPortal(Transform body, Vector3 ground)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 target = body.position;
            Vector3 scale = body.localScale;
            body.position = target - Vector3.up * 0.9f;
            float elapsed = 0f;
            const float duration = 0.55f;
            vfx.Ring(ground + Vector3.up * 0.05f, 28, 1.6f, ParticleSpec.Sparks(StoneLight, 0.5f, 0.08f));
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                body.position = Vector3.Lerp(target - Vector3.up * 0.9f, target, t);
                body.localScale = scale * Mathf.Lerp(0.6f, 1f, t);
                ParticleSpec column = ParticleSpec.Sparks(StoneLight, 0.3f, 0.05f);
                column.Velocity = Vector3.up * 2.4f;
                vfx.Emit(ground + new Vector3(Random.Range(-0.35f, 0.35f), 0.05f, Random.Range(-0.35f, 0.35f)), column);
                yield return null;
            }

            body.position = target;
            body.localScale = scale;
            vfx.Burst(ground + Vector3.up * 0.3f, 20, ParticleSpec.Smoke(new Color(1f, 0.85f, 0.55f, 0.4f), 0.4f, 0.8f), 0.3f);
            vfx.Shake(0.05f, 0.15f);
        }

        /// <summary>Invisibility shimmers faintly; Mist wraps the resonant in drifting white fog.</summary>
        public static void StealthMark(Vector3 position, bool mist)
        {
            VfxSystem vfx = VfxSystem.Instance;
            if (mist)
            {
                ParticleSpec fog = ParticleSpec.Smoke(new Color(0.85f, 0.9f, 0.95f, 0.35f), 0.55f, 1.4f);
                fog.Velocity = new Vector3(Random.Range(-0.15f, 0.15f), 0.12f, Random.Range(-0.15f, 0.15f));
                vfx.Emit(position + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.4f, 0.3f), Random.Range(-0.35f, 0.35f)), fog);
                return;
            }

            if (Random.value < 0.5f)
                vfx.Emit(position + Random.insideUnitSphere * 0.4f, ParticleSpec.Sparks(new Color(0.6f, 0.7f, 1f, 0.6f), 0.2f, 0.04f));
        }

        /// <summary>A resonant calling its replacement: a golden ring of stone light rises around it (it is vulnerable).</summary>
        public static void SummoningMark(Vector3 position)
        {
            float angle = Time.time * 3f + Random.value * Mathf.PI * 2f;
            ParticleSpec mote = ParticleSpec.Sparks(StoneLight, 0.3f, 0.06f);
            mote.Velocity = Vector3.up * 0.9f;
            VfxSystem.Instance.Emit(position + new Vector3(Mathf.Cos(angle) * 0.45f, -0.5f, Mathf.Sin(angle) * 0.45f), mote);
        }

        /// <summary>Short fire flare on one cell (worm entry).</summary>
        public static void FireBurstAt(Vector3 position) => FireImpact(position, 0.5f);

        /// <summary>Magma on a cell: glowing crust with lazy sparks.</summary>
        public static void MagmaMark(Vector3 point)
        {
            VfxSystem vfx = VfxSystem.Instance;
            ParticleSpec glow = ParticleSpec.Flame(0.22f);
            glow.StartColor = new Color(1f, 0.32f, 0.04f, 0.9f);
            glow.Velocity = Vector3.up * 0.15f;
            vfx.Emit(point + new Vector3(Random.Range(-0.4f, 0.4f), 0.04f, Random.Range(-0.4f, 0.4f)), glow);
            if (Random.value < 0.25f) vfx.Emit(point + Vector3.up * 0.05f, ParticleSpec.Sparks(new Color(1f, 0.55f, 0.15f), 0.6f, 0.05f));
        }

        /// <summary>An active Flame Burst cone keeps flickering until it goes out.</summary>
        public static void FlameConeMark(Vector3 point)
        {
            if (Random.value < 0.5f) return;
            ParticleSpec flame = ParticleSpec.Flame(0.28f);
            flame.Velocity = Vector3.up * 0.6f;
            VfxSystem.Instance.Emit(point + new Vector3(Random.Range(-0.35f, 0.35f), 0.1f, Random.Range(-0.35f, 0.35f)), flame);
        }

        public static void GeyserMark(Vector3 point)
        {
            Rune(point, new Color(1f, 0.4f, 0.1f), 3);
            if (Random.value < 0.3f) VfxSystem.Instance.Emit(point + Vector3.up * 0.1f, ParticleSpec.Smoke(new Color(0.25f, 0.2f, 0.18f, 0.5f), 0.35f, 0.9f));
        }

        /// <summary>Fire Geyser fires: a column of flame shoots up from the point.</summary>
        public static void GeyserEruption(Vector3 point)
        {
            VfxSystem vfx = VfxSystem.Instance;
            for (int index = 0; index < 60; index++)
            {
                ParticleSpec flame = ParticleSpec.Flame(0.55f);
                flame.Lifetime = 0.7f;
                vfx.Emit(point + Random.insideUnitSphere * 0.15f, flame, Vector3.up * Random.Range(2.5f, 5f) + Random.insideUnitSphere * 0.4f);
            }

            vfx.Burst(point, 30, ParticleSpec.Sparks(new Color(1f, 0.7f, 0.25f), 4f));
            vfx.Burst(point + Vector3.up * 1.4f, 10, ParticleSpec.Smoke(new Color(0.2f, 0.18f, 0.17f, 0.55f), 1f, 1.6f), 0.3f);
            vfx.Shake(0.1f, 0.25f);
        }

        /// <summary>Lift Trap waiting on a cell: a soft turquoise eddy.</summary>
        public static void TrapMark(Vector3 point)
        {
            float angle = Time.time * 4f + Random.value;
            ParticleSpec eddy = ParticleSpec.Smoke(WindColor, 0.2f, 0.6f);
            eddy.Velocity = Vector3.up * 0.4f;
            VfxSystem.Instance.Emit(point + new Vector3(Mathf.Cos(angle), 0.05f, Mathf.Sin(angle)) * 0.3f, eddy);
        }

        /// <summary>Lift Trap fires: a tornado throws the victim up; it crashes back down.</summary>
        public static IEnumerator LiftTrapToss(Transform victim, Vector3 ground)
        {
            VfxSystem vfx = VfxSystem.Instance;
            ParticleSpec swirl = ParticleSpec.Smoke(WindColor, 0.5f, 0.9f);
            for (int index = 0; index < 40; index++)
            {
                float angle = index * 0.5f;
                vfx.Emit(ground + new Vector3(Mathf.Cos(angle), index * 0.05f, Mathf.Sin(angle)) * 0.35f, swirl,
                    new Vector3(-Mathf.Sin(angle) * 1.5f, 2.2f, Mathf.Cos(angle) * 1.5f));
            }

            if (victim == null) yield break;
            Vector3 start = victim.position;
            yield return Animate(0.35f, t => victim.position = start + Vector3.up * Mathf.Sin(t * Mathf.PI * 0.5f) * 2.2f);
            yield return Animate(0.22f, t => victim.position = start + Vector3.up * (1f - t * t) * 2.2f);
            victim.position = start;
            vfx.Ring(ground, 18, 2f, ParticleSpec.Smoke(DustColor, 0.5f, 0.8f));
            vfx.Chunks(ground, 6, RockColor, 2f);
            vfx.Shake(0.1f, 0.2f);
        }

        /// <summary>Earth Pillar: the column grinds upward with dust and stones.</summary>
        public static void PillarRumble(Vector3 ground)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Ring(ground, 20, 1.8f, ParticleSpec.Smoke(DustColor, 0.55f, 1f));
            vfx.Chunks(ground, 10, new Color(0.62f, 0.48f, 0.3f), 2.6f);
            vfx.Shake(0.09f, 0.35f);
        }

        /// <summary>Deep Burrow: the ground cracks, the worm sinks, a dust line runs underground, it erupts.</summary>
        public static void BurrowDig(Vector3 groundPosition)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Ring(groundPosition, 20, 2.2f, ParticleSpec.Smoke(DustColor, 0.5f, 0.9f));
            vfx.Chunks(groundPosition, 8, RockColor, 2.2f);
            vfx.Shake(0.06f, 0.25f);
        }

        public static void BurrowTrail(Vector3 groundPosition)
        {
            ParticleSpec dust = ParticleSpec.Smoke(DustColor, 0.35f, 0.7f);
            dust.Velocity = Vector3.up * 0.5f;
            VfxSystem.Instance.Emit(groundPosition + Random.insideUnitSphere * 0.15f, dust);
        }

        public static void BurrowEmerge(Vector3 groundPosition)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Ring(groundPosition, 26, 2.8f, ParticleSpec.Smoke(DustColor, 0.6f, 1.1f));
            vfx.Chunks(groundPosition, 14, RockColor, 3.4f, 0.11f);
            vfx.Burst(groundPosition, 12, ParticleSpec.Sparks(new Color(1f, 0.6f, 0.2f), 2.5f));
            vfx.Shake(0.12f, 0.3f);
        }

        /// <summary>Generic hit spark used for every damage result.</summary>
        public static void Hit(Vector3 position, bool shieldOnly)
        {
            VfxSystem vfx = VfxSystem.Instance;
            Color color = shieldOnly ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.35f, 0.25f);
            vfx.Burst(position, 10, ParticleSpec.Sparks(color, 2f, 0.07f));
        }

        public static void Defeat(Vector3 position)
        {
            VfxSystem vfx = VfxSystem.Instance;
            vfx.Burst(position, 14, ParticleSpec.Smoke(new Color(0.3f, 0.28f, 0.28f, 0.7f), 0.8f, 1.5f), 0.3f);
            vfx.Chunks(position, 6, new Color(0.25f, 0.22f, 0.2f), 2f);
            vfx.Shake(0.08f, 0.25f);
        }

        private static IEnumerator Travel(Vector3 from, Vector3 to, float duration, float arcHeight, System.Action<Vector3> onStep)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * arcHeight;
                onStep(position);
                yield return null;
            }
        }

        private static IEnumerator MoveBody(Transform body, Vector3 from, Vector3 to, float duration)
        {
            if (body == null) yield break;
            yield return Animate(duration, t => body.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)));
            body.position = to;
        }

        private static IEnumerator Animate(float duration, System.Action<float> step)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                step(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            step(1f);
        }

        private static IEnumerator RunTogether(List<IEnumerator> routines)
        {
            bool running = true;
            while (running)
            {
                running = false;
                foreach (IEnumerator routine in routines)
                {
                    if (routine.MoveNext()) running = true;
                }

                yield return null;
            }
        }
    }

    /// <summary>Ongoing status visuals on a resonant: flames, poison bubbles, oil drips, corrosion fizz, mud.</summary>
    public sealed class StatusAuraView : MonoBehaviour
    {
        private CombatUnit unit;
        private float flameBudget;
        private float statusBudget;

        public void Initialize(CombatUnit combatUnit) => unit = combatUnit;

        private void Update()
        {
            if (unit == null || unit.IsDefeated) return;
            VfxSystem vfx = VfxSystem.Instance;
            Vector3 feet = transform.position - Vector3.up * 0.5f;
            if (unit.IsBurning)
            {
                flameBudget += Time.deltaTime * 22f;
                while (flameBudget >= 1f)
                {
                    flameBudget -= 1f;
                    vfx.Emit(feet + Vector3.up * Random.Range(0.1f, 0.7f) + Random.insideUnitSphere * 0.25f, ParticleSpec.Flame(0.3f));
                }
            }

            statusBudget += Time.deltaTime * 6f;
            if (statusBudget < 1f) return;
            statusBudget -= 1f;
            if (unit.GetMechanicStacks(StatusMechanic.Poison) > 0)
            {
                ParticleSpec bubble = ParticleSpec.Smoke(new Color(0.45f, 0.95f, 0.15f, 0.8f), 0.12f, 1f);
                bubble.Hardness = 0.6f;
                bubble.Velocity = Vector3.up * 0.6f;
                vfx.Emit(feet + Vector3.up * 0.4f + Random.insideUnitSphere * 0.3f, bubble);
            }

            if (unit.GetMechanicStacks(StatusMechanic.Oil) > 0)
            {
                var drip = new ParticleSpec
                {
                    StartColor = new Color(0.1f, 0.06f, 0.14f, 0.9f),
                    EndColor = new Color(0.1f, 0.06f, 0.14f, 0f),
                    StartSize = 0.1f,
                    EndSize = 0.07f,
                    Lifetime = 0.6f,
                    Gravity = Vector3.down * 3f,
                    Hardness = 0.7f
                };
                vfx.Emit(feet + Vector3.up * 0.8f + Random.insideUnitSphere * 0.25f, drip, Vector3.zero);
            }

            if (unit.GetMechanicStacks(StatusMechanic.Corrosion) > 0)
            {
                ParticleSpec fizz = ParticleSpec.Sparks(new Color(1f, 0.75f, 0.1f), 0.6f, 0.06f);
                fizz.Gravity = Vector3.up * 0.5f;
                vfx.Emit(feet + Vector3.up * 0.5f + Random.insideUnitSphere * 0.3f, fizz);
            }

            if (unit.GetMechanicStacks(StatusMechanic.Slow) > 0 || unit.IsImmobilized)
            {
                var mud = new ParticleSpec
                {
                    StartColor = new Color(0.3f, 0.2f, 0.1f, 0.8f),
                    EndColor = new Color(0.3f, 0.2f, 0.1f, 0f),
                    StartSize = 0.25f,
                    EndSize = 0.45f,
                    Lifetime = 0.9f,
                    Hardness = 0.4f
                };
                vfx.Emit(feet + Vector3.up * 0.05f + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)), mud,
                    Vector3.zero);
            }
        }
    }

    /// <summary>A cloud cell: a faint ground stain plus continuously drifting smoke puffs in the cloud colour.</summary>
    public sealed class CloudPuffView : MonoBehaviour
    {
        private Color color;
        private float height;
        private float budget;

        public void Initialize(Color cloudColor, float volumeHeight)
        {
            color = cloudColor;
            height = volumeHeight;
        }

        private void Update()
        {
            budget += Time.deltaTime * 8f;
            while (budget >= 1f)
            {
                budget -= 1f;
                Color puffColor = color;
                puffColor.a = Mathf.Clamp(color.a * 1.8f, 0.4f, 0.75f);
                ParticleSpec puff = ParticleSpec.Smoke(puffColor, 0.95f, 1.8f);
                puff.Velocity = new Vector3(Random.Range(-0.1f, 0.1f), 0.08f, Random.Range(-0.1f, 0.1f));
                puff.Spread = 0.05f;
                Vector3 position = transform.position + new Vector3(Random.Range(-0.4f, 0.4f),
                    Random.Range(0.05f, height), Random.Range(-0.4f, 0.4f));
                VfxSystem.Instance.Emit(position, puff);
            }
        }
    }
}
