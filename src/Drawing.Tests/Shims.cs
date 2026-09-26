using System;
using System.IO;

// Minimal stand-ins for the two Unity types ProjectionStore touches, so the *real*
// ProjectionStore.cs source can be compiled and exercised outside the game process.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public static class Application
    {
        public static string persistentDataPath = Path.Combine(
            Path.GetTempPath(), "gk2_projection_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
    }
}

namespace Drawing
{
    // Test output goes to stdout even for Error(): a native process writing to stderr makes
    // Windows PowerShell raise a terminating error, and these "errors" are expected cases.
    internal static class Log
    {
        public static void Info(string m) => Console.WriteLine("    [info] " + m);
        public static void Warn(string m) => Console.WriteLine("    [warn] " + m);
        public static void Error(string m) => Console.WriteLine("    [err ] " + m);
        public static void Error(string m, Exception e) => Console.WriteLine("    [err ] " + m + " :: " + e.Message);
    }
}

