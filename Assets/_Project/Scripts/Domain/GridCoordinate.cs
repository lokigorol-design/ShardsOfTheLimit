using System;
using System.Collections.Generic;

namespace ShardsOfTheLimit.Domain
{
    /// <summary>Stable logical coordinate; world transforms never define grid identity.</summary>
    public readonly struct GridCoordinate : IEquatable<GridCoordinate>
    {
        public GridCoordinate(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public int ManhattanDistance(GridCoordinate other)
        {
            return Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
        }

        public IEnumerable<GridCoordinate> FourWayNeighbors()
        {
            yield return new GridCoordinate(X + 1, Y);
            yield return new GridCoordinate(X - 1, Y);
            yield return new GridCoordinate(X, Y + 1);
            yield return new GridCoordinate(X, Y - 1);
        }

        public bool Equals(GridCoordinate other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCoordinate other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => $"({X}, {Y})";
        public static bool operator ==(GridCoordinate left, GridCoordinate right) => left.Equals(right);
        public static bool operator !=(GridCoordinate left, GridCoordinate right) => !left.Equals(right);
    }
}
