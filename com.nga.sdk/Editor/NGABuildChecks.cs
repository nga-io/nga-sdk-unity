// The build checks: an Android or iOS build fails before it reaches a device, with a message naming the fix, when
// Minimum API Level is below 23, the iOS target below 15.0, the External Dependency Manager for Unity is not installed,
// network.nga.sdk and network.nga.sdk-render are at different versions or, within a version, carry different native
// builds (within a version, the build id is the pin), with network.nga.sdk-render installed, Render Over Native UI
// is on, or, on Unity below 6, an Android build without minify and without a newer R8 on the Gradle classpath, which
// Unity 2022.3's dexer cannot handle. The check itself is a function of the settings, so each wrong
// setting is tested without a build (Tests/Editor).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace NGA.Editor
{
    internal sealed class NGABuildChecks : IPreprocessBuildWithReport
    {
        internal const int AndroidFloor = 23;
        internal static readonly Version IosFloor = new Version(15, 0);
        internal const string CorePackage = "network.nga.sdk";
        internal const string RenderPackage = "network.nga.sdk-render";

        public int callbackOrder => 0;

        /// <summary>What the check reads; Read() takes it from the project, a test builds its own.</summary>
        internal struct Settings
        {
            internal BuildTarget Target;
            internal int AndroidMinSdk;
            internal string IosTarget;
            internal bool ResolverPresent;
            internal string CoreVersion;
            internal string RenderVersion; // null when network.nga.sdk-render is not installed
            internal string CoreBuild; // the native build id in the package's Plugins/<platform>/SOURCE; null when unknown
            internal string RenderBuild;
            internal bool RenderOverNativeUi;
            internal string UnityVersion; // Application.unityVersion: "2022.3.62f3", "6000.4.9f1"
            internal bool Development; // a development build: Unity minifies it by Minify > Debug
            internal bool Minify; // the Minify setting in force for this build type
            internal bool NewerR8; // an R8 of 8 or later on the Gradle build classpath (a template under Assets/Plugins/Android)
            internal bool ExportOnly; // an exported Gradle project: Unity dexes nothing, the studio's own Gradle does
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            // the build being made decides development or release, as Unity's Gradle build type follows it
            var development = (report.summary.options & BuildOptions.Development) != 0;
            var problems = Check(Read(report.summary.platform, development));
            if (problems.Count > 0)
            {
                throw new BuildFailedException("NGA SDK: this build cannot run the NGA SDK.\n- " + string.Join("\n- ", problems));
            }
        }

        internal static Settings Read(BuildTarget target, bool development)
        {
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            var core = packages.FirstOrDefault(p => p.name == CorePackage);
            var render = packages.FirstOrDefault(p => p.name == RenderPackage);
            return new Settings
            {
                CoreBuild = BuildOf(core?.resolvedPath, target),
                RenderBuild = BuildOf(render?.resolvedPath, target),
                Target = target,
                AndroidMinSdk = (int)PlayerSettings.Android.minSdkVersion,
                IosTarget = PlayerSettings.iOS.targetOSVersionString,
                ResolverPresent = ResolverInstalled(),
                CoreVersion = packages.FirstOrDefault(p => p.name == CorePackage)?.version,
                RenderVersion = packages.FirstOrDefault(p => p.name == RenderPackage)?.version,
                RenderOverNativeUi = PlayerSettings.preserveFramebufferAlpha,
                UnityVersion = UnityEngine.Application.unityVersion,
                Development = development,
                Minify = development ? PlayerSettings.Android.minifyDebug : PlayerSettings.Android.minifyRelease,
                NewerR8 = NewerR8In("Assets/Plugins/Android"),
                ExportOnly = EditorUserBuildSettings.exportAsGoogleAndroidProject,
            };
        }

        /// <summary>
        /// The native build id a package carries for the target: its Plugins/Android/SOURCE or
        /// Plugins/iOS/SOURCE; null when there is none to read.
        /// </summary>
        internal static string BuildOf(string packagePath, BuildTarget target)
        {
            if (string.IsNullOrEmpty(packagePath)) return null;
            var platform = target == BuildTarget.iOS ? "iOS" : "Android";
            try
            {
                var file = Path.Combine(packagePath, "Plugins", platform, "SOURCE");
                return File.Exists(file) ? BuildIdIn(File.ReadAllText(file)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A SOURCE file's build id: its `build:` line, or for packages from before it (0.1.0-rc.2 and earlier) the first
        /// 12 hex digits of its `commit:`, which is the same within one refresh of both packages.
        /// </summary>
        internal static string BuildIdIn(string source)
        {
            string commit = null;
            foreach (var raw in source.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("build:"))
                {
                    // a file with a build: line is this shape: its commit: is not the build id, so a garbled line is no id
                    var id = line.Substring(6).Trim().ToLowerInvariant();
                    return Regex.IsMatch(id, "^[0-9a-f]{12}$") ? id : null;
                }
                if (line.StartsWith("commit:")) commit = line.Substring(7).Trim().ToLowerInvariant();
            }
            return commit != null && commit.Length >= 12 ? commit.Substring(0, 12) : null;
        }

        /// <summary>
        /// Whether a Gradle template in [dir] puts R8 8 or later on the build classpath (`classpath 'com.android.tools:r8:8.7.18'`),
        /// which dexes what Unity 2022.3's own dexer cannot.
        /// </summary>
        internal static bool NewerR8In(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                return Directory.GetFiles(dir, "*.gradle").Any(f => NewerR8Text(File.ReadAllText(f)));
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool NewerR8Text(string gradle) =>
            Regex.Replace(gradle, @"/\*.*?\*/", "", RegexOptions.Singleline)
                .Split('\n')
                .Select(line => line.Split(new[] { "//" }, StringSplitOptions.None)[0])
                .Where(line => line.Contains("classpath"))
                .Any(line => Regex.Matches(line, @"com\.android\.tools:r8:(\d+)\.").Cast<Match>().Any(m => int.Parse(m.Groups[1].Value) >= 8));

        /// <summary>Unity's major version: 2022 for 2022.3.62f3, 6000 for Unity 6; 0 when it does not parse.</summary>
        internal static int UnityMajor(string version) =>
            int.TryParse((version ?? "").Split('.')[0], out var major) ? major : 0;

        /// <summary>EDM4U from a registry or as a .unitypackage under Assets: its resolver type is loaded either way.</summary>
        internal static bool ResolverInstalled() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            {
                try
                {
                    return a.GetType("GooglePlayServices.PlayServicesResolver", false) != null;
                }
                catch (Exception)
                {
                    return false;
                }
            });

        /// <summary>Each problem, with the fix it names; empty when the build may go on.</summary>
        internal static List<string> Check(Settings s)
        {
            var problems = new List<string>();
            var android = s.Target == BuildTarget.Android;
            var ios = s.Target == BuildTarget.iOS;
            if (!android && !ios) return problems;
            if (android && s.AndroidMinSdk > 0 && s.AndroidMinSdk < AndroidFloor)
            {
                problems.Add($"Minimum API Level is {s.AndroidMinSdk}; the NGA SDK needs API {AndroidFloor} or later. " +
                             $"Set Project Settings > Player > Android > Other Settings > Minimum API Level to Android 6.0 (API level {AndroidFloor}).");
            }
            if (ios && (!Version.TryParse(s.IosTarget, out var v) || v < IosFloor))
            {
                problems.Add($"Target minimum iOS Version is {s.IosTarget}; the NGA SDK needs iOS {IosFloor} or later. " +
                             $"Set Project Settings > Player > iOS > Other Settings > Target minimum iOS Version to {IosFloor}.");
            }
            if (android && !s.ResolverPresent)
            {
                problems.Add("The External Dependency Manager for Unity (com.google.external-dependency-manager) is not installed, " +
                             "so the NGA SDK's Android dependencies (kotlin-stdlib among them) would be missing on the device. " +
                             "Add it from OpenUPM or Google's release page, then resolve (Assets > External Dependency Manager > Android Resolver > Resolve).");
            }
            if (s.RenderVersion != null && s.CoreVersion != s.RenderVersion)
            {
                problems.Add($"{CorePackage} is at {s.CoreVersion ?? "(not installed)"} and {RenderPackage} at {s.RenderVersion}; " +
                             $"they must be at one version. Set both to {s.RenderVersion} in Packages/manifest.json.");
            }
            if (s.RenderVersion != null && s.CoreVersion == s.RenderVersion && s.CoreBuild != null && s.RenderBuild != null &&
                s.CoreBuild != s.RenderBuild)
            {
                problems.Add($"{CorePackage} carries native build {s.CoreBuild} and {RenderPackage} build {s.RenderBuild}; " +
                             $"both are {s.CoreVersion}, but two candidates of one version do not work together (the ads would " +
                             "be off). Install both packages from one release: the same tag in both Git URLs in Packages/manifest.json.");
            }
            var major = UnityMajor(s.UnityVersion);
            if (android && !s.ExportOnly && major > 0 && major < 6000 && !s.Minify && !s.NewerR8)
            {
                var setting = s.Development ? "Debug" : "Release";
                problems.Add($"Unity {s.UnityVersion} builds this Android {(s.Development ? "development" : "release")} build without minify, " +
                             "and its own dexer cannot dex the Kotlin 1.9 standard library the NGA SDK uses: the build would fail at " +
                             $"dexing. Turn on Project Settings > Player > Android > Publishing Settings > Minify > {setting}, " +
                             "or build with Unity 6.");
            }
            if (s.RenderVersion != null && s.RenderOverNativeUi)
            {
                problems.Add("Render Over Native UI is on, so the game's surface would cover the NGA cross-promo banner while its " +
                             "impression still counts. Turn it off: Project Settings > Player > Resolution and Presentation > Render Over Native UI.");
            }
            return problems;
        }
    }
}
