// The iOS player's backend: NGA's iOS framework through Plugins/iOS/NGABridge.mm. Native callbacks arrive on
// the main thread, Unity's game thread, at one static [MonoPInvokeCallback] trampoline, which copies the text into plain C#
// values and only enqueues (Dispatcher), so the studio's code never runs on the native-to-managed frame, even there.
// Values cross as text: fields joined by \x1f, a nil field \x1e. Compiled on every platform so the Editor
// type-checks it; created only in an iOS player (Backends).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;

namespace NGA.Internal
{
    internal sealed class IOSBackend : IBackend
    {
        // IL2CPP links __Internal statically on every target: another name is looked up at the first call, which never comes.
#if UNITY_IOS
        const string Lib = "__Internal";
#else
        const string Lib = "nga_bridge";
#endif

        delegate void Callback(long token, int kind, IntPtr text);

        [DllImport(Lib)] static extern void nga_bridge_set_callback(Callback callback);
        [DllImport(Lib)] static extern void nga_initialize(int hasConfig, string appKey, int analytics, int ads, int testMode, long listener);
        [DllImport(Lib)] static extern string nga_sdk_version();
        [DllImport(Lib)] static extern void nga_set_log_level(int level);
        [DllImport(Lib)] static extern void nga_set_consent(int analytics, int ads);
        [DllImport(Lib)] static extern string nga_get_consent();
        [DllImport(Lib)] static extern void nga_set_age(int age);
        [DllImport(Lib)] static extern int nga_get_age();
        [DllImport(Lib)] static extern string nga_set_customer_user_id(string id);
        [DllImport(Lib)] static extern string nga_log_ad_revenue(int hasRevenue, int mediation, long micros, string currency, int precision,
            string network, string adUnitId, int adFormat, string placement, string networkPlacement, string creativeId, string advertiserDomain,
            string impressionId);
        [DllImport(Lib)] static extern string nga_log_purchase(string jws);
        [DllImport(Lib)] static extern void nga_load_ad(string placementId, long callback);
        [DllImport(Lib)] static extern void nga_set_impression_listener(int on);
        [DllImport(Lib)] static extern void nga_set_muted(int muted);
        [DllImport(Lib)] static extern int nga_is_muted();
        [DllImport(Lib)] static extern int nga_ad_state(long handle);
        [DllImport(Lib)] static extern int nga_ad_is_ready(long handle);
        [DllImport(Lib)] static extern void nga_ad_show(long handle, long listener);
        [DllImport(Lib)] static extern void nga_ad_close(long handle);
        [DllImport(Lib)] internal static extern void nga_ad_release(long handle);

        // The bridge's kinds (NGABridge.mm).
        const int Initialized = 0, Error = 1, ClosedAfterRestart = 2, Loaded = 3, LoadFailed = 4, Expired = 5, ShowStarted = 6, Clicked = 7,
            Reward = 8, Completed = 9, Closed = 10, ShowFailed = 11, Impression = 12, Release = 13;

        const char Separator = '\x1f';
        const string Nil = "\x1e";

        static readonly Callback Trampoline = Receive; // held, so the pointer native keeps stays valid
        static Action<NGAImpression> impressionListener;

        internal IOSBackend() => nga_bridge_set_callback(Trampoline);

        public void Initialize(NGAConfig config, NGAListener listener)
        {
            var (analytics, ads) = Consent(config?.Consent);
            // C# Initialize takes no context. A null config and an unset listener cross as nil.
            nga_initialize(config == null ? 0 : 1, config?.AppKey, analytics, ads, config != null && config.TestModeEnabled ? 1 : 0,
                listener == null ? 0 : Tokens.Own(listener));
        }

        public string SdkVersion() => nga_sdk_version();

        public void SetLogLevel(NGALogLevel level)
        {
            // An unnamed enum integer counts as a null of that argument: one ERROR, nothing done.
            if (!Validation.Named(level)) Debug.LogError("[NGA] SetLogLevel ignored: level is null");
            else nga_set_log_level((int)level);
        }

        public void SetConsent(NGAConsent consent)
        {
            var (analytics, ads) = Consent(consent);
            nga_set_consent(analytics, ads);
        }

        public NGAConsent GetConsent()
        {
            var f = nga_get_consent().Split(Separator);
            return new NGAConsent((NGAConsentState)Int(f[0]), (NGAConsentState)Int(f[1]));
        }

        public void SetAge(NGAAgeState age)
        {
            if (!Validation.Named(age)) Debug.LogError("[NGA] SetAge ignored: age is null");
            else nga_set_age((int)age);
        }

        public NGAAgeState GetAge() => (NGAAgeState)nga_get_age();

        public NGAResult SetCustomerUserId(string id) => Result(nga_set_customer_user_id(id));

        public NGAResult LogAdRevenue(NGAAdRevenue r)
        {
            // An unnamed enum integer is answered here, as native answers a null.
            var unnamed = r == null ? null : Validation.AdRevenue(r);
            if (unnamed != null && unnamed.Detail == Errors.ArgumentNull)
            {
                Debug.LogWarning("[NGA] LogAdRevenue: " + unnamed.Message);
                return new NGAResult(false, unnamed);
            }
            return Result(r == null
                ? nga_log_ad_revenue(0, 0, 0, null, 0, null, null, 0, null, null, null, null, null)
                : nga_log_ad_revenue(1, (int)r.Mediation, r.RevenueMicros, r.Currency, (int)r.Precision, r.Network, r.AdUnitId, (int)r.AdFormat,
                    r.Placement, r.NetworkPlacement, r.CreativeId, r.AdvertiserDomain, r.ImpressionId));
        }

        /// <summary>The order's JWS, recorded by native; null is <c>argument.null</c> there too.</summary>
        public NGAResult LogPurchase(string jws) => Result(nga_log_purchase(jws));

        public void LoadAd(string placementId, NGAAdLoadCallback callback) =>
            nga_load_ad(placementId, callback == null ? 0 : Tokens.Of(callback));

        public void SetImpressionListener(Action<NGAImpression> listener)
        {
            impressionListener = listener;
            nga_set_impression_listener(listener == null ? 0 : 1);
        }

        public void SetMuted(bool muted) => nga_set_muted(muted ? 1 : 0);

        public bool IsMuted() => nga_is_muted() != 0;

        public NGAAdState State(NGAAd ad) => (NGAAdState)nga_ad_state(((IOSHandle)ad.Native).Id);

        public bool IsReady(NGAAd ad) => nga_ad_is_ready(((IOSHandle)ad.Native).Id) != 0;

        /// <summary>C# Show takes no presenter; the bridge shows from Unity's root view controller.</summary>
        public void Show(NGAAd ad, NGAAdShowListener listener) =>
            nga_ad_show(((IOSHandle)ad.Native).Id, Tokens.Own(new ShowTarget(ad, listener)));

        public void Close(NGAAd ad) => nga_ad_close(((IOSHandle)ad.Native).Id);

        static (int analytics, int ads) Consent(NGAConsent c) => Validation.Named(c) ? ((int)c.Analytics, (int)c.AdsPersonalization) : (-1, -1);

        /// <summary>The one trampoline: copy, enqueue, return. It never throws back into native.</summary>
        [MonoPInvokeCallback(typeof(Callback))]
        static void Receive(long token, int kind, IntPtr text)
        {
            try
            {
                var fields = Marshal.PtrToStringUTF8(text).Split(Separator);
                if (kind == Release) Tokens.Release(token);
                else if (kind == Impression) OnImpression(fields);
                else if (kind <= ClosedAfterRestart) OnListener((NGAListener)Tokens.Get(token), kind, fields);
                else if (kind <= Expired) OnLoad((NGAAdLoadCallback)Tokens.Get(token), kind, fields);
                else OnShow((ShowTarget)Tokens.Get(token), kind, fields);
            }
            catch (Exception e)
            {
                Debug.LogError("[NGA] a native callback (" + kind + "): " + e);
            }
        }

        static void OnListener(NGAListener target, int kind, string[] f)
        {
            if (target == null) return;
            switch (kind)
            {
                case Initialized:
                    var result = new NGAInitResult(f[0] == "1", Err(f, 2), f[1] == "1");
                    Dispatcher.Post(() => target.OnInitialized(result));
                    break;
                case Error:
                    var error = Err(f, 0);
                    Dispatcher.Post(() => target.OnError(error));
                    break;
                case ClosedAfterRestart:
                    Dispatcher.Post(() => target.OnAdClosedAfterRestart(f[0], f[1], f[2] == "1"));
                    break;
            }
        }

        static void OnLoad(NGAAdLoadCallback target, int kind, string[] f)
        {
            if (target == null) return;
            switch (kind)
            {
                case Loaded:
                    var loaded = Ad(f);
                    Dispatcher.Post(() => target.OnLoaded(loaded));
                    break;
                case LoadFailed:
                    var error = Err(f, 1);
                    Dispatcher.Post(() => target.OnFailed(f[0], error));
                    break;
                case Expired:
                    var expired = Ad(f);
                    AdRegistry.Release(expired.OpportunityId);
                    Dispatcher.Post(() => target.OnExpired(expired));
                    break;
            }
        }

        static void OnShow(ShowTarget show, int kind, string[] f)
        {
            if (show == null) return;
            var ad = show.Ad;
            var target = show.Target;
            switch (kind)
            {
                case ShowStarted: Post(target, t => t.OnShowStarted(ad)); break;
                case Clicked: Post(target, t => t.OnClicked(ad)); break;
                case Reward:
                    var reward = new NGARewardResult(f[0] == "1", (NGARewardDenial)Int(f[1]));
                    Post(target, t => t.OnRewardResult(ad, reward));
                    break;
                case Completed:
                    var completion = (NGAShowCompletion)Int(f[0]);
                    Post(target, t => t.OnCompleted(ad, completion));
                    break;
                case Closed:
                    AdRegistry.Release(ad.OpportunityId);
                    Post(target, t => t.OnClosed(ad));
                    break;
                case ShowFailed:
                    // A refused show leaves the handle READY (or SHOWING for same_handle): not the wrapper's last callback.
                    var state = (NGAAdState)Int(f[0]);
                    if (state != NGAAdState.Ready && state != NGAAdState.Showing) AdRegistry.Release(ad.OpportunityId);
                    var error = Err(f, 1);
                    Post(target, t => t.OnShowFailed(ad, error));
                    break;
            }
        }

        static void Post(NGAAdShowListener target, Action<NGAAdShowListener> call)
        {
            if (target != null) Dispatcher.Post(() => call(target));
        }

        static void OnImpression(string[] f)
        {
            var listener = impressionListener;
            if (listener == null) return;
            var i = new NGAImpression(f[0], f[1], (NGAAdFormat)Int(f[2]), Long(f[3]), f[4], (NGARevenuePrecision)Int(f[5]), Str(f[6]), Str(f[7]), Str(f[8]),
                Str(f[9]));
            Dispatcher.Post(() => listener(i));
        }

        /// <summary>The one wrapper for a native handle: each delivery carries a handle id, and
        /// one the wrapper does not take is let go at once.</summary>
        static NGAAd Ad(string[] f)
        {
            var id = Long(f[0]);
            var ad = AdRegistry.For(f[2], () => new NGAAd(f[1], f[2], Long(f[3]), new IOSHandle(id)));
            if (((IOSHandle)ad.Native).Id != id) nga_ad_release(id);
            return ad;
        }

        static NGAResult Result(string text)
        {
            var f = text.Split(Separator);
            return new NGAResult(f[0] == "1", Err(f, 1));
        }

        static NGAError Err(string[] f, int at) =>
            f[at] == Nil ? null : new NGAError((NGAErrorCode)Int(f[at]), f[at + 1], f[at + 2], f[at + 3] == "1", f[at + 4] == Nil ? (long?)null : Long(f[at + 4]));

        static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        static long Long(string s) => long.Parse(s, CultureInfo.InvariantCulture);
        static string Str(string s) => s == Nil ? null : s;

        sealed class ShowTarget
        {
            internal readonly NGAAd Ad;
            internal readonly NGAAdShowListener Target;

            internal ShowTarget(NGAAd ad, NGAAdShowListener target)
            {
                Ad = ad;
                Target = target;
            }
        }
    }

    /// <summary>A native handle's id, let go when the C# wrapper is collected: its state stays readable until then.</summary>
    internal sealed class IOSHandle
    {
        internal readonly long Id;

        internal IOSHandle(long id) => Id = id;

        ~IOSHandle() => IOSBackend.nga_ad_release(Id);
    }

    /// <summary>
    /// The objects native holds through its proxies, by token: native decides how long, and its proxy's end releases the token.
    /// A load callback keeps one token while native holds it, so native sees one registration for it on
    /// every placement (in C# a callback's identity is the managed object).
    /// </summary>
    internal static class Tokens
    {
        static readonly object Gate = new object();
        static readonly Dictionary<long, object> Held = new Dictionary<long, object>();
        static readonly ConditionalWeakTable<object, object> Ids = new ConditionalWeakTable<object, object>();
        static long next;

        internal static long Own(object target)
        {
            lock (Gate)
            {
                var token = ++next;
                Held[token] = target;
                return token;
            }
        }

        internal static long Of(object callback)
        {
            lock (Gate)
            {
                if (Ids.TryGetValue(callback, out var boxed) && Held.ContainsKey((long)boxed)) return (long)boxed;
                var token = ++next;
                Held[token] = callback;
                Ids.Remove(callback);
                Ids.Add(callback, token);
                return token;
            }
        }

        internal static object Get(long token)
        {
            lock (Gate) return Held.TryGetValue(token, out var target) ? target : null;
        }

        internal static void Release(long token)
        {
            lock (Gate) Held.Remove(token);
        }
    }
}
