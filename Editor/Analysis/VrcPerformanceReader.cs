using System;
using System.Reflection;

namespace AvatarTuner.Editor.Analysis
{
    /// <summary>Reads the installed SDK's current-avatar metric without a hard SDK dependency.</summary>
    internal static class VrcPerformanceReader
    {
        public static bool TryRead(object avatar, string avatarName, bool mobile, out long bytes)
        {
            bytes = 0;
            if (avatar == null) return false;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var calculator = assembly.GetType("VRC.SDKBase.Validation.Performance.AvatarPerformance", false);
                    if (calculator == null) continue;
                    var statsType = assembly.GetType("VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats", false);
                    if (statsType == null) return false;
                    var metric = statsType.GetField("textureMegabytes", BindingFlags.Public | BindingFlags.Instance);
                    if (metric == null) return false;
                    foreach (var method in calculator.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.Name != "CalculatePerformanceStats") continue;
                        var parameters = method.GetParameters();
                        if (parameters.Length != 4 || parameters[0].ParameterType != typeof(string) ||
                            !parameters[1].ParameterType.IsInstanceOfType(avatar) ||
                            parameters[2].ParameterType != statsType || parameters[3].ParameterType != typeof(bool)) continue;
                        var stats = Activator.CreateInstance(statsType, new object[] { mobile });
                        method.Invoke(null, new[] { (object)avatarName, avatar, stats, mobile });
                        var value = metric.GetValue(stats);
                        if (!(value is float)) return false;
                        var megabytes = (float)value;
                        var rawBytes = megabytes * 1048576.0;
                        if (float.IsNaN(megabytes) || float.IsInfinity(megabytes) || rawBytes < 0 || rawBytes >= long.MaxValue) return false;
                        bytes = (long)Math.Round(rawBytes);
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception) { return false; }
            return false;
        }
    }
}
