using System;
using System.Globalization;

namespace AvatarTuner.Core
{
    /// <summary>
    /// A parseable Unity engine version such as <c>2022.3.22f1</c> or <c>2022.3</c>.
    ///
    /// The change tracker records the engine version that produced an optimization so that
    /// switching Unity versions can be surfaced as a re-optimization reason rather than
    /// silently producing stale generated assets.
    /// </summary>
    public struct EngineVersion : IComparable<EngineVersion>, IEquatable<EngineVersion>
    {
        public int Major;
        public int Minor;
        public int Patch;

        /// <summary>Raw text exactly as supplied, e.g. <c>2022.3.22f1</c>.</summary>
        public string Raw;

        /// <summary>Build suffix such as <c>f1</c>, <c>b3</c>, <c>a1</c> or <c>rc1</c>.</summary>
        public string Suffix;

        public EngineVersion(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Suffix = string.Empty;
            Raw = major.ToString(CultureInfo.InvariantCulture) + "." +
                  minor.ToString(CultureInfo.InvariantCulture) + "." +
                  patch.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>True when the version carries a <c>f</c> (final/public) build suffix.</summary>
        public bool IsFinal
        {
            get { return string.IsNullOrEmpty(Suffix) || (Suffix.Length > 0 && (Suffix[0] == 'f' || Suffix[0] == 'F')); }
        }

        /// <summary>
        /// Parses a version string. Returns false rather than throwing so that a malformed
        /// version recorded in a manifest never prevents the tracker from loading.
        ///
        /// Accepted shapes:
        ///   2022.3.22f1     major.minor.patch + build suffix
        ///   2022.3.22       major.minor.patch
        ///   2022.3          major.minor (patch defaults to 0)
        ///   2022            major only
        ///
        /// Rejected shapes include anything where a dot separated segment is not purely
        /// numeric except the final one, which may carry a build suffix such as <c>f1</c>,
        /// <c>a12</c>, <c>b3</c>, <c>rc1</c> or <c>p2</c>. This is why <c>2022.x.1</c> is
        /// rejected: <c>x</c> sits in a non-final segment and is not numeric.
        /// </summary>
        public static bool TryParse(string text, out EngineVersion version)
        {
            version = default(EngineVersion);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var pieces = trimmed.Split('.');
            if (pieces.Length > 3)
            {
                return false;
            }

            var numbers = new int[3];

            // Every segment except the last must be a plain non-negative integer.
            for (var i = 0; i < pieces.Length - 1; i++)
            {
                if (!IsDigits(pieces[i]))
                {
                    return false;
                }

                if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return false;
                }
            }

            // The final segment may carry the build suffix: digits, then letters, then digits.
            var last = pieces[pieces.Length - 1];
            string suffix;
            if (!SplitFinalSegment(last, out numbers[pieces.Length - 1], out suffix))
            {
                return false;
            }

            if (!IsValidSuffix(suffix))
            {
                return false;
            }

            version = new EngineVersion(numbers[0], numbers[1], numbers[2]);
            version.Suffix = suffix;
            version.Raw = trimmed;
            return true;
        }

        /// <summary>True when the string is a non-empty run of decimal digits.</summary>
        private static bool IsDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Splits the final version segment into its numeric patch part and its build
        /// suffix. Requires at least one leading digit so that a segment such as <c>f1</c>
        /// is rejected rather than silently read as patch 0.
        /// </summary>
        private static bool SplitFinalSegment(string segment, out int number, out string suffix)
        {
            number = 0;
            suffix = string.Empty;

            if (string.IsNullOrEmpty(segment))
            {
                return false;
            }

            var digitCount = 0;
            while (digitCount < segment.Length && segment[digitCount] >= '0' && segment[digitCount] <= '9')
            {
                digitCount++;
            }

            if (digitCount == 0)
            {
                return false;
            }

            var digits = segment.Substring(0, digitCount);
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }

            suffix = segment.Substring(digitCount);
            return true;
        }

        /// <summary>
        /// A build suffix is one or more letters optionally followed by digits, for example
        /// <c>f1</c>, <c>a12</c>, <c>rc1</c>. The empty suffix is valid and means no suffix.
        /// </summary>
        private static bool IsValidSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return true;
            }

            var i = 0;
            var letters = 0;
            while (i < suffix.Length && IsLetter(suffix[i]))
            {
                letters++;
                i++;
            }

            if (letters == 0)
            {
                return false;
            }

            while (i < suffix.Length)
            {
                if (suffix[i] < '0' || suffix[i] > '9')
                {
                    return false;
                }

                i++;
            }

            return true;
        }

        private static bool IsLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>Parses or returns the default instance.</summary>
        public static EngineVersion ParseOrZero(string text)
        {
            EngineVersion version;
            return TryParse(text, out version) ? version : default(EngineVersion);
        }

        /// <summary>Compares the numeric major.minor.patch triple only.</summary>
        public int CompareTo(EngineVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }

        public bool Equals(EngineVersion other)
        {
            return CompareTo(other) == 0;
        }

        public override bool Equals(object obj)
        {
            return obj is EngineVersion && Equals((EngineVersion)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Major;
                hash = (hash * 397) ^ Minor;
                hash = (hash * 397) ^ Patch;
                return hash;
            }
        }

        /// <summary>
        /// Canonical three-part form used in hash inputs so that <c>2022.3</c> and
        /// <c>2022.3.0</c> hash identically.
        /// </summary>
        public string ToCanonicalString()
        {
            return Major.ToString(CultureInfo.InvariantCulture) + "." +
                   Minor.ToString(CultureInfo.InvariantCulture) + "." +
                   Patch.ToString(CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Raw) ? ToCanonicalString() : Raw;
        }

        public static bool operator ==(EngineVersion left, EngineVersion right)
        {
            return left.CompareTo(right) == 0;
        }

        public static bool operator !=(EngineVersion left, EngineVersion right)
        {
            return left.CompareTo(right) != 0;
        }

        public static bool operator <(EngineVersion left, EngineVersion right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(EngineVersion left, EngineVersion right)
        {
            return left.CompareTo(right) > 0;
        }
    }
}
