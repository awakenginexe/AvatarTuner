using System;
using System.Globalization;
using System.Reflection;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Entry point for the standalone test suite.
    ///
    /// Exit code 0 means every test passed; non-zero means at least one failed. This makes
    /// the suite usable directly in a CI step or from a shell script.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var filter = string.Empty;
            var listOnly = false;

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--filter" && i + 1 < args.Length)
                {
                    filter = args[i + 1];
                    i++;
                }
                else if (args[i] == "--list")
                {
                    listOnly = true;
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    Console.WriteLine("AvatarTuner standalone test runner");
                    Console.WriteLine();
                    Console.WriteLine("  --filter <text>   only run tests whose full name contains <text>");
                    Console.WriteLine("  --list            list discovered tests and exit");
                    Console.WriteLine("  --help            show this message");
                    return 0;
                }
            }

            var assembly = Assembly.GetExecutingAssembly();
            var tests = TestRunner.Discover(assembly);

            Console.WriteLine("AvatarTuner v2.0.0 - standalone Core test suite");
            Console.WriteLine("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine("Discovered " + tests.Count.ToString(CultureInfo.InvariantCulture) + " test(s).");
            Console.WriteLine();

            if (listOnly)
            {
                for (var i = 0; i < tests.Count; i++)
                {
                    Console.WriteLine("  " + tests[i].FullName);
                }

                return 0;
            }

            int passed, failed, skipped;
            var allPassed = TestRunner.Run(tests, filter, out passed, out failed, out skipped);

            Console.WriteLine();
            Console.WriteLine("Result: " + passed.ToString(CultureInfo.InvariantCulture) + " passed, " +
                              failed.ToString(CultureInfo.InvariantCulture) + " failed, " +
                              skipped.ToString(CultureInfo.InvariantCulture) + " skipped.");

            return allPassed ? 0 : 1;
        }
    }
}
