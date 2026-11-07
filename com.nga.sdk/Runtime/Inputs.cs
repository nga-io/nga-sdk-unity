// The builders: NGAConfig (fixed for the process) and NGAAdRevenue. Plain values; validation happens where the call is made.

namespace NGA
{
    /// <summary>What <see cref="NGASDK.Initialize"/> takes: the app key, the consent at start, test mode.</summary>
    public sealed class NGAConfig
    {
        public string AppKey { get; }
        public NGAConsent Consent { get; }
        public bool TestModeEnabled { get; }

        NGAConfig(string appKey, NGAConsent consent, bool testMode)
        {
            AppKey = appKey;
            Consent = consent;
            TestModeEnabled = testMode;
        }

        /// <summary><c>new NGAConfig.Builder(appKey, consent).TestMode(b).Build()</c>.</summary>
        public sealed class Builder
        {
            readonly string appKey;
            readonly NGAConsent consent;
            bool testMode;

            public Builder(string appKey, NGAConsent consent)
            {
                this.appKey = appKey;
                this.consent = consent;
            }

            /// <summary>Marks every request and event as test traffic.</summary>
            public Builder TestMode(bool enabled)
            {
                testMode = enabled;
                return this;
            }

            public NGAConfig Build() => new NGAConfig(appKey, consent, testMode);
        }
    }

    /// <summary>An impression's revenue from a network other than NGA.</summary>
    public sealed class NGAAdRevenue
    {
        public NGAAdRevenueMediation Mediation { get; }
        public long RevenueMicros { get; }
        public string Currency { get; }
        public NGARevenuePrecision Precision { get; }
        public string Network { get; private set; }
        public string AdUnitId { get; private set; }
        public NGAAdFormat AdFormat { get; private set; }
        public string Placement { get; private set; }
        public string NetworkPlacement { get; private set; }
        public string CreativeId { get; private set; }
        public string AdvertiserDomain { get; private set; }
        public string ImpressionId { get; private set; }

        NGAAdRevenue(NGAAdRevenueMediation mediation, long revenueMicros, string currency, NGARevenuePrecision precision)
        {
            Mediation = mediation;
            RevenueMicros = revenueMicros;
            Currency = currency;
            Precision = precision;
        }

        /// <summary><c>new NGAAdRevenue.Builder(mediation, revenueMicros, currency, precision)</c>, then the optional fields.</summary>
        public sealed class Builder
        {
            readonly NGAAdRevenue value;

            public Builder(NGAAdRevenueMediation mediation, long revenueMicros, string currency, NGARevenuePrecision precision)
            {
                value = new NGAAdRevenue(mediation, revenueMicros, currency, precision);
            }

            public Builder Network(string network) { value.Network = network; return this; }
            public Builder AdUnitId(string adUnitId) { value.AdUnitId = adUnitId; return this; }
            public Builder AdFormat(NGAAdFormat adFormat) { value.AdFormat = adFormat; return this; }
            public Builder Placement(string placement) { value.Placement = placement; return this; }
            public Builder NetworkPlacement(string networkPlacement) { value.NetworkPlacement = networkPlacement; return this; }
            public Builder CreativeId(string creativeId) { value.CreativeId = creativeId; return this; }
            public Builder AdvertiserDomain(string advertiserDomain) { value.AdvertiserDomain = advertiserDomain; return this; }
            public Builder ImpressionId(string impressionId) { value.ImpressionId = impressionId; return this; }

            public NGAAdRevenue Build() =>
                new NGAAdRevenue(value.Mediation, value.RevenueMicros, value.Currency, value.Precision)
                {
                    Network = value.Network,
                    AdUnitId = value.AdUnitId,
                    AdFormat = value.AdFormat,
                    Placement = value.Placement,
                    NetworkPlacement = value.NetworkPlacement,
                    CreativeId = value.CreativeId,
                    AdvertiserDomain = value.AdvertiserDomain,
                    ImpressionId = value.ImpressionId,
                };
        }
    }
}
