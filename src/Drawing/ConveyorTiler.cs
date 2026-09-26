using System;
using System.Collections.Generic;

namespace Drawing
{
    /// <summary>Which conveyor tile a cell should show.</summary>
    internal enum ConveyorTile
    {
        /// <summary>Let the mod work it out from the neighbouring cells.</summary>
        Auto = 0,
        Centre = 1,
        End = 2,
        Single = 3,
    }

    /// <summary>
    /// Works out how a conveyor cell should be drawn from the cells around it.
    ///
    /// Graveyard Keeper 2 models a belt tile as one of three WGO part states:
    ///   centre - belt runs through
    ///   end    - belt terminates here, points at its single neighbour
    ///   single - an isolated tile with no belt at all
    ///
    /// The build cursor always shows 'centre' regardless of what the player intends, which is
    /// exactly why a plan drawn with it never showed where a chain of belts begins or ends.
    /// This class derives the right tile from the plan itself, so the drawing reads as a belt
    /// network you can reason about.
    ///
    /// Pure logic on purpose - it is unit tested without Unity.
    /// </summary>
    internal static class ConveyorTiler
    {
        /// <summary>The result for one cell.</summary>
        internal readonly struct TileResult
        {
            public readonly string Variation;
            public readonly int Rotation;
            public readonly bool FromCache;

            public TileResult(string variation, int rotation, bool fromCache)
            {
                Variation = variation;
                Rotation = rotation;
                FromCache = fromCache;
            }
        }
        // BuildConsts.CELL_SIZE, mirrored so the tests need no Unity.
        public const float CellSizeX = 0.32f;
        public const float CellSizeZ = 0.30f;

        public const string Centre = "centre";
        public const string End = "end";
        public const string Single = "single";

        // Tolerance well above the coordinate rounding the game does, but far below half a
        // cell, so a 0.0001 rounding difference is not mistaken for a different cell.
        private const float MatchTolerance = 0.05f;
        private const float MatchToleranceSq = MatchTolerance * MatchTolerance;

        /// <summary>A cell that exists in the plan.</summary>
        internal readonly struct Cell
        {
            public readonly float X;
            public readonly float Z;
            public readonly string WgoId;

            public Cell(float x, float z, string wgoId)
            {
                X = x;
                Z = z;
                WgoId = wgoId;
            }
        }

        /// <summary>Bit flags for the four neighbours, in N/E/S/W order.</summary>
        [Flags]
        internal enum Neighbours
        {
            None = 0,
            North = 1,
            East = 2,
            South = 4,
            West = 8,
        }

        /// <summary>Conveyor element ids this mod auto-tiles.</summary>
        public static bool IsAutoTileable(string wgoId)
        {
            // Only the plain cell types. Splitters, pallets, chests, power sources and
            // workbenches have their own multi-port logic and are left alone.
            return string.Equals(wgoId, "conveyor_cell", StringComparison.Ordinal)
                || string.Equals(wgoId, "conveyor_cell_underground", StringComparison.Ordinal)
                || string.Equals(wgoId, "conveyor_cell_station", StringComparison.Ordinal);
        }

        /// <summary>Finds the four axis neighbours of a cell among the plan.</summary>
        public static Neighbours NeighboursOf(Cell self, IReadOnlyList<Cell> plan)
        {
            Neighbours result = Neighbours.None;
            if (plan == null)
            {
                return result;
            }
            foreach (Cell other in plan)
            {
                // Only belt cells extend a belt. A chest or a workbench next door is not a
                // neighbour, and neither is the cell itself.
                if (!IsAutoTileable(other.WgoId) || SameCell(other, self))
                {
                    continue;
                }
                float dx = other.X - self.X;
                float dz = other.Z - self.Z;
                if (IsEast(dx, dz)) { result |= Neighbours.East; }
                else if (IsWest(dx, dz)) { result |= Neighbours.West; }
                else if (IsSouth(dx, dz)) { result |= Neighbours.South; }
                else if (IsNorth(dx, dz)) { result |= Neighbours.North; }
            }
            return result;
        }

        private static bool SameCell(Cell a, Cell b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return (dx * dx + dz * dz) <= MatchToleranceSq;
        }

        private static bool IsEast(float dx, float dz) =>
            Math.Abs(dx - CellSizeX) <= MatchTolerance && Math.Abs(dz) <= MatchTolerance;

        private static bool IsWest(float dx, float dz) =>
            Math.Abs(dx + CellSizeX) <= MatchTolerance && Math.Abs(dz) <= MatchTolerance;

        private static bool IsSouth(float dx, float dz) =>
            Math.Abs(dx) <= MatchTolerance && Math.Abs(dz - CellSizeZ) <= MatchTolerance;

        private static bool IsNorth(float dx, float dz) =>
            Math.Abs(dx) <= MatchTolerance && Math.Abs(dz + CellSizeZ) <= MatchTolerance;

        /// <summary>
        /// Maps a neighbour mask to (variation, rotation).
        ///
        /// Rotation convention, matching the game's own clockwise 0..3 stepping over
        /// { East, South, West, North }:
        ///   0 = belt runs along X (east-west)
        ///   1 = belt runs along Z (north-south)
        /// An 'end' points its belt at its neighbour.
        /// </summary>
        public static TileResult Resolve(Neighbours n, ConveyorTile forced, string[] knownVariations)
        {
            string variation;
            int rotation;

            switch (forced)
            {
                case ConveyorTile.Centre:
                    variation = Centre;
                    rotation = AxisRotation(n);
                    break;
                case ConveyorTile.End:
                    variation = End;
                    rotation = SingleNeighbourRotation(n);
                    break;
                case ConveyorTile.Single:
                    variation = Single;
                    rotation = 0;
                    break;
                default:
                    AutoResolve(n, out variation, out rotation);
                    break;
            }

            // Only keep a state the object actually has, otherwise the model would go missing.
            if (knownVariations != null && knownVariations.Length > 0 && !Contains(knownVariations, variation))
            {
                string fallback = PreferDefault(knownVariations);
                if (fallback != null)
                {
                    return new TileResult(fallback, rotation, fromCache: true);
                }
            }
            return new TileResult(variation, rotation, fromCache: false);
        }

        private static void AutoResolve(Neighbours n, out string variation, out int rotation)
        {
            int count = 0;
            if ((n & Neighbours.North) != 0) count++;
            if ((n & Neighbours.East) != 0) count++;
            if ((n & Neighbours.South) != 0) count++;
            if ((n & Neighbours.West) != 0) count++;

            switch (count)
            {
                case 0:
                    variation = Single;
                    rotation = 0;
                    break;
                case 1:
                    variation = End;
                    rotation = SingleNeighbourRotation(n);
                    break;
                case 2:
                    variation = Centre;
                    rotation = AxisRotation(n);
                    break;
                default:
                    // Three or four sides: the belt has to cross, 'centre' is the closest fit.
                    variation = Centre;
                    rotation = AxisRotation(n);
                    break;
            }
        }

        private static int SingleNeighbourRotation(Neighbours n)
        {
            if ((n & (Neighbours.East | Neighbours.West)) != 0) return 0;
            if ((n & (Neighbours.North | Neighbours.South)) != 0) return 1;
            return 0;
        }

        private static int AxisRotation(Neighbours n)
        {
            // Prefer the X axis when there is any east/west link, else fall back to Z.
            if ((n & (Neighbours.East | Neighbours.West)) != 0) return 0;
            if ((n & (Neighbours.North | Neighbours.South)) != 0) return 1;
            return 0;
        }

        private static bool Contains(string[] arr, string v)
        {
            foreach (string s in arr)
            {
                if (string.Equals(s, v, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string PreferDefault(string[] arr) => arr.Length > 0 ? arr[0] : null;
    }
}

