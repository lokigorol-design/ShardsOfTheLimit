using System;
using System.Collections.Generic;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>
    /// Pure cardinal push and pull math. A displacement stopped early by a unit, obstacle or impassable terrain
    /// reports the blocking cell as a collision (16.10.8); the map edge stops it without a collision.
    /// </summary>
    public static class ForcedMovementCalculator
    {
        /// <summary>Collision damage to each side: 0.2 × ATK of the unit that caused the displacement.</summary>
        public const double CollisionAttackMultiplier = 0.2d;

        /// <summary>Cardinal, dominant-axis pull; each step uses the same terrain rules as push.</summary>
        public static PushResult Pull(TacticalGrid grid, CombatUnit target, GridCoordinate sourcePosition,
            GridCoordinate targetPosition, int maximumDistance, Func<GridCoordinate, bool> isOccupied)
        {
            var path = new List<GridCoordinate> { targetPosition };
            var current = targetPosition;
            GridCoordinate? collision = null;
            for (int step = 0; step < maximumDistance && current.ManhattanDistance(sourcePosition) > 1; step++)
            {
                var direction = CloudAreaEffect.DirectionFrom(current, sourcePosition);
                var next = new GridCoordinate(current.X + direction.X, current.Y + direction.Y);
                if (next == sourcePosition || !grid.Contains(next)) break;
                if ((isOccupied?.Invoke(next) ?? false) || !grid.TryGetMovementCost(current, next, out _))
                {
                    collision = next;
                    break;
                }
                path.Add(next); current = next;
            }
            return new PushResult(target, path, collision);
        }

        /// <summary>
        /// Storm Magnet: the first resonant on a straight line from <paramref name="point"/> within
        /// <paramref name="reach"/> cells (nearest first; ties in the order +X, −X, +Y, −Y). Null when nobody is in reach.
        /// </summary>
        public static CombatUnit FindFirstOnLines(
            TacticalGrid grid,
            GridCoordinate point,
            int reach,
            IEnumerable<CombatUnit> units)
        {
            var candidates = new List<CombatUnit>();
            foreach (CombatUnit unit in units) if (!unit.IsDefeated && unit.Cell != point) candidates.Add(unit);
            var directions = new[] { new GridCoordinate(1, 0), new GridCoordinate(-1, 0), new GridCoordinate(0, 1), new GridCoordinate(0, -1) };
            for (int step = 1; step <= reach; step++)
            {
                foreach (GridCoordinate direction in directions)
                {
                    var cell = new GridCoordinate(point.X + direction.X * step, point.Y + direction.Y * step);
                    // An obstacle between the point and the target cuts that line.
                    bool blocked = false;
                    for (int inner = 1; inner < step; inner++)
                    {
                        var between = new GridCoordinate(point.X + direction.X * inner, point.Y + direction.Y * inner);
                        if (!grid.Contains(between) || !grid.GetCell(between).IsWalkable ||
                            candidates.Exists(unit => unit.Cell == between)) blocked = true;
                    }

                    if (blocked) continue;
                    CombatUnit hit = candidates.Find(unit => unit.Cell == cell);
                    if (hit != null) return hit;
                }
            }

            return null;
        }

        /// <summary>Pull straight towards a surface point; on a floor point the target ends on the point itself.</summary>
        public static PushResult PullToPoint(TacticalGrid grid, CombatUnit target, GridCoordinate point,
            GridCoordinate targetPosition, Func<GridCoordinate, bool> isOccupied)
        {
            var path = new List<GridCoordinate> { targetPosition };
            var current = targetPosition;
            GridCoordinate? collision = null;
            while (current != point)
            {
                var direction = CloudAreaEffect.DirectionFrom(current, point);
                var next = new GridCoordinate(current.X + direction.X, current.Y + direction.Y);
                if ((isOccupied?.Invoke(next) ?? false) || !grid.TryGetMovementCost(current, next, out _))
                {
                    collision = next;
                    break;
                }

                path.Add(next);
                current = next;
            }

            return new PushResult(target, path, collision);
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
            GridCoordinate? collision = null;
            for (int step = 0; step < maximumDistance; step++)
            {
                var next = new GridCoordinate(current.X + direction.X, current.Y + direction.Y);
                if (!grid.Contains(next)) break;
                if ((isOccupied?.Invoke(next) ?? false) ||
                    !grid.TryGetMovementCost(current, next, out _))
                {
                    collision = next;
                    break;
                }

                path.Add(next);
                current = next;
            }

            return new PushResult(target, path, collision);
        }
    }
}
