// The one entry point, as a thin mirror of
// the native NGASDK. Any thread in; every callback out on Unity's game thread, always asynchronously.
// In an Android or iOS player it drives NGA's native library; elsewhere it is a stub.

using System;
using NGA.Internal;

namespace NGA
{
    public static class NGASDK
    {
        /// <summary>This package's version; the native library's is the same.</summary>
        internal const string PackageVersion = "0.1.0";

        /// <summary>
        /// Starts the SDK with the app key and the player's consent. Answers once through
        /// <see cref="NGAListener.OnInitialized"/> on the game thread; on Android the plugin passes Unity's current
        /// Activity. Outside an Android or iOS player: <c>INVALID_CONFIG / platform.unsupported</c>. Any thread.
        /// </summary>
        public static void Initialize(NGAConfig config, NGAListener listener) => Backends.Current.Initialize(config, listener);

        /// <summary>The native library's version. Any thread.</summary>
        public static string GetSdkVersion() => Backends.Current.SdkVersion();

        /// <summary>Applies at once, before Initialize too. Any thread.</summary>
        public static void SetLogLevel(NGALogLevel level) => Backends.Current.SetLogLevel(level);

        /// <summary>The player's consent; applies at once and is persisted. Null is ignored with a
        /// warning. Any thread.</summary>
        public static void SetConsent(NGAConsent consent) => Backends.Current.SetConsent(consent);

        /// <summary>The effective consent, CMP keys included. Any thread; never blocks.</summary>
        public static NGAConsent GetConsent() => Backends.Current.GetConsent();

        /// <summary>The player's age, a privacy input. Any thread.</summary>
        public static void SetAge(NGAAgeState age) => Backends.Current.SetAge(age);

        /// <summary>Any thread; never blocks.</summary>
        public static NGAAgeState GetAge() => Backends.Current.GetAge();

        /// <summary>The studio's own user id, 1–128 characters, or null to remove it. Any thread.</summary>
        public static NGAResult SetCustomerUserId(string id) => Backends.Current.SetCustomerUserId(id);

        /// <summary>An impression's revenue from another network. Any thread.</summary>
        public static NGAResult LogAdRevenue(NGAAdRevenue revenue) => Backends.Current.LogAdRevenue(revenue);

        /// <summary>
        /// A StoreKit 2 purchase from Unity IAP 5: pass the order's JWS (<c>Info.Apple.jwsRepresentation</c>) beside
        /// <c>ConfirmPurchase</c>. On Android it records nothing and answers <c>accepted = true</c>,
        /// so a cross-platform script needs no platform check. A string that is not a StoreKit 2 transaction's JWS is
        /// <c>INVALID_ARGUMENT / purchase.jws</c> on iOS; null is <c>argument.null</c>. Any thread.
        /// </summary>
        public static NGAResult LogPurchase(string jws) => Backends.Current.LogPurchase(jws);

        /// <summary>
        /// Loads an ad for a placement. The callback is held strongly until <c>OnFailed</c> or
        /// until its handle leaves scope; the same callback object registered twice counts once. Any thread.
        /// </summary>
        public static void LoadAd(string placementId, NGAAdLoadCallback callback) => Backends.Current.LoadAd(placementId, callback);

        /// <summary>The app-wide impression listener, replacing any earlier one; null removes it.
        /// Called on the game thread. Any thread.</summary>
        public static void SetImpressionListener(Action<NGAImpression> listener) => Backends.Current.SetImpressionListener(listener);

        /// <summary>The one global mute for NGA's ads. Any thread.</summary>
        public static void SetMuted(bool muted) => Backends.Current.SetMuted(muted);

        /// <summary>Any thread; never blocks.</summary>
        public static bool IsMuted() => Backends.Current.IsMuted();

        /// <summary>
        /// The game's own banner ad yields to NGA's native cross-promo banner: <paramref name="hide"/> runs on the game thread
        /// just before the banner is added, which waits for it at most a second, and <paramref name="restore"/> exactly once
        /// after each hide: after the banner is removed, whatever ended the show, or at once when the hide threw or took
        /// longer than a second, which refuses the show. Both are held until a later call replaces them; both null clears
        /// them, one null alone is ignored with a warning. Any thread.
        /// </summary>
        public static void SetBannerYield(Action hide, Action restore)
        {
            var pair = BannerYield.Of(hide, restore, out var valid);
            if (valid) Backends.Current.SetBannerYield(pair);
        }
    }
}
