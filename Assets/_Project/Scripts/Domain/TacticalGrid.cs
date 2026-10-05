using System;
using System.Collections.Generic;
using System.Linq;

namespace ShardsOfTheLimit.Domain
{
    public sealed class GridCell
    {
        public GridCell(GridCoordinate coordinate, int height, bool isWalkable = true)
        {
            Coordinate = coordinate;
            Height = height;
            IsWalkable = isWalkable;
        }

        public GridCoordinate Coordinate { get; }
        public int Height { get; }
        public bool IsWalkable { get; }
    }

    public sealed class TerrainChangeResult
    {
        public TerrainChangeResult(
            GridCoordinate coordinate,
            int previousHeight,
            int newHeight,
            bool isWalkable)
        {
            Coordinate = coordinate;
            PreviousHeight = previousHeight;
            NewHeight = newHeight;
            IsWalkable = isWalkable;
        }

        public GridCoordinate Coordinate { get; }
        public int PreviousHeight { get; }
        public int NewHeight { get; }
        public bool IsWalkable { get; }
        public bool Changed => PreviousHeight != NewHeight;
    }

    public static class TerrainChangeCalculator
    {
        public static TerrainChangeResult ChangeHeight(
            GridCell cell,
            int heightDelta,
            int minimumHeight,
            int maximumHeight)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            if (minimumHeight > maximumHeight)
            {
                throw new ArgumentException("Minimum terrain height exceeds maximum terrain height.");
            }

            int newHeight = Math.Max(minimumHeight, Math.Min(maximumHeight, cell.Height + heightDelta));
            return new TerrainChangeResult(cell.Coordinate, cell.Height, newHeight, cell.IsWalkable);
        }
    }

    /// <summary>Immutable result used by both movement preview and execution.</summary>
    public sealed class MovementMap
    {
        private readonly IReadOnlyDictionary<GridCoordinate, int> costs;
        private readonly IReadOnlyDictionary<GridCoordinate, GridCoordinate> predecessors;

        internal MovementMap(
            GridCoordinate start,
            IDictionary<GridCoordinate, int> costs,
            IDictionary<GridCoordinate, GridCoordinate> predecessors)
        {
            Start = start;
            this.costs = new Dictionary<GridCoordinate, int>(costs);
            this.predecessors = new Dictionary<GridCoordinate, GridCoordinate>(predecessors);
            var reachable = new Dictionary<GridCoordinate, int>(costs);
            reachable.Remove(start);
            ReachableCosts = reachable;
        }

        public GridCoordinate Start { get; }
        public IReadOnlyDictionary<GridCoordinate, int> ReachableCosts { get; }

        public IReadOnlyList<GridCoordinate> GetPath(GridCoordinate destination)
        {
            if (!costs.ContainsKey(destination)) return Array.Empty<GridCoordinate>();

            var path = new List<GridCoordinate> { destination };
            while (path[path.Count - 1] != Start)
            {
                if (!predecessors.TryGetValue(path[path.Count - 1], out GridCoordinate previous))
                {
                    return Array.Empty<GridCoordinate>();
                }

                path.Add(previous);
            }

            path.Reverse();
            return path;
        }
    }

    /// <summary>Validated square grid with four-way movement and height-aware movement costs.</summary>
    public sealed class TacticalGrid
    {
        public const int MaximumClimbHeightDifference = 2;

        private readonly Dictionary<GridCoordinate, GridCell> cells;

        public TacticalGrid(IEnumerable<GridCell> cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            this.cells = new Dictionary<GridCoordinate, GridCell>();
            foreach (GridCell cell in cells)
            {
                if (cell == null)
                {
                    throw new ArgumentException("Grid cannot contain a null cell.", nameof(cells));
                }

                if (!this.cells.TryAdd(cell.Coordinate, cell))
                {
                    throw new ArgumentException($"Duplicate grid coordinate {cell.Coordinate}.", nameof(cells));
                }
            }
        }

        public IEnumerable<GridCell> Cells => cells.Values;
        public bool Contains(GridCoordinate coordinate) => cells.ContainsKey(coordinate);
        public GridCell GetCell(GridCoordinate coordinate) => cells[coordinate];

        public void ApplyTerrainChange(TerrainChangeResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!cells.TryGetValue(result.Coordinate, out GridCell current))
            {
                throw new ArgumentException("Terrain change points outside the grid.", nameof(result));
            }

            if (current.Height != result.PreviousHeight || current.IsWalkable != result.IsWalkable)
            {
                throw new InvalidOperationException("Terrain changed after the result was calculated.");
            }

            cells[result.Coordinate] = new GridCell(
                result.Coordinate,
                result.NewHeight,
                result.IsWalkable);
        }

        public IEnumerable<GridCoordinate> GetNeighbors(GridCoordinate coordinate)
        {
            return coordinate.FourWayNeighbors().Where(cells.ContainsKey);
        }

        /// <summary>
        /// A step costs one movement point plus the positive height difference. Climbing three or
        /// more height levels in one step is illegal; descending has no surcharge.
        /// </summary>
        public bool TryGetMovementCost(
            GridCoordinate from,
            GridCoordinate to,
            out int movementCost)
        {
            movementCost = 0;
            if (!cells.TryGetValue(from, out GridCell fromCell) ||
                !cells.TryGetValue(to, out GridCell toCell) ||
                !toCell.IsWalkable ||
                !from.FourWayNeighbors().Contains(to))
            {
                return false;
            }

            int climb = toCell.Height - fromCell.Height;
            if (climb > MaximumClimbHeightDifference)
            {
                return false;
            }

            movementCost = 1 + Math.Max(0, climb);
            return true;
        }

        public IReadOnlyDictionary<GridCoordinate, int> GetReachable(
            GridCoordinate start,
            int movementRange,
            Func<GridCoordinate, bool> isOccupied = null)
        {
            return BuildMovementMap(start, movementRange, isOccupied).ReachableCosts;
        }

        public MovementMap BuildMovementMap(
            GridCoordinate start,
            int movementRange,
            Func<GridCoordinate, bool> isOccupied = null)
        {
            if (movementRange < 0) throw new ArgumentOutOfRangeException(nameof(movementRange));
            if (!Contains(start)) throw new ArgumentException("Start coordinate is outside the grid.", nameof(start));

            var distance = new Dictionary<GridCoordinate, int>();
            var predecessor = new Dictionary<GridCoordinate, GridCoordinate>();
            CalculateMovementCosts(start, movementRange, isOccupied, distance, predecessor);
            return new MovementMap(start, distance, predecessor);
        }

        public IReadOnlyList<GridCoordinate> FindPath(
            GridCoordinate start,
            GridCoordinate destination,
            int maximumMovementPoints,
            Func<GridCoordinate, bool> isOccupied = null)
        {
            if (maximumMovementPoints < 0) throw new ArgumentOutOfRangeException(nameof(maximumMovementPoints));
            if (!Contains(start) || !Contains(destination) || !cells[destination].IsWalkable)
            {
                return Array.Empty<GridCoordinate>();
            }

            return BuildMovementMap(start, maximumMovementPoints, isOccupied).GetPath(destination);
        }

        private void CalculateMovementCosts(
            GridCoordinate start,
            int movementBudget,
            Func<GridCoordinate, bool> isOccupied,
            IDictionary<GridCoordinate, int> distance,
            IDictionary<GridCoordinate, GridCoordinate> predecessor)
        {
            distance[start] = 0;
            var frontier = new HashSet<GridCoordinate> { start };

            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier
                    .OrderBy(coordinate => distance[coordinate])
                    .ThenBy(coordinate => coordinate.X)
                    .ThenBy(coordinate => coordinate.Y)
                    .First();
                frontier.Remove(current);

                foreach (GridCoordinate next in GetNeighbors(current))
                {
                    if ((isOccupied?.Invoke(next) ?? false) ||
                        !TryGetMovementCost(current, next, out int stepCost))
                    {
                        continue;
                    }

                    int candidateCost = distance[current] + stepCost;
                    if (candidateCost > movementBudget ||
                        (distance.TryGetValue(next, out int knownCost) && candidateCost >= knownCost))
                    {
                        continue;
                    }

                    distance[next] = candidateCost;
                    predecessor[next] = current;
                    frontier.Add(next);
                }
            }
        }
    }
}
