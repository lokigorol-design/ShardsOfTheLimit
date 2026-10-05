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
