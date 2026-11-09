// Unity outside a player: in the Editor and on every
// platform without a native library, Initialize reports INVALID_CONFIG / platform.unsupported with one log line, every
// later call behaves as after a failed init (LoadAd answers NOT_INITIALIZED / init.failed), the setters keep their values,
// and nothing throws into the game. Callbacks still come through the dispatcher, so play mode sees the device's order.

using System;
using UnityEngine;

namespace NGA.Internal
{
    internal sealed class StubBackend : IBackend
    {
        readonly object gate = new object();
        bool logged;
        NGAConsent consent = NGAConsent.Unknown();
        NGAAgeState age = NGAAgeState.Unknown;
        bool muted;

        public void Initialize(NGAConfig config, NGAListener listener)
        {
            lock (gate)
            {
                if (!logged)
                {
                    logged = true;
                    Debug.Log("[NGA] " + Errors.PlatformUnsupported + ": the NGA SDK runs only in an Android or iOS player; nothing native runs here.");
                }
            }
            if (listener == null) return;
            var result = new NGAInitResult(false, Errors.PlatformUnsupportedError(), false);
            Dispatcher.Post(() => listener.OnInitialized(result));
        }

        public string SdkVersion() => NGASDK.PackageVersion;

        public void SetLogLevel(NGALogLevel level)
        {
            if (!Validation.Named(level)) Debug.LogError("[NGA] SetLogLevel ignored: level is null");
        }

        public void SetConsent(NGAConsent value)
        {
            if (!Validation.Named(value))
            {
                Debug.LogWarning("[NGA] SetConsent(null) ignored");
                return;
            }
            lock (gate) consent = value;
        }

        public NGAConsent GetConsent()
        {
            lock (gate) return consent;
        }

        public void SetAge(NGAAgeState value)
        {
            if (!Validation.Named(value))
            {
                Debug.LogError("[NGA] SetAge ignored: age is null");
                return;
            }
            lock (gate) age = value;
        }

        public NGAAgeState GetAge()
        {
            lock (gate) return age;
        }

        public NGAResult SetCustomerUserId(string id)
        {
            var error = Validation.CustomerUserId(id);
            return new NGAResult(error == null, error);
        }

        public NGAResult LogAdRevenue(NGAAdRevenue revenue)
        {
            var error = Validation.AdRevenue(revenue);
            if (error != null && error.Detail == Errors.ArgumentNull) Debug.LogWarning("[NGA] LogAdRevenue: " + error.Message);
            return new NGAResult(error == null, error);
        }

        /// <summary>The stub validates as iOS's native call does, and records nothing.</summary>
        public NGAResult LogPurchase(string jws)
        {
            var error = Validation.PurchaseJws(jws);
            if (error != null && error.Detail == Errors.ArgumentNull) Debug.LogWarning("[NGA] LogPurchase: " + error.Message);
            return new NGAResult(error == null, error);
        }

        public void LoadAd(string placementId, NGAAdLoadCallback callback)
        {
            if (callback == null)
            {
                Debug.LogError("[NGA] LoadAd without a callback does nothing");
                return;
            }
            var error = Validation.IsWellFormedPlacement(placementId) ? Errors.InitFailedError() : Errors.PlacementFormatError();
            Dispatcher.Post(() => callback.OnFailed(placementId, error));
        }

        public void SetImpressionListener(Action<NGAImpression> listener) { }

        public void SetMuted(bool value)
        {
            lock (gate) muted = value;
        }

        public bool IsMuted()
        {
            lock (gate) return muted;
        }

        BannerYield bannerYield;

        /// <summary>Kept, like every setter's value; no banner shows here, so neither half is ever called.</summary>
        public void SetBannerYield(BannerYield pair)
        {
            lock (gate) bannerYield = pair;
        }

        // No ad exists outside a player; these answer as for a failed handle.
        public NGAAdState State(NGAAd ad) => NGAAdState.Failed;
        public bool IsReady(NGAAd ad) => false;
        public void Show(NGAAd ad, NGAAdShowListener listener) { }
        public void Close(NGAAd ad) { }
    }
}
