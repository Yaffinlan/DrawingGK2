using System;
using System.Collections.Generic;
using Drawing;

namespace Drawing.Tests
{
    /// <summary>
    /// The conveyor tile resolver is pure logic, so it is tested directly: given a set of cells
    /// and a neighbour mask, which tile and rotation must a drawing show?
    /// </summary>
    internal static class ConveyorTilerTests
    {
        private static int _failures;
        private static int _checks;

        private static readonly string[] Cell =
        {
            ConveyorTiler.Centre, ConveyorTiler.End, ConveyorTiler.Single
        };

        public static int Run()
        {
            _failures = 0;
            _checks = 0;

            NeighbourMasks();
            AutoRules();
            ManualOverrides();
            UnknownVariationFallsBack();
            PlanTopologies();
            AutoTileableIds();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? $"ALL PASS ({_checks} checks)"
                : $"{_failures} FAILURE(S) out of {_checks} checks");
            return _failures;
        }

        private static void Check(string name, object expected, object actual)
        {
            _checks++;
            bool ok = Equals(expected, actual);
            if (!ok)
            {
                _failures++;
            }
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + name +
                              (ok ? "" : $"   expected <{expected}> got <{actual}>"));
        }

        private static void Section(string t)
        {
            Console.WriteLine();
            Console.WriteLine("== " + t);
        }

        private static void Tile(ConveyorTiler.Neighbours n, string expectVar, int expectRot,
                                 string label = null)
        {
            var r = ConveyorTiler.Resolve(n, ConveyorTile.Auto, Cell);
            Check(label ?? n.ToString(), expectVar + "/" + expectRot, r.Variation + "/" + r.Rotation);
        }

        // ---------------------------------------------------------------- tests

        private static void NeighbourMasks()
        {
            Section("neighbour detection on a real cell grid");
            const float X = ConveyorTiler.CellSizeX;
            const float Z = ConveyorTiler.CellSizeZ;

            var plan = new List<ConveyorTiler.Cell>
            {
                new ConveyorTiler.Cell(0f, 0f, "conveyor_cell"),          // self
                new ConveyorTiler.Cell(X, 0f, "conveyor_cell"),           // east
                new ConveyorTiler.Cell(-X, 0f, "conveyor_cell"),          // west
                new ConveyorTiler.Cell(0f, -Z, "conveyor_cell"),          // north
                new ConveyorTiler.Cell(X * 9f, Z * 9f, "conveyor_cell"),  // far away
            };
            var self = plan[0];
            var got = ConveyorTiler.NeighboursOf(self, plan);
            Check("isolated cell sees only its 4 neighbours",
                ConveyorTiler.Neighbours.East | ConveyorTiler.Neighbours.West |
                ConveyorTiler.Neighbours.North, got);
            Check("diagonal is not a neighbour",
                ConveyorTiler.Neighbours.None,
                ConveyorTiler.NeighboursOf(
                    new ConveyorTiler.Cell(0f, 0f, "conveyor_cell"),
                    new List<ConveyorTiler.Cell> { new ConveyorTiler.Cell(X, Z, "conveyor_cell") }));
            Check("tiny float noise still counts",
                ConveyorTiler.Neighbours.East,
                ConveyorTiler.NeighboursOf(
                    new ConveyorTiler.Cell(0f, 0f, "conveyor_cell"),
                    new List<ConveyorTiler.Cell> {
                        new ConveyorTiler.Cell(X + 0.0001f, -0.0001f, "conveyor_cell") }));
            Check("a different object type is not a belt neighbour",
                ConveyorTiler.Neighbours.None,
                ConveyorTiler.NeighboursOf(
                    new ConveyorTiler.Cell(0f, 0f, "conveyor_cell"),
                    new List<ConveyorTiler.Cell> { new ConveyorTiler.Cell(X, 0f, "conveyor_chest_t1_place") }));
        }

        private static void AutoRules()
        {
            Section("auto tile rules");
            Tile(ConveyorTiler.Neighbours.None, ConveyorTiler.Single, 0);
            Tile(ConveyorTiler.Neighbours.East, ConveyorTiler.End, 0);
            Tile(ConveyorTiler.Neighbours.West, ConveyorTiler.End, 0);
            Tile(ConveyorTiler.Neighbours.North, ConveyorTiler.End, 1);
            Tile(ConveyorTiler.Neighbours.South, ConveyorTiler.End, 1);
            Tile(ConveyorTiler.Neighbours.East | ConveyorTiler.Neighbours.West,
                 ConveyorTiler.Centre, 0);
            Tile(ConveyorTiler.Neighbours.North | ConveyorTiler.Neighbours.South,
                 ConveyorTiler.Centre, 1);
            Tile(ConveyorTiler.Neighbours.East | ConveyorTiler.Neighbours.South,
                 ConveyorTiler.Centre, 0, "corner: prefers the X axis");
            Tile(ConveyorTiler.Neighbours.North | ConveyorTiler.Neighbours.East |
                 ConveyorTiler.Neighbours.South | ConveyorTiler.Neighbours.West,
                 ConveyorTiler.Centre, 0, "crossing");
        }

        private static void ManualOverrides()
        {
            Section("manual override wins over auto");
            var forcedCentre = ConveyorTiler.Resolve(ConveyorTiler.Neighbours.None,
                ConveyorTile.Centre, Cell);
            Check("forced centre on an isolated cell", "centre", forcedCentre.Variation);

            var forcedEnd = ConveyorTiler.Resolve(
                ConveyorTiler.Neighbours.East | ConveyorTiler.Neighbours.West, ConveyorTile.End, Cell);
            Check("forced end on a through cell", "end", forcedEnd.Variation);
            Check("forced end keeps the axis rotation", 0, forcedEnd.Rotation);

            var forcedSingle = ConveyorTiler.Resolve(
                ConveyorTiler.Neighbours.East, ConveyorTile.Single, Cell);
            Check("forced single", "single", forcedSingle.Variation);
            Check("forced single has no rotation", 0, forcedSingle.Rotation);
        }

        private static void UnknownVariationFallsBack()
        {
            Section("a state the object does not have falls back instead of vanishing");
            // e.g. an object that only knows 'centre'
            var onlyCentre = new[] { ConveyorTiler.Centre };
            var r = ConveyorTiler.Resolve(ConveyorTiler.Neighbours.None, ConveyorTile.Auto, onlyCentre);
            Check("falls back to the only known state", ConveyorTiler.Centre, r.Variation);
            Check("fallback is flagged", true, r.FromCache);

            var r2 = ConveyorTiler.Resolve(ConveyorTiler.Neighbours.None, ConveyorTile.Auto, null);
            Check("no known states -> keeps the computed one", ConveyorTiler.Single, r2.Variation);
        }

        private static void PlanTopologies()
        {
            Section("whole-plan behaviour");
            const float X = ConveyorTiler.CellSizeX;
            const float Z = ConveyorTiler.CellSizeZ;

            // A straight run of 3: single / centre / single
            var run = new List<ConveyorTiler.Cell>
            {
                new ConveyorTiler.Cell(0f, 0f, "conveyor_cell"),
                new ConveyorTiler.Cell(X, 0f, "conveyor_cell"),
                new ConveyorTiler.Cell(X * 2f, 0f, "conveyor_cell"),
            };
            var expectations = new[]
            {
                new[] { ConveyorTiler.End, "left end" },
                new[] { ConveyorTiler.Centre, "middle" },
                new[] { ConveyorTiler.End, "right end" },
            };
            for (int i = 0; i < run.Count; i++)
            {
                var res = ConveyorTiler.Resolve(
                    ConveyorTiler.NeighboursOf(run[i], run), ConveyorTile.Auto, Cell);
                Check("run of 3: " + expectations[i][1],
                    expectations[i][0], res.Variation);
            }

            // An L shape: the corner ends up 'centre' along X, the two arms 'end'.
            var elbow = new List<ConveyorTiler.Cell>
            {
                new ConveyorTiler.Cell(0f, 0f, "conveyor_cell"),
                new ConveyorTiler.Cell(X, 0f, "conveyor_cell"),
                new ConveyorTiler.Cell(X, Z, "conveyor_cell"),
            };
            var corner = ConveyorTiler.Resolve(
                ConveyorTiler.NeighboursOf(elbow[1], elbow), ConveyorTile.Auto, Cell);
            Check("elbow joint is a through tile", ConveyorTiler.Centre, corner.Variation);
            var armA = ConveyorTiler.Resolve(
                ConveyorTiler.NeighboursOf(elbow[0], elbow), ConveyorTile.Auto, Cell);
            Check("elbow arm A terminates", ConveyorTiler.End, armA.Variation);
            var armB = ConveyorTiler.Resolve(
                ConveyorTiler.NeighboursOf(elbow[2], elbow), ConveyorTile.Auto, Cell);
            Check("elbow arm B terminates", ConveyorTiler.End, armB.Variation);
            Check("elbow arm B runs along Z", 1, armB.Rotation);

            // Adding a cell must change its neighbours: that is what re-tiling relies on.
            var before = ConveyorTiler.Resolve(
                ConveyorTiler.NeighboursOf(run[0], run), ConveyorTile.Auto, Cell);
            run.Add(new ConveyorTiler.Cell(X * 3f, 0f, "conveyor_cell"));
            var after = ConveyorTiler.Resolve(
                ConveyorTiler.NeighboursOf(run[0], run), ConveyorTile.Auto, Cell);
            Check("extending the run does not disturb the far end", before.Variation, after.Variation);
            var newMiddle = ConveyorTiler.Resolve(
                ConveyorTiler.NeighboursOf(run[2], run), ConveyorTile.Auto, Cell);
            Check("the old end becomes a through tile",
                ConveyorTiler.Centre, newMiddle.Variation);
        }

        private static void AutoTileableIds()
        {
            Section("which ids get auto-tiled");
            Check("plain cell", true, ConveyorTiler.IsAutoTileable("conveyor_cell"));
            Check("splitter is left alone", false, ConveyorTiler.IsAutoTileable("conveyor_splitter"));
            Check("workbench is left alone", false,
                ConveyorTiler.IsAutoTileable("conveyor_furnace_t1_place"));
            Check("chest is left alone", false,
                ConveyorTiler.IsAutoTileable("conveyor_chest_t1_place"));
            Check("case sensitive", false, ConveyorTiler.IsAutoTileable("CONVEYOR_CELL"));
        }
    }
}

