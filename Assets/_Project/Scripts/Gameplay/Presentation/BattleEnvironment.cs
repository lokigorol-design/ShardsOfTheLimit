using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>
    /// Built-in render pipeline look of the battle: soft-shadowed key light, trilight ambient, fog, sky,
    /// procedural stone textures for the field and rock formations for obstacles. No new packages.
    /// </summary>
    public static class BattleEnvironment
    {
        private const int TextureSize = 256;
        private static Texture2D stoneAlbedo;
        private static Texture2D stoneNormal;
        private static Texture2D groundAlbedo;
        private static Texture2D groundNormal;
        private static Material rockMaterial;

        /// <summary>URP Lit when the project runs on URP, Standard otherwise; both read colour and textures the same way.</summary>
        public static Shader LitShader =>
            (GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : null) ??
            Shader.Find("Standard");

        private static void SetSmoothness(Material material, float value)
        {
            material.SetFloat("_Glossiness", value);
            material.SetFloat("_Smoothness", value);
        }

        public static void ConfigureLighting(Light light, Camera camera)
        {
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.3f;
            light.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.High;
            QualitySettings.shadowDistance = 70f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.antiAliasing = 4;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Warm kitchen light: daylight from the window plus bounce from the wooden table and the walls.
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.6f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.42f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.22f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.7f, 0.58f);
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 260f;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "Battle Sky" };
                sky.SetFloat("_SunSize", 0.03f);
                sky.SetFloat("_AtmosphereThickness", 1.15f);
                sky.SetColor("_SkyTint", new Color(0.55f, 0.6f, 0.7f));
                sky.SetColor("_GroundColor", new Color(0.3f, 0.29f, 0.28f));
                sky.SetFloat("_Exposure", 1.1f);
                RenderSettings.skybox = sky;
                if (camera != null) camera.clearFlags = CameraClearFlags.Skybox;
            }

            RenderSettings.sun = light;
            ConfigurePostProcessing(camera);
        }

        /// <summary>URP only: post-processing on the battle camera and the global volume (bloom, ACES, grading, vignette).</summary>
        private static void ConfigurePostProcessing(Camera camera)
        {
            if (camera == null || GraphicsSettings.currentRenderPipeline == null) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            var profile = Resources.Load<VolumeProfile>("Prototype/Rendering/BattlePostProcess");
            if (profile == null || Object.FindAnyObjectByType<Volume>() != null) return;
            var volume = new GameObject("Battle Post Process").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            // Runtime copy so the authored asset stays untouched; a soft far blur gives the miniature-table look.
            VolumeProfile runtimeProfile = Object.Instantiate(profile);
            if (!runtimeProfile.TryGet(out DepthOfField depthOfField)) depthOfField = runtimeProfile.Add<DepthOfField>(true);
            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(21f);
            depthOfField.gaussianEnd.Override(48f);
            depthOfField.gaussianMaxRadius.Override(1.2f);
            depthOfField.highQualitySampling.Override(true);
            volume.profile = runtimeProfile;
        }

        /// <summary>Stone material for one cell column; its colour is the tint the highlights replace.</summary>
        public static Material CreateCellMaterial(bool walkable)
        {
            BattleTabletop.CardboardTextures(out Texture2D albedo, out Texture2D normal);
            var material = new Material(LitShader) { name = walkable ? "Cell Cardboard" : "Cell Cardboard Dark" };
            material.mainTexture = albedo;
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 0.6f);
            material.EnableKeyword("_NORMALMAP");
            SetSmoothness(material, 0.08f);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        /// <summary>Wide dirt plane under the field so the columns read as a raised rock plateau.</summary>
        public static GameObject CreateGround(Transform parent, float y)
        {
            EnsureTextures();
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            Object.Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0f, y, 0f);
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            var material = new Material(LitShader) { name = "Ground Dirt" };
            material.mainTexture = groundAlbedo;
            material.mainTextureScale = new Vector2(6f, 6f);
            material.SetTexture("_BumpMap", groundNormal);
            material.EnableKeyword("_NORMALMAP");
            material.color = new Color(0.36f, 0.33f, 0.27f);
            SetSmoothness(material, 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = material;
            return ground;
        }

        /// <summary>Irregular rock: a sphere with displaced vertices, flattened at the base.</summary>
        public static Mesh CreateRockMesh(int seed, float radius, float height)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh source = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temp);
            var mesh = Object.Instantiate(source);
            mesh.name = "Procedural Rock";
            Vector3[] vertices = mesh.vertices;
            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 100f;
            float offsetY = (float)random.NextDouble() * 100f;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 v = vertices[index];
                Vector3 direction = v.normalized;
                float noise = Mathf.PerlinNoise(direction.x * 2.1f + offsetX, direction.z * 2.1f + direction.y * 1.3f + offsetY);
                float facet = Mathf.Round(noise * 4f) / 4f;
                float scale = 0.75f + Mathf.Lerp(noise, facet, 0.6f) * 0.55f;
                v = direction * radius * scale;
                v.y = Mathf.Max(v.y * height / radius, -0.05f) + 0.05f;
                vertices[index] = v;
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Material RockMaterial
        {
            get
            {
                if (rockMaterial != null) return rockMaterial;
                EnsureTextures();
                rockMaterial = new Material(LitShader) { name = "Rock" };
                rockMaterial.mainTexture = stoneAlbedo;
                rockMaterial.mainTextureScale = new Vector2(2f, 2f);
                rockMaterial.SetTexture("_BumpMap", stoneNormal);
                rockMaterial.EnableKeyword("_NORMALMAP");
                rockMaterial.color = new Color(0.46f, 0.43f, 0.4f);
                SetSmoothness(rockMaterial, 0.18f);
                return rockMaterial;
            }
        }

        public static GameObject CreateRock(Transform parent, Vector3 position, int seed, float radius, float height)
        {
            var rock = new GameObject("Rock");
            rock.transform.SetParent(parent, false);
            rock.transform.position = position;
            rock.transform.rotation = Quaternion.Euler(0f, seed * 47 % 360, 0f);
            rock.AddComponent<MeshFilter>().sharedMesh = CreateRockMesh(seed, radius, height);
            rock.AddComponent<MeshRenderer>().sharedMaterial = RockMaterial;
            return rock;
        }

        private static void EnsureTextures()
        {
            if (stoneAlbedo != null) return;
            stoneAlbedo = BuildStone(out stoneNormal, 1337, 4f, true, 0.7f);
            groundAlbedo = BuildStone(out groundNormal, 4242, 4f, false, 0.45f);
        }

        /// <summary>Multi-octave value noise with cracks, plus a normal map derived from it.</summary>
        private static Texture2D BuildStone(out Texture2D normal, int seed, float frequency, bool cracks, float contrast)
        {
            var heights = new float[TextureSize * TextureSize];
            var random = new System.Random(seed);
            float ox = (float)random.NextDouble() * 1000f;
            float oy = (float)random.NextDouble() * 1000f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = x / (float)TextureSize;
                    float v = y / (float)TextureSize;
                    float value = 0f;
                    float amplitude = 0.55f;
                    float f = frequency;
                    for (int octave = 0; octave < 5; octave++)
                    {
                        value += amplitude * TileableNoise(u, v, f, ox + octave * 31f, oy + octave * 17f);
                        amplitude *= 0.5f;
                        f *= 2f;
                    }

                    if (cracks)
                    {
                        float ridge = Mathf.Abs(TileableNoise(u, v, frequency * 0.6f, ox + 91f, oy + 53f) - 0.5f);
                        value -= Mathf.Clamp01(0.035f - ridge) * 5f;
                    }

                    heights[y * TextureSize + x] = value;
                }
            }

            var albedo = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true) { name = "Procedural Stone" };
            normal = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true, true) { name = "Procedural Stone Normal" };
            var albedoPixels = new Color32[heights.Length];
            var normalPixels = new Color32[heights.Length];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float h = heights[y * TextureSize + x];
                    float shade = Mathf.Clamp01(0.62f + (h - 0.5f) * contrast);
                    byte grey = (byte)(shade * 255f);
                    albedoPixels[y * TextureSize + x] = new Color32(grey, grey, grey, 255);
                    float left = heights[y * TextureSize + (x + TextureSize - 1) % TextureSize];
                    float right = heights[y * TextureSize + (x + 1) % TextureSize];
                    float down = heights[(y + TextureSize - 1) % TextureSize * TextureSize + x];
                    float up = heights[(y + 1) % TextureSize * TextureSize + x];
                    Vector3 n = new Vector3((left - right) * 3f, (down - up) * 3f, 1f).normalized;
                    normalPixels[y * TextureSize + x] = new Color32(
                        (byte)((n.x * 0.5f + 0.5f) * 255f),
                        (byte)((n.y * 0.5f + 0.5f) * 255f),
                        (byte)((n.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }

            albedo.SetPixels32(albedoPixels);
            albedo.wrapMode = TextureWrapMode.Repeat;
            albedo.filterMode = FilterMode.Trilinear;
            albedo.anisoLevel = 4;
            albedo.Apply(true, true);
            normal.SetPixels32(normalPixels);
            normal.wrapMode = TextureWrapMode.Repeat;
            normal.filterMode = FilterMode.Trilinear;
            normal.Apply(true, true);
            return albedo;
        }

        /// <summary>Perlin noise that wraps at the texture edges so tiles repeat seamlessly.</summary>
        private static float TileableNoise(float u, float v, float frequency, float ox, float oy)
        {
            float a = Mathf.PerlinNoise(u * frequency + ox, v * frequency + oy);
            float b = Mathf.PerlinNoise((u - 1f) * frequency + ox, v * frequency + oy);
            float c = Mathf.PerlinNoise(u * frequency + ox, (v - 1f) * frequency + oy);
            float d = Mathf.PerlinNoise((u - 1f) * frequency + ox, (v - 1f) * frequency + oy);
            float top = Mathf.Lerp(a, b, u);
            float bottom = Mathf.Lerp(c, d, u);
            return Mathf.Lerp(top, bottom, v);
        }
    }
}
