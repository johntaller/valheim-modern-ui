using System;
using System.IO;
using Newtonsoft.Json.Linq;
using ValheimNewUI;

static JObject Release(string tag, bool prerelease = false, bool draft = false) => new()
{
    ["tag_name"] = tag, ["prerelease"] = prerelease, ["draft"] = draft,
    ["html_url"] = "https://invalid.example/not-a-release"
};

var passed = 0;
void Expect(string name, string expected, string installed, string skipped, params JToken[] entries)
{
    string actual = UpdateRelease.Find(new JArray(entries), installed, skipped)?.Version;
    if (actual != expected) throw new Exception($"{name}: expected {expected ?? "no update"}, got {actual ?? "no update"}");
    Console.WriteLine($"PASS {name}");
    passed++;
}

Expect("old alpha sees newer alpha", "0.1.1-alpha", "0.1.0-alpha+commit", null, Release("v0.1.1-alpha", true));
Expect("same release or older is silent", null, "0.1.1-alpha+commit", null, Release("v0.1.1-alpha", true), Release("v0.1.0-alpha", true));
Expect("stable skips prereleases and drafts", "1.4.0", "1.2.0", null, Release("v2.0.0-rc.1", true), Release("v3.0.0", draft: true), Release("v1.4.0"));
Expect("stable release outranks matching prerelease", "1.2.0", "1.2.0-rc.1", null, Release("v1.2.0"));
Expect("semver, not release-list order", "1.2.0-rc.10", "1.2.0-alpha", null, Release("v1.2.0-rc.2", true), Release("v1.2.0-rc.10", true), Release("v1.2.0-rc.9", true));
Expect("skip does not fall back to older update", null, "0.1.0-alpha", "0.1.2-alpha", Release("v0.1.2-alpha", true), Release("v0.1.1-alpha", true));
Expect("skip does not suppress next release", "0.1.3-alpha", "0.1.0-alpha", "0.1.2-alpha", Release("v0.1.3-alpha", true));
Expect("invalid tags and entries do not mask valid releases", "1.1.0", "1.0.0", null, JValue.CreateNull(), Release("v9.01.0"), Release("v9.1.0-beta.01", true), Release("v9.1.0+"), Release("v9.1.0<script>"), Release("v1.1.0"));
Expect("invalid installed version is silent", null, "unknown", null, Release("v1.1.0"));
Expect("empty release list is silent", null, "1.0.0", null);
Expect("numerical identifier can exceed Int32", "1.1.0-alpha.10000000000", "1.1.0-alpha.9999999999", null, Release("v1.1.0-alpha.10000000000", true));
Expect("stable rejects a prerelease tag even if GitHub flag is false", null, "1.0.0", null, Release("v1.1.0-rc.1"));

if (!UpdateRelease.HasNextPage("<https://api.github.com/repositories/42/releases?page=2>; rel=\"next\", <https://api.github.com/repositories/42/releases?page=3>; rel=\"last\"") ||
    UpdateRelease.HasNextPage(null) || UpdateRelease.HasNextPage("<https://api.github.com/repositories/42/releases?page=1>; rel=\"prev\""))
    throw new Exception("GitHub pagination must continue exactly when a next page is advertised.");
Console.WriteLine("PASS GitHub next-page detection");
passed++;

var release = UpdateRelease.Find(new JArray(Release("v1.1.0")), "1.0.0", null);
if (release?.Page != "https://github.com/johntaller/valheim-modern-ui/releases/tag/v1.1.0")
    throw new Exception($"Unexpected release URL: {release?.Page}");
Console.WriteLine("PASS fixed repository release link");
passed++;

if (args.Length != 0)
{
    if (args.Length != 2) throw new ArgumentException("Pass a GitHub payload path and its expected release tag, or no arguments.");
    string expected = UpdateRelease.Normalize(args[1]) ?? throw new ArgumentException("Expected release tag is not SemVer.");
    var actual = UpdateRelease.Find(JArray.Parse(File.ReadAllText(args[0])), "0.1.0-alpha", null);
    if (actual?.Version != expected) throw new Exception($"Expected {expected}, got {actual?.Version}");
    Console.WriteLine($"PASS live GitHub payload: {actual.Version}");
    passed++;
}
Console.WriteLine($"{passed} checks passed");
