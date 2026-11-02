using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Marks a parameterless method as a test case. Both static and instance methods work.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class TestAttribute : Attribute
    {
        public string Description { get; set; }

        public TestAttribute()
        {
        }

        public TestAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>Thrown when an assertion fails. Carries a readable message.</summary>
    public sealed class AssertionException : Exception
    {
        public AssertionException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Minimal assertion helpers. Kept dependency free so the test project needs no NuGet
    /// restore and therefore runs on a machine without network access.
    /// </summary>
    public static class Assert
    {
        public static void IsTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException("Expected true: " + message);
            }
        }

        public static void IsFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new AssertionException("Expected false: " + message);
            }
        }

        public static void AreEqual(object expected, object actual, string message)
        {
            if (!Equals(expected, actual) && !NumericallyEqual(expected, actual))
            {
                throw new AssertionException(
                    message + "\n  expected: " + Render(expected) + "\n  actual:   " + Render(actual));
            }
        }

        /// <summary>
        /// Numeric identity across boxed types: 128 (int) and (byte) 128 are the same value.
        /// Without this an assertion on a byte[] sample fails while printing identical numbers.
        /// </summary>
        private static bool NumericallyEqual(object expected, object actual)
        {
            if (expected == null || actual == null || !IsNumeric(expected) || !IsNumeric(actual))
            {
                return false;
            }

            try
            {
                return Convert.ToDecimal(expected, CultureInfo.InvariantCulture) ==
                       Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsNumeric(object value)
        {
            return value is sbyte || value is byte || value is short || value is ushort ||
                   value is int || value is uint || value is long || value is ulong ||
                   value is float || value is double || value is decimal;
        }

        public static void AreNotEqual(object unexpected, object actual, string message)
        {
            if (Equals(unexpected, actual))
            {
                throw new AssertionException(message + "\n  did not expect: " + Render(actual));
            }
        }

        public static void AreEqualIgnoreCase(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new AssertionException(
                    message + "\n  expected (case insensitive): " + expected + "\n  actual:                     " + actual);
            }
        }

        public static void IsNull(object value, string message)
        {
            if (value != null)
            {
                throw new AssertionException(message + "\n  expected null but got: " + Render(value));
            }
        }

        public static void IsNotNull(object value, string message)
        {
            if (value == null)
            {
                throw new AssertionException(message + "\n  expected a value but got null");
            }
        }

        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionException(
                    message + "\n  expected " + typeof(TException).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }

            throw new AssertionException(message + "\n  expected " + typeof(TException).Name + " but nothing was thrown");
        }

        public static void DoesNotThrow(Action action, string message)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new AssertionException(message + "\n  unexpected " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void AreClose(double expected, double actual, double tolerance, string message)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new AssertionException(
                    message + "\n  expected: " + expected.ToString(CultureInfo.InvariantCulture) +
                    " +/- " + tolerance.ToString(CultureInfo.InvariantCulture) +
                    "\n  actual:   " + actual.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static void IsGreater(double minimum, double actual, string message)
        {
            if (!(actual > minimum))
            {
                throw new AssertionException(
                    message + "\n  expected > " + minimum.ToString(CultureInfo.InvariantCulture) +
                    "\n  actual:     " + actual.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static void IsLess(double maximum, double actual, string message)
        {
            if (!(actual < maximum))
            {
                throw new AssertionException(
                    message + "\n  expected < " + maximum.ToString(CultureInfo.InvariantCulture) +
                    "\n  actual:     " + actual.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static void Contains(string needle, string haystack, string message)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            {
                throw new AssertionException(message + "\n  expected to contain: " + needle + "\n  in: " + (haystack ?? "<null>"));
            }
        }

        public static void Fail(string message)
        {
            throw new AssertionException(message);
        }

        private static string Render(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            var text = value as string;
            return text != null ? "\"" + text + "\"" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One discovered test case.</summary>
    public sealed class TestCase
    {
        public MethodInfo Method;
        public Type DeclaringType;
        public string Name;
        public string Description;

        public string FullName
        {
            get { return DeclaringType.Name + "." + Method.Name; }
        }
    }

    /// <summary>Reflection based test discovery and execution.</summary>
    public static class TestRunner
    {
        public static List<TestCase> Discover(Assembly assembly)
        {
            var results = new List<TestCase>();
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract && type.IsSealed)
                {
                    // static class: fine
                }
                else if (type.IsAbstract)
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var attribute = (TestAttribute)Attribute.GetCustomAttribute(method, typeof(TestAttribute));
                    if (attribute == null)
                    {
                        continue;
                    }

                    if (method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    results.Add(new TestCase
                    {
                        Method = method,
                        DeclaringType = type,
                        Name = method.Name,
                        Description = attribute.Description ?? string.Empty
                    });
                }
            }

            results.Sort(delegate(TestCase a, TestCase b)
            {
                var byType = string.CompareOrdinal(a.DeclaringType.FullName, b.DeclaringType.FullName);
                return byType != 0 ? byType : string.CompareOrdinal(a.Method.Name, b.Method.Name);
            });

            return results;
        }

        /// <summary>Runs the tests, returning true when everything passed.</summary>
        public static bool Run(IList<TestCase> tests, string filter, out int passed, out int failed, out int skipped)
        {
            passed = 0;
            failed = 0;
            skipped = 0;

            var failures = new List<string>();

            for (var i = 0; i < tests.Count; i++)
            {
                var test = tests[i];

                if (!string.IsNullOrEmpty(filter) &&
                    test.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    skipped++;
                    continue;
                }

                object instance = null;
                if (!test.Method.IsStatic)
                {
                    instance = Activator.CreateInstance(test.DeclaringType);
                }

                try
                {
                    test.Method.Invoke(instance, null);
                    passed++;
                    Console.WriteLine("  PASS " + test.FullName);
                }
                catch (TargetInvocationException wrapper)
                {
                    failed++;
                    var inner = wrapper.InnerException ?? wrapper;
                    Console.WriteLine("  FAIL " + test.FullName);
                    Console.WriteLine("       " + inner.Message.Replace("\n", "\n       "));
                    failures.Add(test.FullName + ": " + inner.Message);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("  FAIL " + test.FullName);
                    Console.WriteLine("       " + ex.Message.Replace("\n", "\n       "));
                    failures.Add(test.FullName + ": " + ex.Message);
                }
            }

            if (failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failures:");
                for (var i = 0; i < failures.Count; i++)
                {
                    Console.WriteLine("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + failures[i]);
                }
            }

            return failed == 0;
        }
    }
}
