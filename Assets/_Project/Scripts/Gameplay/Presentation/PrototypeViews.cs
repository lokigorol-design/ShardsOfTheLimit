using ShardsOfTheLimit.Domain;
using UnityEngine;

namespace ShardsOfTheLimit.Presentation
{
    public sealed class CellView : MonoBehaviour
    {
        private Renderer cachedRenderer;

        public GridCoordinate Coordinate { get; private set; }
        public Color BaseColor { get; private set; }

        public void Initialize(GridCoordinate coordinate, Color baseColor)
        {
            Coordinate = coordinate;
            BaseColor = baseColor;
            cachedRenderer = GetComponent<Renderer>();
            SetColor(baseColor);
        }

        public void SetColor(Color color)
        {
            if (cachedRenderer == null)
            {
                cachedRenderer = GetComponent<Renderer>();
            }

            cachedRenderer.material.color = color;
        }

        public void SetBaseColor(Color color)
        {
            BaseColor = color;
            SetColor(color);
        }
    }

    public sealed class CombatUnitView : MonoBehaviour
    {
        private Renderer[] cachedRenderers;
        private Color[] baseColors;

        public CombatUnit Unit { get; private set; }
        public Color BaseColor { get; private set; }

        public void Initialize(CombatUnit unit, Color color)
        {
            Unit = unit;
            BaseColor = color;
            cachedRenderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[cachedRenderers.Length];
            for (int index = 0; index < cachedRenderers.Length; index++)
            {
                baseColors[index] = cachedRenderers[index].material.color;
            }
        }

        /// <summary>Facing shown while the player is still choosing it; null shows the unit's real facing.</summary>
        public GridCoordinate? PreviewFacing { get; set; }

        private void Update()
        {
            if (Unit == null) return;
            GridCoordinate facing = PreviewFacing ?? Unit.Facing;
            Quaternion target = Quaternion.LookRotation(new Vector3(facing.X, 0f, facing.Y));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 540f * Time.deltaTime);
        }

        public void SetHitFlash(float strength)
        {
            float clamped = Mathf.Clamp01(strength);
            for (int index = 0; index < cachedRenderers.Length; index++)
            {
                cachedRenderers[index].material.color = Color.Lerp(baseColors[index], Color.white, clamped);
            }
        }

        public void RestoreColors()
        {
            for (int index = 0; index < cachedRenderers.Length; index++)
            {
                cachedRenderers[index].material.color = baseColors[index];
            }
        }
    }

    public sealed class ObstacleView : MonoBehaviour
    {
        public GridCoordinate Coordinate { get; private set; }

        public void Initialize(GridCoordinate coordinate)
        {
            Coordinate = coordinate;
        }
    }
}
