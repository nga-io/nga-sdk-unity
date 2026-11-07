// The values the SDK hands back and the consent input. Immutable; safe to read on any thread.

using System;

namespace NGA
{
    /// <summary>
    /// An error from the one table. <see cref="Detail"/> is stable and may be branched on;
    /// <see cref="Message"/> may change between releases. C# carries no <c>Cause</c>.
    /// </summary>
    public sealed class NGAError
    {
        public NGAErrorCode Code { get; }
        public string Detail { get; }
        public string Message { get; }
        public bool Retryable { get; }

        /// <summary>A sensible wait when the server or the SDK knows one; otherwise null.</summary>
        public long? RetryAfterMillis { get; }

        public NGAError(NGAErrorCode code, string detail, string message, bool retryable, long? retryAfterMillis)
        {
            Code = code;
            Detail = detail;
            Message = message;
            Retryable = retryable;
            RetryAfterMillis = retryAfterMillis;
        }

        public override string ToString() =>
            $"NGAError({Code} {(int)Code} / {Detail}{(Retryable ? ", retryable" : "")}" +
            (RetryAfterMillis.HasValue ? $", retry after {RetryAfterMillis.Value} ms" : "") + ")";
    }

    /// <summary>The answer of a call that validates at once: <c>SetCustomerUserId</c>, <c>LogAdRevenue</c>.</summary>
    public sealed class NGAResult
    {
        public bool Accepted { get; }
        public NGAError Error { get; }

        public NGAResult(bool accepted, NGAError error)
        {
            Accepted = accepted;
            Error = error;
        }

        public override string ToString() => Accepted ? "NGAResult(accepted)" : $"NGAResult({Error})";
    }

    /// <summary>The answer of <c>Initialize</c>, through <see cref="NGAListener.OnInitialized"/>.</summary>
    public sealed class NGAInitResult
    {
        public bool Success { get; }
        public NGAError Error { get; }
        public bool FromCache { get; }

        public NGAInitResult(bool success, NGAError error, bool fromCache)
        {
            Success = success;
            Error = error;
            FromCache = fromCache;
        }

        public override string ToString() =>
            Success ? $"NGAInitResult(success{(FromCache ? ", from cache" : "")})" : $"NGAInitResult({Error})";
    }

    /// <summary>A rewarded show's outcome: granted, or a typed denial.</summary>
    public sealed class NGARewardResult
    {
        public bool Granted { get; }

        /// <summary><see cref="NGARewardDenial.None"/> when granted.</summary>
        public NGARewardDenial Denial { get; }

        public NGARewardResult(bool granted, NGARewardDenial denial)
        {
            Granted = granted;
            Denial = denial;
        }

        public override string ToString() => Granted ? "NGARewardResult(granted)" : $"NGARewardResult(denied: {Denial})";
    }

    /// <summary>One NGA impression and its payout.</summary>
    public sealed class NGAImpression
    {
        public string PlacementId { get; }
        public string OpportunityId { get; }
        public NGAAdFormat Format { get; }
        public long RevenueMicros { get; }
        public string Currency { get; }
        public NGARevenuePrecision Precision { get; }
        public string CampaignId { get; }
        public string CreativeId { get; }
        public string AdvertiserBundle { get; }
        public string AdvertiserDomain { get; }

        public NGAImpression(
            string placementId,
            string opportunityId,
            NGAAdFormat format,
            long revenueMicros,
            string currency,
            NGARevenuePrecision precision,
            string campaignId,
            string creativeId,
            string advertiserBundle,
            string advertiserDomain)
        {
            PlacementId = placementId;
            OpportunityId = opportunityId;
            Format = format;
            RevenueMicros = revenueMicros;
            Currency = currency;
            Precision = precision;
            CampaignId = campaignId;
            CreativeId = creativeId;
            AdvertiserBundle = advertiserBundle;
            AdvertiserDomain = advertiserDomain;
        }
    }

    /// <summary>
    /// The player's consent: first-party analytics and ads personalisation. Required at
    /// <c>Initialize</c>; <see cref="Unknown"/> leaves the decision to the CMP's keys and the server's regional rule.
    /// </summary>
    public sealed class NGAConsent : IEquatable<NGAConsent>
    {
        public NGAConsentState Analytics { get; }
        public NGAConsentState AdsPersonalization { get; }

        public NGAConsent(NGAConsentState analytics, NGAConsentState adsPersonalization)
        {
            Analytics = analytics;
            AdsPersonalization = adsPersonalization;
        }

        public static NGAConsent Unknown() => new NGAConsent(NGAConsentState.Unknown, NGAConsentState.Unknown);

        public bool Equals(NGAConsent other) =>
            other is object && Analytics == other.Analytics && AdsPersonalization == other.AdsPersonalization;

        public override bool Equals(object obj) => obj is NGAConsent c && Equals(c);

        public override int GetHashCode() => ((int)Analytics * 31) + (int)AdsPersonalization;

        public override string ToString() => $"NGAConsent(analytics={Analytics}, adsPersonalization={AdsPersonalization})";
    }
}
