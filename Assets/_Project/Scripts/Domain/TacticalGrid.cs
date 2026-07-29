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

    /// <summary>Validated square grid with four-way movement and unit step cost.</summary>
    public sealed class TacticalGrid
    {
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

        public IEnumerable<GridCoordinate> GetNeighbors(GridCoordinate coordinate)
        {
            return coordinate.FourWayNeighbors().Where(cells.ContainsKey);
        }

        public IReadOnlyDictionary<GridCoordinate, int> GetReachable(
            GridCoordinate start,
            int movementRange,
            Func<GridCoordinate, bool> isOccupied = null)
        {
            var distance = new Dictionary<GridCoordinate, int> { [start] = 0 };
            var frontier = new Queue<GridCoordinate>();
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier.Dequeue();
                foreach (GridCoordinate next in GetNeighbors(current))
                {
                    int nextDistance = distance[current] + 1;
                    if (nextDistance > movementRange || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    GridCell cell = cells[next];
                    if (!cell.IsWalkable || (isOccupied?.Invoke(next) ?? false))
                    {
                        continue;
                    }

                    distance.Add(next, nextDistance);
                    frontier.Enqueue(next);
                }
            }

            distance.Remove(start);
            return distance;
        }

        public IReadOnlyList<GridCoordinate> FindPath(
            GridCoordinate start,
            GridCoordinate destination,
            int maximumSteps,
            Func<GridCoordinate, bool> isOccupied = null)
        {
            if (!Contains(start) || !Contains(destination) || !cells[destination].IsWalkable)
            {
                return Array.Empty<GridCoordinate>();
            }

            var predecessor = new Dictionary<GridCoordinate, GridCoordinate>();
            var distance = new Dictionary<GridCoordinate, int> { [start] = 0 };
            var frontier = new Queue<GridCoordinate>();
            frontier.Enqueue(start);

            while (frontier.Count > 0)
            {
                GridCoordinate current = frontier.Dequeue();
                if (current == destination)
                {
                    break;
                }

                foreach (GridCoordinate next in GetNeighbors(current))
                {
                    int nextDistance = distance[current] + 1;
                    if (nextDistance > maximumSteps || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    if (!cells[next].IsWalkable || (isOccupied?.Invoke(next) ?? false))
                    {
                        continue;
                    }

                    distance.Add(next, nextDistance);
                    predecessor.Add(next, current);
                    frontier.Enqueue(next);
                }
            }

            if (!distance.ContainsKey(destination))
            {
                return Array.Empty<GridCoordinate>();
            }

            var path = new List<GridCoordinate> { destination };
            while (path[path.Count - 1] != start)
            {
                path.Add(predecessor[path[path.Count - 1]]);
            }

            path.Reverse();
            return path;
        }
    }
}
