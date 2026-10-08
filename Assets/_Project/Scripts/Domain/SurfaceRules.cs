namespace ShardsOfTheLimit.Domain
{
    /// <summary>
    /// Ritual Surfaces (Primal Idol, 10.11): Surface abilities may target a wall instead of a floor point.
    /// A wall is the face of a cell (a column at least 2 units above its neighbour, or an obstacle) that looks towards
    /// the caster. The effect comes out of the wall perpendicularly, into the free cell in front of it.
    /// </summary>
    public static class SurfaceRules
    {
        public const int MinimumWallHeight = 2;

        /// <param name="wallCell">The cell whose face is the wall.</param>
        /// <param name="front">The cell in front of the wall, where the effect emerges.</param>
        /// <param name="outward">Direction pointing out of the wall, towards the caster.</param>
        public static bool TryGetWall(TacticalGrid grid, GridCoordinate caster, GridCoordinate wallCell,
            out GridCoordinate front, out GridCoordinate outward)
        {
            outward = default;
            front = default;
            if (grid == null || !grid.Contains(wallCell) || wallCell == caster) return false;
            outward = CloudAreaEffect.DirectionFrom(wallCell, caster);
            front = new GridCoordinate(wallCell.X + outward.X, wallCell.Y + outward.Y);
            if (!grid.Contains(front)) return false;
            GridCell wall = grid.GetCell(wallCell);
            GridCell ground = grid.GetCell(front);
            if (!ground.IsWalkable) return false;
            return !wall.IsWalkable || wall.Height - ground.Height >= MinimumWallHeight;
        }

        /// <summary>Height of the wall's top, used for an Earth Pillar ledge pushed out of it.</summary>
        public static int WallTop(TacticalGrid grid, GridCoordinate wallCell)
        {
            GridCell wall = grid.GetCell(wallCell);
            return wall.IsWalkable ? wall.Height : wall.Height + LineOfFire.ObstacleHeight;
        }
    }
}
