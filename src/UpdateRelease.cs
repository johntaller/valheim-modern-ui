using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ValheimNewUI;

internal sealed class UpdateRelease
{
    private const string ReleasePages = "https://github.com/johntaller/valheim-modern-ui/releases/tag/";
    private static readonly Regex TagPattern = new(
        @"\A[vV]?(?<version>(?<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?)(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");

    internal string Version { get; }
    internal string Page { get; }

    private UpdateRelease(VersionTag tag)
    {
        Version = tag.Version;
        Page = ReleasePages + Uri.EscapeDataString(tag.Tag);
    }

    internal static bool HasNextPage(string links) => links?.IndexOf("rel=\"next\"", StringComparison.Ordinal) >= 0;

    internal static UpdateRelease Find(IEnumerable<JToken> releases, string installed, string skipped)
    {
        var current = Parse(installed);
        if (current == null) return null;
        VersionTag best = null;
        foreach (var entry in releases)
        {
            if (entry is not JObject release || release["draft"] is not JValue { Value: false } ||
                release["tag_name"] is not JValue { Value: string raw }) continue;
            var tag = Parse(raw);
            if (tag == null || (current.Pre.Length == 0 &&
                (tag.Pre.Length != 0 || release["prerelease"] is not JValue { Value: false }))) continue;
            if (best == null || Compare(tag, best) > 0) best = tag;
        }
        if (best == null || Compare(best, current) <= 0 || best.Version == Parse(skipped)?.Version) return null;
        return new UpdateRelease(best);
    }

    internal static string Normalize(string version) => Parse(version)?.Version;

    private static VersionTag Parse(string value)
    {
        if (value == null || value.Length > 256) return null;
        string tag = value.Trim();
        var match = TagPattern.Match(tag);
        if (!match.Success) return null;
        var preGroup = match.Groups["pre"];
        string[] pre = preGroup.Success ? preGroup.Value.Split('.') : Array.Empty<string>();
        foreach (string part in pre)
            if (part.Length > 1 && part[0] == '0' && IsNumber(part)) return null;
        return new VersionTag(tag, match.Groups["version"].Value, match.Groups["core"].Value.Split('.'), pre);
    }

    private static bool IsNumber(string part)
    {
        foreach (char c in part)
            if (c < '0' || c > '9') return false;
        return true;
    }

    private static int CompareNumbers(string left, string right) => left.Length != right.Length
        ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);

    private static int Compare(VersionTag left, VersionTag right)
    {
        for (int i = 0; i < left.Core.Length; i++)
        {
            int order = CompareNumbers(left.Core[i], right.Core[i]);
            if (order != 0) return order;
        }
        if (left.Pre.Length == 0 || right.Pre.Length == 0)
            return right.Pre.Length.CompareTo(left.Pre.Length);
        for (int i = 0; i < Math.Min(left.Pre.Length, right.Pre.Length); i++)
        {
            string a = left.Pre[i], b = right.Pre[i];
            bool numberA = IsNumber(a), numberB = IsNumber(b);
            int order = numberA && numberB ? CompareNumbers(a, b)
                : numberA ? -1 : numberB ? 1 : string.CompareOrdinal(a, b);
            if (order != 0) return order;
        }
        return left.Pre.Length.CompareTo(right.Pre.Length);
    }

    private sealed class VersionTag
    {
        internal readonly string Tag, Version;
        internal readonly string[] Core, Pre;

        internal VersionTag(string tag, string version, string[] core, string[] pre)
        {
            Tag = tag;
            Version = version;
            Core = core;
            Pre = pre;
        }
    }
}
