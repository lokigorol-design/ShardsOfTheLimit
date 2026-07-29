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
    }

    public sealed class CombatUnitView : MonoBehaviour
    {
        private Renderer cachedRenderer;

        public CombatUnit Unit { get; private set; }
        public Color BaseColor { get; private set; }

        public void Initialize(CombatUnit unit, Color color)
        {
            Unit = unit;
            BaseColor = color;
            cachedRenderer = GetComponent<Renderer>();
            cachedRenderer.material.color = color;
        }

        public void SetColor(Color color) => cachedRenderer.material.color = color;
    }
}
