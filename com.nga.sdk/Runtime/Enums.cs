// The public enums: the same cases and raw integers as Android and iOS, PascalCase in C#, 0 the safe default. The
// integer, not the declaration order, is what crosses the bridge.

namespace NGA
{
    /// <summary>The one error table; values identical on every platform.</summary>
    public enum NGAErrorCode : int
    {
        NotInitialized = 100,
        InvalidConfig = 101,
        InvalidArgument = 102,
        Disabled = 103,
        RenderNotAvailable = 104,
        Network = 200,
        Timeout = 201,
        ServerRejected = 202,
        Internal = 299,
        InvalidPlacement = 300,
        NoFill = 301,
        Expired = 302,
        AdConsumed = 303,
        NotReady = 304,
        AlreadyShowing = 305,
        ActivityNotResumed = 306,
        RenderFailed = 307,
        InvalidShowTarget = 308,
    }

    /// <summary>A consent answer for one scope.</summary>
    public enum NGAConsentState : int
    {
        Unknown = 0,
        Granted = 1,
        Denied = 2,
    }

    /// <summary>The studio's age gate for this user.</summary>
    public enum NGAAgeState : int
    {
        Unknown = 0,
        Adult = 1,
        Child = 2,
    }

    /// <summary>How much the SDK logs; the default is <c>Warn</c>.</summary>
    public enum NGALogLevel : int
    {
        None = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4,
    }

    /// <summary>A mediation network keeps its own spelling.</summary>
    public enum NGAAdRevenueMediation : int
    {
        None = 0,
        AdMob = 1,
        Max = 2,
        LevelPlay = 3,
        Other = 4,
    }

    /// <summary>How exact a revenue figure is.</summary>
    public enum NGARevenuePrecision : int
    {
        Undefined = 0,
        Exact = 1,
        Estimated = 2,
        PublisherDefined = 3,
    }

    /// <summary><c>Unknown</c> is "not given", never on an <see cref="NGAImpression"/>.</summary>
    public enum NGAAdFormat : int
    {
        Unknown = 0,
        Banner = 1,
        Mrec = 2,
        Interstitial = 3,
        Rewarded = 4,
        RewardedInterstitial = 5,
        Native = 6,
        AppOpen = 7,
    }

    /// <summary>A loaded ad's state.</summary>
    public enum NGAAdState : int
    {
        Failed = 0,
        Ready = 1,
        Showing = 2,
        Consumed = 3,
        Expired = 4,
    }

    /// <summary>How a show ended.</summary>
    public enum NGAShowCompletion : int
    {
        Skipped = 0,
        Completed = 1,
    }

    /// <summary><c>None</c> when the reward was granted.</summary>
    public enum NGARewardDenial : int
    {
        None = 0,
        Skipped = 1,
        Rejected = 2,
        Interrupted = 3,
    }
}
