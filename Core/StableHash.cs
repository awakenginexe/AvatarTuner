using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Deterministic hashing helpers used by the change-tracking subsystem.
    ///
    /// Every hash produced here is a lowercase hex SHA-256 string. Hashes are combined by
    /// feeding a canonical, order-independent textual form into the digest so that the same
    /// logical state always yields the same hash regardless of dictionary iteration order.
    /// </summary>
    public static class StableHash
    {
        /// <summary>Computes the lowercase hex SHA-256 of a UTF-8 string.</summary>
        public static string Sha256Hex(string text)
        {
            return Sha256Hex(Encoding.UTF8.GetBytes(text ?? string.Empty));
        }

        /// <summary>Computes the lowercase hex SHA-256 of a byte range.</summary>
        public static string Sha256Hex(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            return Sha256Hex(data, 0, data.Length);
        }

        /// <summary>Computes the lowercase hex SHA-256 of a byte range.</summary>
        public static string Sha256Hex(byte[] data, int offset, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            if (offset < 0 || count < 0 || offset + count > data.Length)
            {
                throw new ArgumentOutOfRangeException("count", "offset/count range is outside the buffer");
            }

            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data, offset, count));
            }
        }

        /// <summary>
        /// Combines a set of tokens into one digest.
        ///
        /// Tokens are sorted ordinally before hashing so that callers may pass values in any
        /// order (for example, the unordered dependency set of an asset) and still obtain a
        /// stable result. Each token is length-prefixed, which prevents concatenation
        /// ambiguity between tokens such as ["ab","c"] and ["a","bc"].
        /// </summary>
        public static string Combine(IEnumerable<string> tokens)
        {
            if (tokens == null)
            {
                throw new ArgumentNullException("tokens");
            }

            var list = new List<string>(tokens);
            list.Sort(StringComparer.Ordinal);
            return CombineOrdered(list);
        }

        /// <summary>
        /// Combines tokens in the order supplied, without sorting. Use when the order is
        /// itself meaningful and must be reflected in the digest.
        /// </summary>
        public static string CombineOrdered(IEnumerable<string> tokens)
        {
            if (tokens == null)
            {
                throw new ArgumentNullException("tokens");
            }

            var sb = new StringBuilder();
            foreach (var token in tokens)
            {
                var text = token ?? string.Empty;
                sb.Append(text.Length.ToString(CultureInfo.InvariantCulture));
                sb.Append(':');
                sb.Append(text);
                sb.Append(';');
            }

            return Sha256Hex(sb.ToString());
        }

        /// <summary>
        /// Hashes a set of key/value pairs. Keys are sorted first so that insertion order
        /// does not influence the result; values are hashed through their invariant string
        /// representation. Intended for importer-settings and optimization-config snapshots.
        /// </summary>
        public static string OfPairs(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException("pairs");
            }

            var keys = new List<string>();
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                var key = pair.Key ?? string.Empty;
                if (!lookup.ContainsKey(key))
                {
                    keys.Add(key);
                }

                lookup[key] = pair.Value ?? string.Empty;
            }

            keys.Sort(StringComparer.Ordinal);
            var tokens = new List<string>(keys.Count * 2);
            for (var i = 0; i < keys.Count; i++)
            {
                tokens.Add(keys[i]);
                tokens.Add(lookup[keys[i]]);
            }

            return CombineOrdered(tokens);
        }

        /// <summary>Converts a byte array to lowercase hex.</summary>
        public static string ToHex(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }

            var sb = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Normalises arbitrary text into a token that is safe to feed into
        /// <see cref="Combine" />. Used for path tokens so that a path written with
        /// backslashes hashes the same as the same path written with forward slashes.
        /// </summary>
        public static string NormalizePathToken(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            return path.Replace('\\', '/').Trim().ToLowerInvariant();
        }
    }
}
