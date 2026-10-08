using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    public sealed partial class BattlePrototypeController
    {
        [SerializeField] private bool blightgulletShowcase;
        [SerializeField] private ControlType blightgulletControl = ControlType.Player;
        private AreaEffectWorld areaEffects;
        private GameObject cloudRoot;
        private readonly Dictionary<string, Material> cloudMaterials = new Dictionary<string, Material>();
        private readonly Dictionary<string, CloudDefinition> cloudDefinitions = new Dictionary<string, CloudDefinition>();
        public AreaEffectWorld AreaEffects => areaEffects;

        private void InitializeAreaEffects()
        {
            areaEffects = new AreaEffectWorld(grid, units);
            foreach (var pair in definitionsByUnit)
                areaEffects.Register(pair.Key, pair.Value.PassiveModifiers != null
                    ? pair.Value.PassiveModifiers.Snapshot() : new PassiveModifiers());
            foreach (var definition in Resources.LoadAll<CloudDefinition>("Prototype/Clouds"))
                cloudDefinitions[definition.stableId] = definition;
        }

        private void RefreshCloudViews()
        {
            if (cloudRoot != null) Destroy(cloudRoot);
            cloudRoot = new GameObject("Area Effects (non-blocking)");
            cloudRoot.transform.SetParent(transform, false);
            foreach (var cloud in areaEffects.Clouds)
            {
                Color color = cloudDefinitions.TryGetValue(cloud.Rules.Id, out var data) ? data.color : Color.green;
                if (!cloudMaterials.TryGetValue(cloud.Rules.Id, out var material))
                {
                    // Soft ground stain under the drifting smoke; the smoke itself is particles (CloudPuffView).
                    material = new Material(Shader.Find("Shards/VfxParticle") ?? Shader.Find("Sprites/Default"));
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.renderQueue = 2990;
                    cloudMaterials.Add(cloud.Rules.Id, material);
                }
                foreach (var cell in cloud.OccupiedCells)
                {
                    var stain = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    stain.name = $"{cloud.Rules.Id} {cell} owner:{cloud.OwnerId}";
                    stain.transform.SetParent(cloudRoot.transform, false);
                    Destroy(stain.GetComponent<Collider>());
                    Vector3 ground = EffectWorldPosition(cell);
                    ground.y = Mathf.Max(ground.y - 0.1f, cloud.LowerHeight * HeightStep + 0.02f);
                    stain.transform.position = ground;
                    stain.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    stain.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
                    MeshFilter filter = stain.GetComponent<MeshFilter>();
                    filter.mesh = CreateStainMesh(new Color(color.r, color.g, color.b, 0.45f));
                    stain.GetComponent<Renderer>().sharedMaterial = material;
                    stain.AddComponent<CloudPuffView>().Initialize(color, cloud.Rules.VerticalHeight * HeightStep * 1.6f);
                }
            }
        }

        /// <summary>Unit quad with vertex colour and a soft-disc shape for the particle shader.</summary>
        private static Mesh CreateStainMesh(Color color)
        {
            var mesh = new Mesh { name = "Cloud Stain" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.uv2 = new[] { new Vector2(0.2f, 0), new Vector2(0.2f, 0), new Vector2(0.2f, 0), new Vector2(0.2f, 0) };
            mesh.colors = new[] { color, color, color, color };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void DrawCloudInformation()
        {
            if (areaEffects == null || battleCamera == null) return;
            var labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            // Unit statuses are shown as icons on the unit plates (Hud partial).

            var displayedClouds = areaEffects.Clouds.Concat(abilityPreview?.Clouds ?? System.Array.Empty<CloudAreaEffect>());
            var labelRows = new Dictionary<GridCoordinate,int>();
            foreach (var cloud in displayedClouds)
            {
                if (cloud.OccupiedCells.Count == 0) continue;
                Vector3 position = cloud.OccupiedCells.Select(EffectWorldPosition).Aggregate(Vector3.zero, (a, b) => a + b) / cloud.OccupiedCells.Count;
                position.y = (cloud.LowerHeight + cloud.Rules.VerticalHeight) * HeightStep + 0.1f;
                Vector3 screen = battleCamera.WorldToScreenPoint(position);
                var anchor = cloud.OccupiedCells.OrderBy(c => c.X).ThenBy(c => c.Y).First();
                labelRows.TryGetValue(anchor,out int row); labelRows[anchor] = row + 1;
                var rect = new Rect(screen.x - 42, Screen.height - screen.y + row * 24, 84, 23);
                Vector3 direction = new Vector3(cloud.MovementDirection.X, 0, cloud.MovementDirection.Y);
                Vector3 end = battleCamera.WorldToScreenPoint(position + direction * 0.6f);
                string arrow = Mathf.Abs(end.x - screen.x) > Mathf.Abs(end.y - screen.y)
                    ? (end.x > screen.x ? "→" : "←") : (end.y > screen.y ? "↑" : "↓");
                GUI.Box(rect, $"{cloud.Rules.Status.Id.Substring(0, System.Math.Min(4, cloud.Rules.Status.Id.Length))} {arrow}", labelStyle);
                bool hover = rect.Contains(Event.current.mousePosition);
                Ray ray = battleCamera.ScreenPointToRay(Input.mousePosition);
                var plane = new Plane(Vector3.up, new Vector3(0, position.y, 0));
                if (plane.Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    hover |= cloud.OccupiedCells.Any(c =>
                    { var p = EffectWorldPosition(c); return Mathf.Abs(p.x - hit.x) < 0.5f && Mathf.Abs(p.z - hit.z) < 0.5f; });
                }
                if (hover)
                {
                    string info = $"{cloud.Rules.Id} — {cloud.Owner.DisplayName}\n" +
                        $"{System.Math.Max(0, cloud.ExpireTick - turns.CurrentTick)} ticks left · +{cloud.StacksPerTick}/{cloud.Rules.PulseInterval}t\n" +
                        $"Direction {cloud.MovementDirection} · speed {cloud.Rules.MovementSpeed}/{cloud.Rules.MovementInterval}t\n" +
                        $"Z [{cloud.LowerHeight}, {cloud.LowerHeight + cloud.Rules.VerticalHeight})\n" +
                        "Next: " + string.Join(" ", cloud.PredictMovement(grid));
                    GUI.Box(new Rect(Mathf.Min(rect.x, Screen.width - 360), rect.y + 25, 350, 100), info, labelStyle);
                }
            }
        }

        private int ScoreAreaControl(CombatUnit source, AbilityResolution preview)
        {
            int score = 0;
            foreach (var cloud in preview.Clouds)
            {
                // Short trajectory forecast. No scene mutation and no solver.
                var simulated = new CloudAreaEffect(source, cloud.SourceAbilityId, cloud.Rules, cloud.OccupiedCells,
                    cloud.MovementDirection, cloud.LowerHeight, turns.CurrentTick, cloud.StacksPerTick);
                for (int step = 0; step < 3; step++)
                {
                    foreach (var unit in units.Where(u => !u.IsDefeated && simulated.Contains(grid, u.Cell)))
                        score += (unit.Team == source.Team ? -1 : 1) * cloud.StacksPerTick * (unit.IsImmobilized ? 14 : 8);
                    simulated = new CloudAreaEffect(source, cloud.SourceAbilityId, cloud.Rules, simulated.PredictMovement(grid),
                        cloud.MovementDirection, cloud.LowerHeight, turns.CurrentTick, cloud.StacksPerTick);
                }
                if (cloud.Rules.Status.Id == "OIL" && units.Any(u => u.Team == source.Team &&
                    definitionsByUnit[u].Abilities.Any(a => a.Effects.OfType<ApplyBurnEffectDefinition>().Any()))) score += 8;
            }
            foreach (var status in preview.Results.OfType<StatusAppliedResult>())
                if (status.Target.Team != source.Team && status.State.Rules.PreventsMovement)
                    score += areaEffects.Clouds.Any(c => c.Contains(grid, status.Target.Cell) || c.PredictMovement(grid).Contains(status.Target.Cell)) ? 35 : 4;
            foreach (var pull in preview.Results.OfType<PushResult>())
                if (pull.Target.Team != source.Team && areaEffects.Clouds.Any(c => c.Contains(grid, pull.Destination))) score += 35;
            return score;
        }

        private void OnDestroy()
        {
            foreach (var material in cloudMaterials.Values) Destroy(material);
        }
    }
}
