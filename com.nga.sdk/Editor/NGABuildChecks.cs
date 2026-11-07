// The build checks: an Android or iOS build fails before it reaches a device, with a message naming the fix, when
// Minimum API Level is below 23, the iOS target below 15.0, the External Dependency Manager for Unity is not installed,
// network.nga.sdk and network.nga.sdk-render are at different versions, or, with network.nga.sdk-render installed,
// Render Over Native UI is on. The check itself is a function of the settings, so each wrong
// setting is tested without a build (Tests/Editor).

using System;
using System.Collections.Generic;
using System.Linq;
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
            internal bool RenderOverNativeUi;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = Check(Read(report.summary.platform));
            if (problems.Count > 0)
            {
                throw new BuildFailedException("NGA SDK: this build cannot run the NGA SDK.\n- " + string.Join("\n- ", problems));
            }
        }

        internal static Settings Read(BuildTarget target)
        {
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            return new Settings
            {
                Target = target,
                AndroidMinSdk = (int)PlayerSettings.Android.minSdkVersion,
                IosTarget = PlayerSettings.iOS.targetOSVersionString,
                ResolverPresent = ResolverInstalled(),
                CoreVersion = packages.FirstOrDefault(p => p.name == CorePackage)?.version,
                RenderVersion = packages.FirstOrDefault(p => p.name == RenderPackage)?.version,
                RenderOverNativeUi = PlayerSettings.preserveFramebufferAlpha,
            };
        }

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
            if (s.RenderVersion != null && s.RenderOverNativeUi)
            {
                problems.Add("Render Over Native UI is on, so the game's surface would cover the NGA cross-promo banner while its " +
                             "impression still counts. Turn it off: Project Settings > Player > Resolution and Presentation > Render Over Native UI.");
            }
            return problems;
        }
    }
}
