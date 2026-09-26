using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Drawing
{
    /// <summary>One stored projection: a blueprint id plus a world position and rotation.</summary>
    [Serializable]
    internal sealed class ProjectionEntry
    {
        public string WgoId = "";
        public float X;
        public float Y;
        public float Z;
        public string VariationId = "";
        public int RotationIndex = -1;

        /// <summary>
        /// Conveyor tile choice. Auto (0, the default) lets the mod derive centre/end/single
        /// from the neighbouring cells; 1/2/3 pin it. Persisted so a hand-picked tile survives
        /// a restart. Non-conveyor drawings ignore it.
        /// </summary>
        public int Tile = 0;

        public ProjectionEntry() { }

        public ProjectionEntry(string wgoId, Vector3 pos, string variationId, int rotationIndex)
        {
            WgoId = wgoId;
            X = pos.x;
            Y = pos.y;
            Z = pos.z;
            VariationId = variationId ?? "";
            RotationIndex = rotationIndex;
        }

        public Vector3 Position => new Vector3(X, Y, Z);

        public ConveyorTile TileChoice => (ConveyorTile)Math.Max(0, Math.Min(3, Tile));

        public void SetTile(ConveyorTile t)
        {
            Tile = (int)t;
        }
    }

    /// <summary>
    /// Projections live in their own file next to the game saves. The game's own save
    /// (<c>Steam_1.dat</c>) is never touched, so a corrupt or stale projection file can
    /// never brick a playthrough.
    /// </summary>
    internal static class ProjectionStore
    {
        private const string FileName = "gk2_projections.json";
        private const int CurrentVersion = 1;

        // zoneId -> projections
        private static readonly Dictionary<string, List<ProjectionEntry>> Zones =
            new Dictionary<string, List<ProjectionEntry>>(StringComparer.Ordinal);

        private static bool _loaded;
        private static bool _dirty;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            Load();
        }

        public static List<ProjectionEntry> GetZone(string zoneId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(zoneId))
            {
                return new List<ProjectionEntry>();
            }
            if (!Zones.TryGetValue(zoneId, out var list))
            {
                list = new List<ProjectionEntry>();
                Zones[zoneId] = list;
            }
            return list;
        }

        public static void Add(string zoneId, ProjectionEntry entry)
        {
            GetZone(zoneId).Add(entry);
            _dirty = true;
        }

        public static void Remove(string zoneId, ProjectionEntry entry)
        {
            var list = GetZone(zoneId);
            if (list.Remove(entry))
            {
                _dirty = true;
            }
        }

        public static int ClearZone(string zoneId)
        {
            var list = GetZone(zoneId);
            int n = list.Count;
            list.Clear();
            if (n > 0)
            {
                _dirty = true;
            }
            return n;
        }

        public static void ClearAllZones()
        {
            foreach (var list in Zones.Values)
            {
                list.Clear();
            }
            _dirty = true;
        }

        /// <summary>Call when the game is shutting down or the zone is left.</summary>
        public static void Flush()
        {
            EnsureLoaded();
            if (!_dirty)
            {
                return;
            }
            try
            {
                string json = Serialize();
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                // Atomic-ish replace so a crash mid-write cannot truncate the real file.
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
                File.Move(tmp, FilePath);
                _dirty = false;
                Log.Info("Projections saved to " + FilePath);
            }
            catch (Exception e)
            {
                Log.Error("Failed to save projections to " + FilePath, e);
            }
        }

        private static void Load()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                Log.Info("No projection file yet (" + path + ") - starting with an empty plan.");
                return;
            }
            try
            {
                Parse(File.ReadAllText(path));
                int total = 0;
                foreach (var kv in Zones)
                {
                    total += kv.Value.Count;
                }
                Log.Info("Loaded " + total + " projection(s) across " + Zones.Count + " zone(s) from " + path);
            }
            catch (Exception e)
            {
                Log.Error("Could not parse " + path + " - starting from an empty plan. The file is kept for inspection.", e);
                Zones.Clear();
            }
        }

        // ---- minimal hand-rolled JSON (no external dependency) --------------------

        private static string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": ").Append(CurrentVersion).Append(",\n  \"zones\": {");
            bool firstZone = true;
            foreach (var kv in Zones)
            {
                if (kv.Value.Count == 0)
                {
                    continue;
                }
                if (!firstZone)
                {
                    sb.Append(',');
                }
                firstZone = false;
                sb.Append("\n    ").Append(Quote(kv.Key)).Append(": [");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var e = kv.Value[i];
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append("\n      {")
                      .Append("\"wgo\": ").Append(Quote(e.WgoId)).Append(',')
                      .Append("\"pos\": [").Append(Num(e.X)).Append(", ").Append(Num(e.Y)).Append(", ").Append(Num(e.Z)).Append(']')
                      .Append(", \"variation\": ").Append(Quote(e.VariationId))
                      .Append(", \"rotation\": ").Append(e.RotationIndex.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"tile\": ").Append(e.Tile.ToString(CultureInfo.InvariantCulture))
                      .Append('}');
                }
                sb.Append("\n    ]");
            }
            sb.Append("\n  }\n}\n");
            return sb.ToString();
        }

        private static string Num(float f) =>
            f.ToString("0.#####", CultureInfo.InvariantCulture);

        private static string Quote(string s)
        {
            if (s == null)
            {
                return "\"\"";
            }
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static void Parse(string json)
        {
            Zones.Clear();
            int i = SkipWs(json, 0);
            if (i >= json.Length || json[i] != '{')
            {
                throw new FormatException("root is not an object");
            }
            i = SkipWs(json, i + 1);
            // walk top-level members
            while (i < json.Length && json[i] != '}')
            {
                string key = ReadString(json, ref i);
                i = SkipWs(json, i);
                Expect(json, ref i, ':');
                i = SkipWs(json, i);

                if (key == "version")
                {
                    ReadNumber(json, ref i);
                }
                else if (key == "zones")
                {
                    if (i >= json.Length || json[i] != '{')
                    {
                        throw new FormatException("'zones' must be an object, at offset " + i);
                    }
                    ParseZones(json, ref i);
                }
                else
                {
                    SkipValue(json, ref i);
                }

                i = SkipWs(json, i);
                if (i < json.Length && json[i] == ',')
                {
                    i = SkipWs(json, i + 1);
                }
            }
            // A truncated or hand-mangled file must be reported, not silently turned into an
            // empty plan - otherwise the user just loses their layout with no explanation.
            i = SkipWs(json, i);
            if (i >= json.Length || json[i] != '}')
            {
                throw new FormatException("unterminated root object at offset " + i);
            }
        }

        private static void ParseZones(string json, ref int i)
        {
            i = SkipWs(json, i + 1); // past '{'
            while (i < json.Length && json[i] != '}')
            {
                string zoneId = ReadString(json, ref i);
                i = SkipWs(json, i);
                Expect(json, ref i, ':');
                i = SkipWs(json, i);
                var list = new List<ProjectionEntry>();
                if (i < json.Length && json[i] == '[')
                {
                    i = SkipWs(json, i + 1);
                    while (i < json.Length && json[i] != ']')
                    {
                        list.Add(ParseEntry(json, ref i));
                        i = SkipWs(json, i);
                        if (i < json.Length && json[i] == ',')
                        {
                            i = SkipWs(json, i + 1);
                        }
                    }
                    if (i < json.Length)
                    {
                        i++; // past ']'
                    }
                }
                if (list.Count > 0)
                {
                    Zones[zoneId] = list;
                }
                i = SkipWs(json, i);
                if (i < json.Length && json[i] == ',')
                {
                    i = SkipWs(json, i + 1);
                }
            }
            if (i < json.Length)
            {
                i++; // past '}'
            }
        }

        private static ProjectionEntry ParseEntry(string json, ref int i)
        {
            var e = new ProjectionEntry();
            i = SkipWs(json, i);
            Expect(json, ref i, '{'); // Expect already steps past the brace
            i = SkipWs(json, i);
            while (i < json.Length && json[i] != '}')
            {
                string key = ReadString(json, ref i);
                i = SkipWs(json, i);
                Expect(json, ref i, ':');
                i = SkipWs(json, i);
                switch (key)
                {
                    case "wgo":
                        e.WgoId = ReadString(json, ref i);
                        break;
                    case "variation":
                        e.VariationId = ReadString(json, ref i);
                        break;
                    case "rotation":
                        e.RotationIndex = (int)ReadNumber(json, ref i);
                        break;
                    case "tile":
                        e.Tile = (int)ReadNumber(json, ref i);
                        break;
                    case "pos":
                        i = SkipWs(json, i);
                        Expect(json, ref i, '[');
                        i = SkipWs(json, i); // Expect already stepped past '['
                        e.X = (float)ReadNumber(json, ref i);
                        i = SkipWs(json, i);
                        Expect(json, ref i, ',');
                        i = SkipWs(json, i); // ...and past ','
                        e.Y = (float)ReadNumber(json, ref i);
                        i = SkipWs(json, i);
                        Expect(json, ref i, ',');
                        i = SkipWs(json, i);
                        e.Z = (float)ReadNumber(json, ref i);
                        i = SkipWs(json, i);
                        Expect(json, ref i, ']');
                        break;
                    default:
                        SkipValue(json, ref i);
                        break;
                }
                i = SkipWs(json, i);
                if (i < json.Length && json[i] == ',')
                {
                    i = SkipWs(json, i + 1);
                }
            }
            if (i < json.Length)
            {
                i++; // past '}'
            }
            return e;
        }

        private static void Expect(string s, ref int i, char c)
        {
            i = SkipWs(s, i);
            if (i >= s.Length || s[i] != c)
            {
                throw new FormatException("expected '" + c + "' at offset " + i);
            }
            i++;
        }

        private static int SkipWs(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            return i;
        }

        private static string ReadString(string s, ref int i)
        {
            i = SkipWs(s, i);
            if (i >= s.Length || s[i] != '"')
            {
                throw new FormatException("expected string at offset " + i);
            }
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    if (i >= s.Length)
                    {
                        break;
                    }
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                            i += 4;
                            break;
                        default: sb.Append(s[i]); break;
                    }
                    i++;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            if (i >= s.Length)
            {
                throw new FormatException("unterminated string at offset " + i);
            }
            i++; // past closing quote
            return sb.ToString();
        }

        private static double ReadNumber(string s, ref int i)
        {
            i = SkipWs(s, i);
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            {
                i++;
            }
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '-' || s[i] == '+'))
            {
                i++;
            }
            if (start == i)
            {
                throw new FormatException("expected number at offset " + i);
            }
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private static void SkipValue(string s, ref int i)
        {
            i = SkipWs(s, i);
            if (i >= s.Length)
            {
                return;
            }
            char c = s[i];
            if (c == '"')
            {
                ReadString(s, ref i);
                return;
            }
            if (c == '{' || c == '[')
            {
                char open = c;
                char close = c == '{' ? '}' : ']';
                int depth = 0;
                while (i < s.Length)
                {
                    if (s[i] == '"')
                    {
                        ReadString(s, ref i);
                        continue;
                    }
                    if (s[i] == open)
                    {
                        depth++;
                    }
                    else if (s[i] == close)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            i++;
                            return;
                        }
                    }
                    i++;
                }
                throw new FormatException("unterminated container at offset " + i);
            }
            // literal (true/false/null) or number
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']')
            {
                i++;
            }
        }

        // --- test seam ------------------------------------------------------------------
        // The unit-test project compiles this exact file with UnityEngine stubbed out, so the
        // serializer is exercised as shipped rather than through reflection.
        internal static string SerializeForTest() => Serialize();
        internal static void ParseForTest(string json) => Parse(json);
        internal static void LoadForTest() => Load();
        internal static Dictionary<string, List<ProjectionEntry>> ZonesForTest => Zones;
        internal static void SetLoadedForTest(bool value) => _loaded = value;
        internal static void SetDirtyForTest(bool value) => _dirty = value;
    }
}

