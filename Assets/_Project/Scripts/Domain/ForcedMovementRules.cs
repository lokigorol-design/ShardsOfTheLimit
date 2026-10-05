using System;
using System.Collections.Generic;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>Pure cardinal push math; blocked steps stop movement without collision damage.</summary>
    public static class ForcedMovementCalculator
    {
        /// <summary>Cardinal, dominant-axis pull; each step uses the same terrain rules as push.</summary>
        public static PushResult Pull(TacticalGrid grid, CombatUnit target, GridCoordinate sourcePosition,
            GridCoordinate targetPosition, int maximumDistance, Func<GridCoordinate, bool> isOccupied)
        {
            var path = new List<GridCoordinate> { targetPosition };
            var current = targetPosition;
            for (int step = 0; step < maximumDistance && current.ManhattanDistance(sourcePosition) > 1; step++)
            {
                var direction = CloudAreaEffect.DirectionFrom(current, sourcePosition);
                var next = new GridCoordinate(current.X + direction.X, current.Y + direction.Y);
                if (next == sourcePosition || (isOccupied?.Invoke(next) ?? false) ||
                    !grid.TryGetMovementCost(current, next, out _)) break;
                path.Add(next); current = next;
            }
            return new PushResult(target, path);
        }

        public static PushResult Push(
            TacticalGrid grid,
            CombatUnit target,
            GridCoordinate sourcePosition,
            GridCoordinate targetPosition,
            int maximumDistance,
            Func<GridCoordinate, bool> isOccupied)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (maximumDistance < 0) throw new ArgumentOutOfRangeException(nameof(maximumDistance));

            var path = new List<GridCoordinate> { targetPosition };
            int deltaX = targetPosition.X - sourcePosition.X;
            int deltaY = targetPosition.Y - sourcePosition.Y;
            if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
            {
                return new PushResult(target, path);
            }

            var direction = new GridCoordinate(deltaX, deltaY);
            GridCoordinate current = targetPosition;
            for (int step = 0; step < maximumDistance; step++)
            {
                var next = new GridCoordinate(current.X + direction.X, current.Y + direction.Y);
                if ((isOccupied?.Invoke(next) ?? false) ||
                    !grid.TryGetMovementCost(current, next, out _))
                {
                    break;
                }

                path.Add(next);
                current = next;
            }

            return new PushResult(target, path);
        }
    }
}
