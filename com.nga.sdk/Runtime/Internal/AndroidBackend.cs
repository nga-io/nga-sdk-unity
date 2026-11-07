// The Android player's backend: NGA's Android library through JNI. Native callbacks arrive on Android's main thread
// at AndroidJavaProxy trampolines marked [Preserve], which copy the native values into plain C# values and only enqueue
// (Dispatcher): the studio's code never runs on the native-to-managed frame. Compiled on every platform so the Editor
// type-checks it; created only in an Android player (Backends).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace NGA.Internal
{
    internal sealed class AndroidBackend : IBackend
    {
        const string Package = "network.nga.sdk.";
        AndroidJavaClass sdk;
        ImpressionProxy impressionProxy;

        AndroidJavaClass Sdk
        {
            get
            {
                Jni.Attach();
                return sdk ?? (sdk = new AndroidJavaClass(Package + "NGASDK"));
            }
        }

        static AndroidJavaClass unityBridge;

        /// <summary>
        /// What the C# layer tells native, and nothing a studio calls: the host is Unity, and each <c>OnRewardResult</c> that has run on the game thread.
        /// </summary>
        internal static AndroidJavaClass UnityBridge
        {
            get
            {
                Jni.Attach();
                return unityBridge ?? (unityBridge = new AndroidJavaClass(Package + "NGAUnityBridge"));
            }
        }

        public void Initialize(NGAConfig config, NGAListener listener)
        {
            var jConfig = config == null ? null : JavaValues.Config(config);
            // Before init, so native keeps a granted reward's record until C# reports its OnRewardResult ran.
            UnityBridge.CallStatic("hostIsUnity");
            // C# Initialize takes no context; the plugin passes Unity's current Activity.
            Sdk.CallStatic("initialize", Jni.CurrentActivity(), jConfig, listener == null ? null : new ListenerProxy(listener));
        }

        public string SdkVersion() => Sdk.CallStatic<string>("getSdkVersion");

        public void SetLogLevel(NGALogLevel level) => Sdk.CallStatic("setLogLevel", JavaEnums.ToJava(level));

        public void SetConsent(NGAConsent consent) => Sdk.CallStatic("setConsent", JavaValues.Consent(consent));

        public NGAConsent GetConsent() => CsValues.Consent(Sdk.CallStatic<AndroidJavaObject>("getConsent"));

        public void SetAge(NGAAgeState age) => Sdk.CallStatic("setAge", JavaEnums.ToJava(age));

        public NGAAgeState GetAge() => (NGAAgeState)JavaEnums.Value(Sdk.CallStatic<AndroidJavaObject>("getAge"));

        public NGAResult SetCustomerUserId(string id) => CsValues.Result(Sdk.CallStatic<AndroidJavaObject>("setCustomerUserId", id));

        public NGAResult LogAdRevenue(NGAAdRevenue revenue)
        {
            // An unnamed enum integer is answered here, as native answers a null.
            var unnamed = revenue == null ? null : Validation.AdRevenue(revenue);
            if (unnamed != null && unnamed.Detail == Errors.ArgumentNull)
            {
                Debug.LogWarning("[NGA] LogAdRevenue: " + unnamed.Message);
                return new NGAResult(false, unnamed);
            }
            return CsValues.Result(Sdk.CallStatic<AndroidJavaObject>("logAdRevenue", revenue == null ? null : JavaValues.AdRevenue(revenue)));
        }

        /// <summary>
        /// On Android every purchase reaches the Play Billing hook, so the call records nothing and
        /// answers <c>accepted = true</c>; null is <c>argument.null</c> with one WARN.
        /// </summary>
        public NGAResult LogPurchase(string jws)
        {
            if (jws != null) return new NGAResult(true, null);
            var error = Validation.PurchaseJws(null);
            Debug.LogWarning("[NGA] LogPurchase: " + error.Message);
            return new NGAResult(false, error);
        }

        public void LoadAd(string placementId, NGAAdLoadCallback callback) =>
            Sdk.CallStatic("loadAd", placementId, callback == null ? null : LoadProxy.For(callback));

        public void SetImpressionListener(Action<NGAImpression> listener)
        {
            impressionProxy = listener == null ? null : new ImpressionProxy(listener);
            Sdk.CallStatic("setImpressionListener", impressionProxy);
        }

        public void SetMuted(bool muted) => Sdk.CallStatic("setMuted", muted);

        public bool IsMuted() => Sdk.CallStatic<bool>("isMuted");

        public NGAAdState State(NGAAd ad)
        {
            Jni.Attach();
            return (NGAAdState)JavaEnums.Value(((AndroidJavaObject)ad.Native).Call<AndroidJavaObject>("getState"));
        }

        public bool IsReady(NGAAd ad)
        {
            Jni.Attach();
            return ((AndroidJavaObject)ad.Native).Call<bool>("isReady");
        }

        public void Show(NGAAd ad, NGAAdShowListener listener)
        {
            Jni.Attach();
            // C# Show takes no presenter; the plugin shows from Unity's current Activity.
            ((AndroidJavaObject)ad.Native).Call("show", Jni.CurrentActivity(), new ShowProxy(ad, listener));
        }

        public void Close(NGAAd ad)
        {
            Jni.Attach();
            ((AndroidJavaObject)ad.Native).Call("close");
        }

        // --- the trampolines: copy, enqueue, return

        [Preserve]
        sealed class ListenerProxy : AndroidJavaProxy
        {
            readonly NGAListener target;

            public ListenerProxy(NGAListener target) : base(Package + "NGAListener") => this.target = target;

            [Preserve]
            void onInitialized(AndroidJavaObject result) => Jni.Guard("onInitialized", () =>
            {
                var r = CsValues.InitResult(result);
                Dispatcher.Post(() => target.OnInitialized(r));
            });

            [Preserve]
            void onError(AndroidJavaObject error) => Jni.Guard("onError", () =>
            {
                var e = CsValues.Error(error);
                Dispatcher.Post(() => target.OnError(e));
            });

            [Preserve]
            void onAdClosedAfterRestart(string placementId, string opportunityId, bool rewardGranted) => Jni.Guard("onAdClosedAfterRestart",
                () => Dispatcher.Post(() => target.OnAdClosedAfterRestart(placementId, opportunityId, rewardGranted)));
        }

        /// <summary>One proxy per C# callback object for as long as the object lives, so the native side sees one
        /// registration for it on every placement (in C# a callback's identity is the managed object). The table holds the callback weakly and is never pruned by hand: one placement's failure or
        /// expiry leaves the callback registered on the others.</summary>
        [Preserve]
        sealed class LoadProxy : AndroidJavaProxy
        {
            static readonly ConditionalWeakTable<NGAAdLoadCallback, LoadProxy> Proxies =
                new ConditionalWeakTable<NGAAdLoadCallback, LoadProxy>();

            readonly NGAAdLoadCallback target;

            LoadProxy(NGAAdLoadCallback target) : base(Package + "NGAAdLoadCallback") => this.target = target;

            internal static LoadProxy For(NGAAdLoadCallback callback) => Proxies.GetValue(callback, c => new LoadProxy(c));

            [Preserve]
            void onLoaded(AndroidJavaObject ad) => Jni.Guard("onLoaded", () =>
            {
                var w = CsValues.Ad(ad);
                Dispatcher.Post(() => target.OnLoaded(w));
            });

            [Preserve]
            void onFailed(string placementId, AndroidJavaObject error) => Jni.Guard("onFailed", () =>
            {
                var e = CsValues.Error(error);
                Dispatcher.Post(() => target.OnFailed(placementId, e));
            });

            [Preserve]
            void onExpired(AndroidJavaObject ad) => Jni.Guard("onExpired", () =>
            {
                var w = CsValues.Ad(ad);
                AdRegistry.Release(w.OpportunityId);
                Dispatcher.Post(() => target.OnExpired(w));
            });
        }

        [Preserve]
        internal sealed class ShowProxy : AndroidJavaProxy
        {
            readonly NGAAd ad;
            readonly NGAAdShowListener target;

            public ShowProxy(NGAAd ad, NGAAdShowListener target) : base(Package + "NGAAdShowListener")
            {
                this.ad = ad;
                this.target = target;
            }

            void Post(Action<NGAAdShowListener> call)
            {
                if (target == null) return;
                var t = target;
                Dispatcher.Post(() => call(t));
            }

            [Preserve]
            void onShowStarted(AndroidJavaObject jAd) => Jni.Guard("onShowStarted", () => Post(t => t.OnShowStarted(ad)));

            [Preserve]
            void onClicked(AndroidJavaObject jAd) => Jni.Guard("onClicked", () => Post(t => t.OnClicked(ad)));

            [Preserve]
            void onRewardResult(AndroidJavaObject jAd, AndroidJavaObject result) => Jni.Guard("onRewardResult", () =>
            {
                var r = CsValues.Reward(result);
                var opportunityId = ad.OpportunityId;
                Post(t =>
                {
                    try
                    {
                        t.OnRewardResult(ad, r);
                    }
                    finally
                    {
                        // The callback ran on the game thread; native may drop the show's record.
                        UnityBridge.CallStatic("rewardResultRan", opportunityId);
                    }
                });
            });

            [Preserve]
            void onCompleted(AndroidJavaObject jAd, AndroidJavaObject completion) => Jni.Guard("onCompleted", () =>
            {
                var c = (NGAShowCompletion)JavaEnums.Value(completion);
                Post(t => t.OnCompleted(ad, c));
            });

            [Preserve]
            void onClosed(AndroidJavaObject jAd) => Jni.Guard("onClosed", () =>
            {
                AdRegistry.Release(ad.OpportunityId);
                Post(t => t.OnClosed(ad));
            });

            [Preserve]
            void onShowFailed(AndroidJavaObject jAd, AndroidJavaObject error) => Jni.Guard("onShowFailed", () =>
            {
                var e = CsValues.Error(error);
                // A refused show leaves the handle READY (or SHOWING for same_handle): not the wrapper's last callback.
                var state = (NGAAdState)JavaEnums.Value(jAd?.Call<AndroidJavaObject>("getState"));
                if (state != NGAAdState.Ready && state != NGAAdState.Showing) AdRegistry.Release(ad.OpportunityId);
                Post(t => t.OnShowFailed(ad, e));
            });
        }

        [Preserve]
        sealed class ImpressionProxy : AndroidJavaProxy
        {
            readonly Action<NGAImpression> target;

            public ImpressionProxy(Action<NGAImpression> target) : base(Package + "NGAImpressionListener") => this.target = target;

            [Preserve]
            void onImpression(AndroidJavaObject impression) => Jni.Guard("onImpression", () =>
            {
                var i = CsValues.Impression(impression);
                Dispatcher.Post(() => target(i));
            });
        }
    }

    internal static class Jni
    {
        /// <summary>JNI from a thread Unity did not start needs the thread attached; harmless when it already is.</summary>
        internal static void Attach()
        {
            if (!Dispatcher.OnGameThread) AndroidJNI.AttachCurrentThread();
        }

        internal static AndroidJavaObject CurrentActivity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                return player.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }

        /// <summary>
        /// C# run by Java (a runnable on Android's main looper, a proxy trampoline in native's hand-off) never throws back:
        /// Unity rethrows to the Java caller, where it would end the process on the main thread. Logged instead.
        /// </summary>
        internal static void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError("[NGA] " + what + ": " + e);
            }
        }
    }

    /// <summary>
    /// The one table between C# and Java enums: the integer crosses, never the declaration order. Java's
    /// constant for a C# value is found by its <c>getValue()</c>.
    /// </summary>
    internal static class JavaEnums
    {
        internal static readonly Dictionary<Type, string> JavaClass = new Dictionary<Type, string>
        {
            { typeof(NGAErrorCode), "network.nga.sdk.NGAErrorCode" },
            { typeof(NGAConsentState), "network.nga.sdk.NGAConsentState" },
            { typeof(NGAAgeState), "network.nga.sdk.NGAAgeState" },
            { typeof(NGALogLevel), "network.nga.sdk.NGALogLevel" },
            { typeof(NGAAdRevenueMediation), "network.nga.sdk.NGAAdRevenueMediation" },
            { typeof(NGARevenuePrecision), "network.nga.sdk.NGARevenuePrecision" },
            { typeof(NGAAdFormat), "network.nga.sdk.NGAAdFormat" },
            { typeof(NGAAdState), "network.nga.sdk.NGAAdState" },
            { typeof(NGAShowCompletion), "network.nga.sdk.NGAShowCompletion" },
            { typeof(NGARewardDenial), "network.nga.sdk.NGARewardDenial" },
        };

        static readonly Dictionary<string, AndroidJavaObject[]> Constants = new Dictionary<string, AndroidJavaObject[]>();

        internal static AndroidJavaObject ToJava<T>(T value) where T : Enum
        {
            var javaClass = JavaClass[typeof(T)];
            var number = Convert.ToInt32(value);
            AndroidJavaObject[] all;
            lock (Constants)
            {
                if (!Constants.TryGetValue(javaClass, out all))
                {
                    using (var c = new AndroidJavaClass(javaClass)) all = c.CallStatic<AndroidJavaObject[]>("values");
                    Constants[javaClass] = all;
                }
            }
            foreach (var constant in all)
            {
                if (constant.Call<int>("getValue") == number) return constant;
            }
            return null; // NGARewardDenial.None has no Java constant: Kotlin uses null
        }

        /// <summary>The integer of a Java enum constant; 0 for null (0 is the default).</summary>
        internal static int Value(AndroidJavaObject constant) => constant == null ? 0 : constant.Call<int>("getValue");
    }

    /// <summary>C# inputs as Java objects.</summary>
    internal static class JavaValues
    {
        /// <summary>null for null, and for a consent carrying an unnamed state, which counts as none.</summary>
        internal static AndroidJavaObject Consent(NGAConsent c) =>
            !Validation.Named(c)
                ? null
                : new AndroidJavaObject("network.nga.sdk.NGAConsent", JavaEnums.ToJava(c.Analytics), JavaEnums.ToJava(c.AdsPersonalization));

        internal static AndroidJavaObject Config(NGAConfig config)
        {
            using (var builder = new AndroidJavaObject("network.nga.sdk.NGAConfig$Builder", config.AppKey,
                       Consent(config.Consent)))
            {
                builder.Call<AndroidJavaObject>("testMode", config.TestModeEnabled).Dispose();
                return builder.Call<AndroidJavaObject>("build");
            }
        }

        internal static AndroidJavaObject AdRevenue(NGAAdRevenue r)
        {
            var b = new AndroidJavaObject("network.nga.sdk.NGAAdRevenue$Builder", JavaEnums.ToJava(r.Mediation), r.RevenueMicros,
                r.Currency, JavaEnums.ToJava(r.Precision));
            void Set(string method, object value)
            {
                if (value != null) b.Call<AndroidJavaObject>(method, value).Dispose();
            }
            Set("network", r.Network);
            Set("adUnitId", r.AdUnitId);
            if (r.AdFormat != NGAAdFormat.Unknown) Set("adFormat", JavaEnums.ToJava(r.AdFormat));
            Set("placement", r.Placement);
            Set("networkPlacement", r.NetworkPlacement);
            Set("creativeId", r.CreativeId);
            Set("advertiserDomain", r.AdvertiserDomain);
            Set("impressionId", r.ImpressionId);
            var built = b.Call<AndroidJavaObject>("build");
            b.Dispose();
            return built;
        }
    }

    /// <summary>Java values as plain C# ones, on the thread that receives them, before they are enqueued.</summary>
    internal static class CsValues
    {
        internal static NGAError Error(AndroidJavaObject e)
        {
            if (e == null) return null;
            long? retryAfter = null;
            using (var boxed = e.Call<AndroidJavaObject>("getRetryAfterMillis"))
            {
                if (boxed != null) retryAfter = boxed.Call<long>("longValue");
            }
            return new NGAError(
                (NGAErrorCode)JavaEnums.Value(e.Call<AndroidJavaObject>("getCode")),
                e.Call<string>("getDetail"),
                e.Call<string>("getMessage"),
                e.Call<bool>("getRetryable"),
                retryAfter);
        }

        internal static NGAResult Result(AndroidJavaObject r) =>
            r == null ? null : new NGAResult(r.Call<bool>("getAccepted"), Error(r.Call<AndroidJavaObject>("getError")));

        internal static NGAInitResult InitResult(AndroidJavaObject r) =>
            new NGAInitResult(r.Call<bool>("getSuccess"), Error(r.Call<AndroidJavaObject>("getError")), r.Call<bool>("getFromCache"));

        internal static NGAConsent Consent(AndroidJavaObject c) =>
            c == null
                ? NGAConsent.Unknown()
                : new NGAConsent(
                    (NGAConsentState)JavaEnums.Value(c.Call<AndroidJavaObject>("getAnalytics")),
                    (NGAConsentState)JavaEnums.Value(c.Call<AndroidJavaObject>("getAdsPersonalization")));

        internal static NGARewardResult Reward(AndroidJavaObject r) =>
            new NGARewardResult(r.Call<bool>("getGranted"), (NGARewardDenial)JavaEnums.Value(r.Call<AndroidJavaObject>("getDenial")));

        internal static NGAImpression Impression(AndroidJavaObject i) =>
            new NGAImpression(
                i.Call<string>("getPlacementId"),
                i.Call<string>("getOpportunityId"),
                (NGAAdFormat)JavaEnums.Value(i.Call<AndroidJavaObject>("getFormat")),
                i.Call<long>("getRevenueMicros"),
                i.Call<string>("getCurrency"),
                (NGARevenuePrecision)JavaEnums.Value(i.Call<AndroidJavaObject>("getPrecision")),
                i.Call<string>("getCampaignId"),
                i.Call<string>("getCreativeId"),
                i.Call<string>("getAdvertiserBundle"),
                i.Call<string>("getAdvertiserDomain"));

        /// <summary>The one wrapper for a native handle; it keeps its own reference to it.</summary>
        internal static NGAAd Ad(AndroidJavaObject ad)
        {
            var opportunityId = ad.Call<string>("getOpportunityId");
            return AdRegistry.For(opportunityId, () => new NGAAd(
                ad.Call<string>("getPlacementId"),
                opportunityId,
                ad.Call<long>("getExpiresAtMillis"),
                new AndroidJavaObject(ad.GetRawObject())));
        }
    }
}
