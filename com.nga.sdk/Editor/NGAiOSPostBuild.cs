// What an iOS build needs so a studio does nothing by hand. Unity copies each installed package's xcframework into the Xcode project and links it into UnityFramework, where
// the bridge lives; this step embeds and signs it in the app, each with its own privacy manifest. Linking NGASDKRender is what lets NGASDK find it by name; a build without network.nga.sdk-render has
// only NGASDK, and LoadAd answers RENDER_NOT_AVAILABLE as on Android.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_IOS
using System.Linq;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
#endif

namespace NGA.Editor
{
    internal sealed class NGAiOSPostBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
#if UNITY_IOS
            var path = PBXProject.GetPBXProjectPath(report.summary.outputPath);
            var project = new PBXProject();
            project.ReadFromFile(path);
            var app = project.GetUnityMainTargetGuid();
            var unity = project.GetUnityFrameworkTargetGuid();
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            foreach (var (package, name) in new[] { (NGABuildChecks.CorePackage, "NGASDK"), (NGABuildChecks.RenderPackage, "NGASDKRender") })
            {
                if (packages.All(p => p.name != package)) continue;
                var relative = $"Frameworks/{package}/Plugins/iOS/{name}.xcframework";
                var file = project.FindFileGuidByProjectPath(relative);
                if (string.IsNullOrEmpty(file)) throw new BuildFailedException($"NGA SDK: {relative} is not in the Xcode project: reinstall {package}.");
                project.AddFileToEmbedFrameworks(app, file);
            }
            // The bridge's one Swift file (Plugins/iOS/NGABridgePurchase.swift), in UnityFramework; the frameworks load from the app.
            project.SetBuildProperty(unity, "SWIFT_VERSION", "5.0");
            project.SetBuildProperty(unity, "CLANG_ENABLE_MODULES", "YES");
            project.AddBuildProperty(unity, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks");
            project.WriteToFile(path);
#endif
        }
    }
}
