using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Drawing.Tests
{
    /// <summary>
    /// Exercises the real ProjectionStore: JSON round-trip, escaping, float precision,
    /// malformed-input rejection, and the on-disk file format.
    /// </summary>
    internal static class StoreRoundTrip
    {
        private static int _failures;
        private static int _checks;

        public static int Run()
        {
            _failures = 0;
            _checks = 0;

            try
            {
                Directory.CreateDirectory(UnityEngine.Application.persistentDataPath);
                RoundTrip();
                RejectsGarbage();
                DiskRoundTrip();
                SurvivesJunkFile();
            }
            finally
            {
                TryDelete(UnityEngine.Application.persistentDataPath);
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? $"ALL PASS ({_checks} checks)"
                : $"{_failures} FAILURE(S) out of {_checks} checks");
            return _failures;
        }

        // ------------------------------------------------------------------ helpers

        private static void Reset()
        {
            ProjectionStore.ZonesForTest.Clear();
        }

        private static ProjectionEntry E(string id, float x, float y, float z, string variation, int rot) =>
            new ProjectionEntry(id, new Vector3(x, y, z), variation, rot);

        private static List<ProjectionEntry> Zone(string id)
        {
            if (!ProjectionStore.ZonesForTest.TryGetValue(id, out var l))
            {
                l = new List<ProjectionEntry>();
                ProjectionStore.ZonesForTest[id] = l;
            }
            return l;
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

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title);
        }

        // ------------------------------------------------------------------ tests

        private static void RoundTrip()
        {
            Section("json round-trip (incl. escaping and precision)");
            Reset();

            Zone("underground_graveyard").Add(E("workbench_press", 1.25f, 0f, -4.5f, "start", 2));
            Zone("underground_graveyard").Add(
                E("conveyor_cell \"quoted\" \\ back", -0.0625f, 0.125f, 12f, "Ñ‚ÐµÑÑ‚\nÑ\tÐ¿ÐµÑ€ÐµÐ½Ð¾ÑÐ¾Ð¼", -1));
            Zone("zone with spaces & \"quotes\"").Add(E("zone\\name", 0.0001f, -0f, 1e6f, "", 0));

            string json = ProjectionStore.SerializeForTest();
            Console.WriteLine("    --- serialized ---");
            foreach (string line in json.Split('\n'))
            {
                Console.WriteLine("    " + line);
            }

            Reset();
            Check("state cleared before parse", 0, ProjectionStore.ZonesForTest.Count);
            ProjectionStore.ParseForTest(json);

            Check("zone count", 2, ProjectionStore.ZonesForTest.Count);
            Check("zone1 count", 2, Zone("underground_graveyard").Count);
            Check("zone2 count", 1, Zone("zone with spaces & \"quotes\"").Count);

            ProjectionEntry a = Zone("underground_graveyard")[0];
            Check("a.WgoId", "workbench_press", a.WgoId);
            Check("a.X", 1.25f, a.X);
            Check("a.Y", 0f, a.Y);
            Check("a.Z", -4.5f, a.Z);
            Check("a.VariationId", "start", a.VariationId);
            Check("a.RotationIndex", 2, a.RotationIndex);

            ProjectionEntry b = Zone("underground_graveyard")[1];
            Check("b.WgoId (escapes)", "conveyor_cell \"quoted\" \\ back", b.WgoId);
            Check("b.X (negative fraction)", -0.0625f, b.X);
            Check("b.Z", 12f, b.Z);
            Check("b.VariationId (\\n \\t)", "Ñ‚ÐµÑÑ‚\nÑ\tÐ¿ÐµÑ€ÐµÐ½Ð¾ÑÐ¾Ð¼", b.VariationId);
            Check("b.RotationIndex", -1, b.RotationIndex);

            ProjectionEntry c = Zone("zone with spaces & \"quotes\"")[0];
            Check("c.WgoId (backslash)", "zone\\name", c.WgoId);
            Check("c.X (small)", 0.0001f, c.X);
            Check("c.Y (negative zero)", 0f, c.Y);
            Check("c.Z (large)", 1e6f, c.Z);
            Check("c.VariationId (empty)", "", c.VariationId);
            Check("c.RotationIndex (zero)", 0, c.RotationIndex);

            // Re-serializing the parsed data must produce the exact same text: this is what
            // guarantees a save/load/save cycle never drifts.
            string json2 = ProjectionStore.SerializeForTest();
            Check("serialize(parse(x)) == x", json, json2);
        }

        private static void RejectsGarbage()
        {
            Section("malformed input is rejected (never silently half-read)");
            string[] bad =
            {
                "{",
                "{\"zones\":}",
                "not json at all",
                "{\"zones\":{\"a\":[{\"wgo\":}]}}",
                "{\"zones\":{\"a\":[{\"pos\":[1,2]}]}}",
                "{\"zones\":{\"a\":[{\"rotation\":\"x\"}]}}",
                "{\"zones\":{\"a\":[{}]}}",     // tolerated, but must not throw
                "[]",
            };

            foreach (string input in bad)
            {
                Reset();
                bool threw = false;
                try
                {
                    ProjectionStore.ParseForTest(input);
                }
                catch (Exception)
                {
                    threw = true;
                }
                _checks++;
                if (input == "{\"zones\":{\"a\":[{}]}}" )
                {
                    // tolerated: an entry with no fields is legal, just empty
                    if (threw)
                    {
                        _failures++;
                        Console.WriteLine("  FAIL  empty entry should be tolerated");
                    }
                    else
                    {
                        Console.WriteLine("  ok    empty entry tolerated");
                    }
                    continue;
                }
                if (!threw)
                {
                    _failures++;
                    Console.WriteLine($"  FAIL  accepted garbage: {Short(input)}");
                }
                else
                {
                    Console.WriteLine("  ok    rejected: " + Short(input));
                }
            }
        }

        private static void DiskRoundTrip()
        {
            Section("on-disk file (what actually lands next to the save)");
            Reset();
            // Go through the real public API so the dirty-flag bookkeeping is exercised too.
            ProjectionStore.SetLoadedForTest(true);
            ProjectionStore.SetDirtyForTest(false);
            ProjectionStore.Add("zone_a", E("w1", 1f, 0f, 2f, "", -1));
            ProjectionStore.Add("zone_a", E("w2", 3f, 0f, 4f, "v", 1));
            ProjectionStore.Add("zone_b", E("w3", 5f, 0f, 6f, "", 0));
            ProjectionStore.Add("empty_zone_should_not_be_written", E("x", 0f, 0f, 0f, "", 0));
            ProjectionStore.ClearZone("empty_zone_should_not_be_written");

            ProjectionStore.Flush();
            string path = ProjectionStore.FilePath;
            Check("file exists", true, File.Exists(path));
            string text = File.ReadAllText(path);
            Check("no temp file left behind", false, File.Exists(path + ".tmp"));
            Check("empty zone omitted", false, text.Contains("empty_zone_should_not_be_written"));
            Check("version header", true, text.Contains("\"version\": 1"));

            Reset();
            ProjectionStore.SetLoadedForTest(true);
            ProjectionStore.SetDirtyForTest(false);
            ProjectionStore.LoadForTest();
            Check("zone_a after reload", 2, Zone("zone_a").Count);
            Check("zone_b after reload", 1, Zone("zone_b").Count);
            Check("w1 id", "w1", Zone("zone_a")[0].WgoId);
            Check("w2 rotation", 1, Zone("zone_a")[1].RotationIndex);
            Check("w1 X survived the file", 1f, Zone("zone_a")[0].X);
            Check("w3 Z survived the file", 6f, Zone("zone_b")[0].Z);

            // A second Flush with nothing dirty must not touch the file.
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            System.Threading.Thread.Sleep(20);
            ProjectionStore.Flush();
            Check("no write when not dirty", stamp, File.GetLastWriteTimeUtc(path).Ticks);

            // Remove/Clear must also mark the file dirty.
            ProjectionStore.Remove("zone_a", Zone("zone_a")[0]);
            ProjectionStore.Flush();
            Reset();
            ProjectionStore.SetLoadedForTest(true);
            ProjectionStore.LoadForTest();
            Check("remove persisted", 1, Zone("zone_a").Count);
        }

        private static void SurvivesJunkFile()
        {
            Section("a corrupted plan file cannot take the game down");
            string path = ProjectionStore.FilePath;
            File.WriteAllText(path, "{ this is not json at all ");
            Reset();
            ProjectionStore.SetLoadedForTest(true);
            ProjectionStore.SetDirtyForTest(false);
            bool threw = false;
            try
            {
                ProjectionStore.LoadForTest();
            }
            catch (Exception)
            {
                threw = true;
            }
            Check("Load did not propagate an exception", false, threw);
            Check("state is empty, not half-parsed", 0, ProjectionStore.ZonesForTest.Count);
            Check("junk file kept for inspection", true, File.Exists(path));
        }

        private static string Short(string s) => s.Length <= 44 ? s : s.Substring(0, 44) + "...";

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // temp dir left behind is not a test failure
            }
        }
    }
}




