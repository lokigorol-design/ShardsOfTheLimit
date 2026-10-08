using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Authoring shape of one particle burst. Colours fade from Start to End over the lifetime.</summary>
    public struct ParticleSpec
    {
        public Color StartColor;
        public Color EndColor;
        public float StartSize;
        public float EndSize;
        public float Lifetime;
        public float LifetimeJitter;
        public Vector3 Velocity;
        public float Spread;
        public float SpeedJitter;
        public Vector3 Gravity;
        public float Drag;
        public float Hardness;
        public bool IsFlame;
        public bool Additive;

        public static ParticleSpec Smoke(Color color, float size = 0.45f, float lifetime = 1.1f) => new ParticleSpec
        {
            StartColor = color,
            EndColor = new Color(color.r, color.g, color.b, 0f),
            StartSize = size * 0.5f,
            EndSize = size,
            Lifetime = lifetime,
            LifetimeJitter = 0.3f,
            Velocity = Vector3.up * 0.35f,
            Spread = 0.35f,
            SpeedJitter = 0.3f,
            Drag = 1.2f,
            Hardness = 0.3f
        };

        public static ParticleSpec Sparks(Color color, float speed = 3f, float size = 0.08f) => new ParticleSpec
        {
            StartColor = color,
            EndColor = new Color(color.r, color.g * 0.4f, color.b * 0.2f, 0f),
            StartSize = size,
            EndSize = size * 0.3f,
            Lifetime = 0.55f,
            LifetimeJitter = 0.25f,
            Velocity = Vector3.up * speed * 0.6f,
            Spread = speed,
            SpeedJitter = 0.5f,
            Gravity = Vector3.down * 6f,
            Drag = 0.6f,
            Hardness = 0.75f,
            Additive = true
        };

        public static ParticleSpec Flame(float size = 0.32f) => new ParticleSpec
        {
            StartColor = new Color(1f, 0.62f, 0.22f, 0.7f),
            EndColor = new Color(0.9f, 0.18f, 0.02f, 0f),
            StartSize = size,
            EndSize = size * 0.35f,
            Lifetime = 0.55f,
            LifetimeJitter = 0.2f,
            Velocity = Vector3.up * 1.1f,
            Spread = 0.25f,
            SpeedJitter = 0.3f,
            Drag = 0.4f,
            Hardness = 0.25f,
            IsFlame = true,
            Additive = true
        };
    }

    /// <summary>
    /// Lightweight particle and debris renderer without the ParticleSystem module (the project keeps its
    /// dependency list unchanged). Particles are camera-facing quads batched into two dynamic meshes.
    /// Presentation only: nothing here takes game decisions.
    /// </summary>
    public sealed class VfxSystem : MonoBehaviour
    {
        private struct Particle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Lifetime;
            public ParticleSpec Spec;
            public float Rotation;
        }

        private sealed class Debris
        {
            public Transform Transform;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Age;
            public float Lifetime;
        }

        private const int MaximumParticles = 2400;
        // Authored sizes are in cells; the orthographic battle camera reads them better slightly enlarged.
        private const float SizeScale = 1.5f;
        private static VfxSystem instance;

        private readonly List<Particle> particles = new List<Particle>(512);
        private readonly List<Debris> debris = new List<Debris>();
        private Mesh alphaMesh;
        private Mesh additiveMesh;
        private Material alphaMaterial;
        private Material additiveMaterial;
        private Material debrisMaterial;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Vector2> shapes = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private Vector3 appliedShake;
        private Vector3 shakenPosition = new Vector3(float.NaN, float.NaN, float.NaN);
        private float shakeAmount;
        private float shakeUntil;

        public static VfxSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new GameObject("VFX System").AddComponent<VfxSystem>();
                }

                return instance;
            }
        }

        public int LiveParticleCount => particles.Count;

        private void Awake()
        {
            instance = this;
            Shader shader = Shader.Find("Shards/VfxParticle") ?? Shader.Find("Sprites/Default");
            alphaMaterial = new Material(shader) { name = "VFX Alpha" };
            alphaMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            alphaMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            alphaMaterial.renderQueue = 3000;
            additiveMaterial = new Material(shader) { name = "VFX Additive" };
            additiveMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            additiveMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            additiveMaterial.renderQueue = 3010;
            alphaMesh = new Mesh { name = "VFX Alpha Particles" };
            alphaMesh.MarkDynamic();
            additiveMesh = new Mesh { name = "VFX Additive Particles" };
            additiveMesh.MarkDynamic();
            debrisMaterial = new Material(BattleEnvironment.LitShader) { name = "VFX Debris" };
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            Destroy(alphaMesh);
            Destroy(additiveMesh);
            Destroy(alphaMaterial);
            Destroy(additiveMaterial);
            Destroy(debrisMaterial);
        }

        public void Emit(Vector3 position, ParticleSpec spec, Vector3? velocity = null)
        {
            if (particles.Count >= MaximumParticles) return;
            Vector3 baseVelocity = velocity ?? spec.Velocity;
            Vector3 random = Random.insideUnitSphere * spec.Spread;
            float speedScale = 1f + Random.Range(-spec.SpeedJitter, spec.SpeedJitter);
            particles.Add(new Particle
            {
                Position = position,
                Velocity = (baseVelocity + random) * speedScale,
                Lifetime = Mathf.Max(0.05f, spec.Lifetime * (1f + Random.Range(-spec.LifetimeJitter, spec.LifetimeJitter))),
                Spec = spec,
                Rotation = Random.Range(0f, 360f)
            });
        }

        public void Burst(Vector3 position, int count, ParticleSpec spec, float radius = 0f)
        {
            for (int index = 0; index < count; index++)
            {
                Vector3 offset = radius > 0f ? Random.insideUnitSphere * radius : Vector3.zero;
                offset.y = Mathf.Abs(offset.y) * 0.4f;
                Emit(position + offset, spec);
            }
        }

        /// <summary>Flat expanding ring of particles on the ground, e.g. a splat or a shock wave.</summary>
        public void Ring(Vector3 center, int count, float speed, ParticleSpec spec)
        {
            for (int index = 0; index < count; index++)
            {
                float angle = index * Mathf.PI * 2f / count + Random.Range(-0.1f, 0.1f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Emit(center + direction * 0.1f, spec, direction * speed + Vector3.up * 0.15f);
            }
        }

        /// <summary>Small solid chunks (rock, mud) thrown out with gravity; they shrink away when they land.</summary>
        public void Chunks(Vector3 position, int count, Color color, float speed, float size = 0.09f)
        {
            for (int index = 0; index < count; index++)
            {
                GameObject chunk = GameObject.CreatePrimitive(index % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                chunk.name = "VFX Chunk";
                Destroy(chunk.GetComponent<Collider>());
                chunk.transform.SetParent(transform, false);
                chunk.transform.position = position + Random.insideUnitSphere * 0.15f;
                chunk.transform.localScale = new Vector3(
                    size * Random.Range(0.6f, 1.4f),
                    size * Random.Range(0.5f, 1.1f),
                    size * Random.Range(0.6f, 1.4f));
                chunk.transform.rotation = Random.rotation;
                Renderer renderer = chunk.GetComponent<Renderer>();
                renderer.sharedMaterial = debrisMaterial;
                var block = new MaterialPropertyBlock();
                Color tint = color * Random.Range(0.75f, 1.15f);
                block.SetColor("_Color", tint);
                block.SetColor("_BaseColor", tint);
                renderer.SetPropertyBlock(block);
                Vector2 planar = Random.insideUnitCircle * speed * 0.55f;
                debris.Add(new Debris
                {
                    Transform = chunk.transform,
                    Velocity = new Vector3(planar.x, speed * Random.Range(0.7f, 1.2f), planar.y),
                    Spin = Random.insideUnitSphere * 540f,
                    Lifetime = Random.Range(0.8f, 1.2f)
                });
            }
        }

        public void Shake(float amount, float duration)
        {
            shakeAmount = Mathf.Max(shakeAmount, amount);
            shakeUntil = Mathf.Max(shakeUntil, Time.unscaledTime + duration);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int index = particles.Count - 1; index >= 0; index--)
            {
                Particle particle = particles[index];
                particle.Age += deltaTime;
                if (particle.Age >= particle.Lifetime)
                {
                    particles.RemoveAt(index);
                    continue;
                }

                particle.Velocity += particle.Spec.Gravity * deltaTime;
                particle.Velocity *= Mathf.Clamp01(1f - particle.Spec.Drag * deltaTime);
                particle.Position += particle.Velocity * deltaTime;
                particles[index] = particle;
            }

            for (int index = debris.Count - 1; index >= 0; index--)
            {
                Debris piece = debris[index];
                piece.Age += deltaTime;
                if (piece.Age >= piece.Lifetime || piece.Transform == null)
                {
                    if (piece.Transform != null) Destroy(piece.Transform.gameObject);
                    debris.RemoveAt(index);
                    continue;
                }

                piece.Velocity += Vector3.down * 9f * deltaTime;
                piece.Transform.position += piece.Velocity * deltaTime;
                piece.Transform.Rotate(piece.Spin * deltaTime, Space.World);
                float fade = 1f - Mathf.Clamp01((piece.Age - piece.Lifetime * 0.6f) / (piece.Lifetime * 0.4f));
                piece.Transform.localScale *= Mathf.Lerp(0.9f, 1f, fade);
            }
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            ApplyShake(camera);
            BuildMesh(alphaMesh, camera, false);
            BuildMesh(additiveMesh, camera, true);
            Graphics.DrawMesh(alphaMesh, Matrix4x4.identity, alphaMaterial, 0);
            Graphics.DrawMesh(additiveMesh, Matrix4x4.identity, additiveMaterial, 0);
        }

        private void ApplyShake(Camera camera)
        {
            // Remove last frame's offset first so the controller's own camera placement stays authoritative;
            // if the controller already re-placed the camera this frame, there is nothing to undo.
            if (camera.transform.position == shakenPosition) camera.transform.position -= appliedShake;
            appliedShake = Vector3.zero;
            if (Time.unscaledTime >= shakeUntil)
            {
                shakeAmount = 0f;
                return;
            }

            float remaining = (shakeUntil - Time.unscaledTime) * 8f;
            appliedShake = (camera.transform.right * Random.Range(-1f, 1f) + camera.transform.up * Random.Range(-1f, 1f)) *
                           shakeAmount * Mathf.Clamp01(remaining);
            camera.transform.position += appliedShake;
            shakenPosition = camera.transform.position;
        }

        private void BuildMesh(Mesh mesh, Camera camera, bool additive)
        {
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            shapes.Clear();
            triangles.Clear();
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;
            foreach (Particle particle in particles)
            {
                if (particle.Spec.Additive != additive) continue;
                float t = particle.Age / particle.Lifetime;
                float size = Mathf.Lerp(particle.Spec.StartSize, particle.Spec.EndSize, t) * 0.5f * SizeScale;
                Color color = Color.Lerp(particle.Spec.StartColor, particle.Spec.EndColor, t);
                // Flames stay upright; smoke and sparks spin slowly for variety.
                float angle = particle.Spec.IsFlame ? 0f : (particle.Rotation + particle.Age * 40f) * Mathf.Deg2Rad;
                Vector3 axisX = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * size;
                Vector3 axisY = (-right * Mathf.Sin(angle) + up * Mathf.Cos(angle)) * size;
                int start = vertices.Count;
                vertices.Add(particle.Position - axisX - axisY);
                vertices.Add(particle.Position - axisX + axisY);
                vertices.Add(particle.Position + axisX + axisY);
                vertices.Add(particle.Position + axisX - axisY);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                var shape = new Vector2(particle.Spec.Hardness, particle.Spec.IsFlame ? 1f : 0f);
                for (int corner = 0; corner < 4; corner++)
                {
                    colors.Add(color);
                    shapes.Add(shape);
                }

                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, shapes);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 200f);
        }
    }
}
