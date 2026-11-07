// The callbacks. C# interfaces declare every member, optional ones included; a class implements those as empty methods.
// Every member is called on Unity's game thread, from Unity's own dispatcher, always after the call that caused it has
// returned;
// on Android a full-screen show's callbacks arrive after the ad closes, in their original order.

namespace NGA
{
    /// <summary>Initialization and process-level events. Held strongly until a later
    /// <c>Initialize</c> with the same key passes a non-null one.</summary>
    public interface NGAListener
    {
        /// <summary>Init's one answer. Game thread.</summary>
        void OnInitialized(NGAInitResult result);

        /// <summary>A problem after init that the studio should know of. Optional. Game thread.</summary>
        void OnError(NGAError error);

        /// <summary>A show the process died in, reported once init has succeeded.
        /// Optional. Game thread.</summary>
        void OnAdClosedAfterRestart(string placementId, string opportunityId, bool rewardGranted);
    }

    /// <summary>One <c>LoadAd</c> registration. Held strongly until <c>OnFailed</c> or until its
    /// handle leaves scope.</summary>
    public interface NGAAdLoadCallback
    {
        /// <summary>The ad is cached, verified and showable. Game thread.</summary>
        void OnLoaded(NGAAd ad);

        /// <summary>The load failed; <see cref="NGAError.Retryable"/> and <see cref="NGAError.RetryAfterMillis"/> say
        /// whether and when to try again. Game thread.</summary>
        void OnFailed(string placementId, NGAError error);

        /// <summary>The handle you were given is no longer showable. Optional. Game thread.</summary>
        void OnExpired(NGAAd ad);
    }

    /// <summary>One show: every show ends with exactly one <c>OnClosed</c> or
    /// <c>OnShowFailed</c>. Held strongly until then.</summary>
    public interface NGAAdShowListener
    {
        /// <summary>The native impression gate passed. Game thread.</summary>
        void OnShowStarted(NGAAd ad);

        /// <summary>The native touch gate passed. Optional. Game thread.</summary>
        void OnClicked(NGAAd ad);

        /// <summary>Rewarded placements only; once. Optional. Game thread.</summary>
        void OnRewardResult(NGAAd ad, NGARewardResult result);

        /// <summary>Game thread.</summary>
        void OnCompleted(NGAAd ad, NGAShowCompletion completion);

        /// <summary>Always last on success; the handle is <see cref="NGAAdState.Consumed"/>. Game thread.</summary>
        void OnClosed(NGAAd ad);

        /// <summary>Game thread.</summary>
        void OnShowFailed(NGAAd ad, NGAError error);
    }
}
