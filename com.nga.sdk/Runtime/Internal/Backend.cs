// What NGASDK delegates to: the native library in an Android or iOS player, the stub everywhere else.

using System;

namespace NGA.Internal
{
    internal interface IBackend
    {
        void Initialize(NGAConfig config, NGAListener listener);
        string SdkVersion();
        void SetLogLevel(NGALogLevel level);
        void SetConsent(NGAConsent consent);
        NGAConsent GetConsent();
        void SetAge(NGAAgeState age);
        NGAAgeState GetAge();
        NGAResult SetCustomerUserId(string id);
        NGAResult LogAdRevenue(NGAAdRevenue revenue);
        NGAResult LogPurchase(string jws);
        void LoadAd(string placementId, NGAAdLoadCallback callback);
        void SetImpressionListener(Action<NGAImpression> listener);
        void SetMuted(bool muted);
        bool IsMuted();

        NGAAdState State(NGAAd ad);
        bool IsReady(NGAAd ad);
        void Show(NGAAd ad, NGAAdShowListener listener);

        /// <summary>Slides the banner down and ends its show; nothing on any other handle or state.</summary>
        void Close(NGAAd ad);
    }

    internal static class Backends
    {
        static IBackend current;

        internal static IBackend Current
        {
            get => current ?? (current = Create());
            set => current = value;
        }

        static IBackend Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidBackend();
#elif UNITY_IOS && !UNITY_EDITOR
            return new IOSBackend();
#else
            return new StubBackend();
#endif
        }
    }
}
