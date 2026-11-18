using System;
using System.Reflection;
using AvatarTuner.StandaloneTests;

internal static class AgentApiCheckProgram
{
    private static int Main()
    {
        int passed, failed, skipped;
        var success = TestRunner.Run(TestRunner.Discover(Assembly.GetExecutingAssembly()), string.Empty, out passed, out failed, out skipped);
        Console.WriteLine("Offline agent API checks: " + passed + " passed, " + failed + " failed.");
        return success ? 0 : 1;
    }
}
