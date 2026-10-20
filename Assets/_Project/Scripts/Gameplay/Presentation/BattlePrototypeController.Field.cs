using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ShardsOfTheLimit.Definitions;
using ShardsOfTheLimit.Domain;
using ShardsOfTheLimit.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShardsOfTheLimit.Presentation
{
    /// <summary>Battlefield geometry: cell columns, rocks, heights and world positions.</summary>
    public sealed partial class BattlePrototypeController
    {        private void BuildGrid()
        {
            var logicalCells = new List<GridCell>();
            for (int x = 0; x < GridWidth; x++)
            {
                for (int y = 0; y < GridHeight; y++)
                {
                    var coordinate = new GridCoordinate(x, y);
                    int height = GetFixedHeight(coordinate);
                    bool walkable = coordinate != new GridCoordinate(2, 4) &&
                                    coordinate != new GridCoordinate(3, 1) &&
                                    coordinate != new GridCoordinate(5, 5) &&
                                    coordinate != new GridCoordinate(6, 3) &&
                                    coordinate != new GridCoordinate(4, 7);
                    logicalCells.Add(new GridCell(coordinate, height, walkable));
                }
            }

            grid = new TacticalGrid(logicalCells);
            foreach (GridCell cell in grid.Cells)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Cell {cell.Coordinate} H{cell.Height}{(cell.IsWalkable ? string.Empty : " Blocked")}";
                block.transform.SetParent(transform, false);
                GetCellColumnGeometry(cell.Height, out float columnCenterY, out float columnHeight);
                block.transform.position = new Vector3(
                    WorldX(cell.Coordinate.X),
                    columnCenterY,
                    WorldZ(cell.Coordinate.Y));
                block.transform.localScale = new Vector3(0.98f, columnHeight, 0.98f);
                // Printed cardboard tile; every face shows the inked cut edge, so no texture offset per cell.
                block.GetComponent<Renderer>().material = BattleEnvironment.CreateCellMaterial(cell.IsWalkable);
                Color baseColor = GetCellBaseColor(cell);
                CellView view = block.AddComponent<CellView>();
                view.Initialize(cell.Coordinate, baseColor);
                cellViews.Add(cell.Coordinate, view);
                if (!cell.IsWalkable) CreateObstacleRocks(cell);
            }

            // The field is a board game laid out on a kitchen table (see BattleTabletop).
            BattleTabletop.Build(transform, -0.62f, GridWidth * CellSpacing * 0.5f);
        }

        /// <summary>Obstacles read as rock formations: one large rock with a couple of smaller ones around it.</summary>
        private void CreateObstacleRocks(GridCell cell)
        {
            int seed = cell.Coordinate.X * 73 + cell.Coordinate.Y * 131;
            var obstacle = new GameObject($"Obstacle Rocks {cell.Coordinate}");
            obstacle.transform.SetParent(transform, false);
            obstacle.transform.position = new Vector3(
                WorldX(cell.Coordinate.X),
                cell.Height * HeightStep + BattleTabletop.BaseHeight + 0.02f,
                WorldZ(cell.Coordinate.Y));
            obstacle.transform.rotation = Quaternion.Euler(0f, seed % 360, 0f);
            // Terrain piece: the rocks are glued onto a wooden token like a miniature.
            BattleTabletop.CreateToken(obstacle.transform,
                new Vector3(obstacle.transform.position.x, cell.Height * HeightStep + BattleTabletop.BaseHeight, obstacle.transform.position.z), 0.47f);
            obstacle.AddComponent<MeshFilter>().sharedMesh = BattleEnvironment.CreateRockMesh(seed, 0.4f, 0.75f);
            obstacle.AddComponent<MeshRenderer>().sharedMaterial = BattleEnvironment.RockMaterial;
            for (int index = 0; index < 2; index++)
            {
                float angle = (seed + index * 140) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.32f;
                BattleEnvironment.CreateRock(obstacle.transform, obstacle.transform.position + offset,
                    seed + index + 1, 0.16f, 0.2f);
            }

            obstacle.AddComponent<ObstacleView>().Initialize(cell.Coordinate);
        }

        private Vector3 UnitWorldPosition(GridCoordinate coordinate)
        {
            return new Vector3(
                WorldX(coordinate.X),
                grid.GetCell(coordinate).Height * HeightStep + 0.62f,
                WorldZ(coordinate.Y));
        }

        private Vector3 EffectWorldPosition(GridCoordinate coordinate)
        {
            return new Vector3(
                WorldX(coordinate.X),
                grid.GetCell(coordinate).Height * HeightStep + 0.12f,
                WorldZ(coordinate.Y));
        }

        private static float WorldX(int x) => (x - (GridWidth - 1) * 0.5f) * CellSpacing;
        private static float WorldZ(int y) => (y - (GridHeight - 1) * 0.5f) * CellSpacing;

        private void RefreshCellVisual(GridCoordinate coordinate)
        {
            if (!cellViews.TryGetValue(coordinate, out CellView view)) return;
            GridCell cell = grid.GetCell(coordinate);
            GetCellColumnGeometry(cell.Height, out float columnCenterY, out float columnHeight);
            view.transform.position = new Vector3(
                WorldX(coordinate.X),
                columnCenterY,
                WorldZ(coordinate.Y));
            view.transform.localScale = new Vector3(0.98f, columnHeight, 0.98f);
            view.SetBaseColor(GetCellBaseColor(cell));
        }

        private static Color GetCellBaseColor(GridCell cell)
        {
            float normalizedHeight = (cell.Height - MinimumTerrainHeight) /
                                     (float)(MaximumTerrainHeight - MinimumTerrainHeight);
            // Natural palette: mossy low ground, warm sandstone on the heights, dark basalt under obstacles.
            Color tint = Color.Lerp(new Color(0.5f, 0.56f, 0.44f), new Color(0.78f, 0.68f, 0.52f), normalizedHeight);
            float checker = (cell.Coordinate.X + cell.Coordinate.Y) % 2 == 0 ? 1f : 0.93f;
            return cell.IsWalkable ? tint * checker : new Color(0.3f, 0.29f, 0.3f);
        }

        private static void GetCellColumnGeometry(int height, out float centerY, out float columnHeight)
        {
            float topY = height * HeightStep;
            float bottomY = MinimumTerrainHeight * HeightStep - 0.22f;
            columnHeight = Mathf.Max(0.22f, topY - bottomY);
            centerY = (topY + bottomY) * 0.5f;
        }

        private static int GetFixedHeight(GridCoordinate coordinate)
        {
            if (coordinate == new GridCoordinate(1, 4)) return 3;
            if (coordinate == new GridCoordinate(2, 2)) return 2;
            if (coordinate == new GridCoordinate(3, 2) || coordinate == new GridCoordinate(3, 3)) return 1;
            if (coordinate == new GridCoordinate(4, 4) || coordinate == new GridCoordinate(5, 4)) return 2;
            if (coordinate == new GridCoordinate(7, 7) || coordinate == new GridCoordinate(8, 7)) return 1;
            if (coordinate == new GridCoordinate(7, 5) || coordinate == new GridCoordinate(8, 5)) return -1;
            return 0;
        }
    }
}
