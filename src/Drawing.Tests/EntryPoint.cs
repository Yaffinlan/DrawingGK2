using System;

namespace Drawing.Tests
{
    internal static class EntryPoint
    {
        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("Drawing - unit tests");
            Console.WriteLine();
            int failures = StoreRoundTrip.Run();
            failures += ConveyorTilerTests.Run();
            return failures;
        }
    }
}


