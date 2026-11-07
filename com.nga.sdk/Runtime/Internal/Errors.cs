// The errors the C# layer raises itself, with the native library's detail strings, and the argument
// checks the stub needs, the native library's own (nga-sdk `Validation`): on a device the native library checks them.

using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace NGA.Internal
{
    internal static class Errors
    {
        internal const string PlatformUnsupported = "platform.unsupported";
        internal const string InitFailed = "init.failed";
        internal const string PlacementFormat = "placement.format";
        internal const string ArgumentNull = "argument.null";
        internal const string CustomerUserIdLength = "customer_user_id.length";
        internal const string CustomerUserIdFormat = "customer_user_id.format";
        internal const string AdRevenueRevenue = "ad_revenue.revenue";
        internal const string AdRevenueCurrency = "ad_revenue.currency";
        internal const string AdRevenueLength = "ad_revenue.length";
        internal const string AdRevenueFormat = "ad_revenue.format";
        internal const string PurchaseJws = "purchase.jws";

        internal static NGAError PlatformUnsupportedError() =>
            new NGAError(NGAErrorCode.InvalidConfig, PlatformUnsupported,
                "The NGA SDK runs only in an Android or iOS player; in the Editor and on other platforms it is a stub.", false, null);

        internal static NGAError InitFailedError() =>
            new NGAError(NGAErrorCode.NotInitialized, InitFailed, "Initialization failed; the call was not made.", false, null);

        internal static NGAError PlacementFormatError() =>
            new NGAError(NGAErrorCode.InvalidPlacement, PlacementFormat,
                "A placement id is plc_ and 3 to 64 lower-case letters, digits or _.", false, null);

        internal static NGAError Invalid(string detail, string message) =>
            new NGAError(NGAErrorCode.InvalidArgument, detail, message, false, null);
    }

    internal static class Validation
    {
        static readonly Regex Placement = new Regex("^plc_[a-z0-9_]{3,64}$");
        static readonly Regex Currency = new Regex("^[A-Za-z]{3}$");

        internal static bool IsWellFormedPlacement(string placementId) => placementId != null && Placement.IsMatch(placementId);

        /// <summary>A customer user id: 1–128 characters, or null to remove the id; no NUL character, which the
        /// server refuses in any string, as the native library answers.</summary>
        internal static NGAError CustomerUserId(string id)
        {
            if (id == null) return null;
            if (id.Length < 1 || id.Length > 128) return Errors.Invalid(Errors.CustomerUserIdLength, "The customer user id must be 1 to 128 characters.");
            if (id.IndexOf('\0') >= 0) return Errors.Invalid(Errors.CustomerUserIdFormat, "The customer user id must not contain a NUL character.");
            return null;
        }

        /// <summary>
        /// An integer cast to an enum that names no case is a null of that
        /// argument, answered as null is, so it never reaches native code that switches on it.
        /// </summary>
        internal static bool Named<T>(T value) where T : Enum => Enum.IsDefined(typeof(T), value);

        /// <summary>A consent whose states both name a case; any other counts as no consent.</summary>
        internal static bool Named(NGAConsent c) => c != null && Named(c.Analytics) && Named(c.AdsPersonalization);

        /// <summary>An ad revenue's fields, in order; an unnamed enum integer is a null.</summary>
        internal static NGAError AdRevenue(NGAAdRevenue r)
        {
            if (r == null) return Errors.Invalid(Errors.ArgumentNull, "adRevenue must not be null.");
            if (!Named(r.Mediation)) return Errors.Invalid(Errors.ArgumentNull, "mediation must not be null.");
            if (!Named(r.Precision)) return Errors.Invalid(Errors.ArgumentNull, "precision must not be null.");
            if (!Named(r.AdFormat)) return Errors.Invalid(Errors.ArgumentNull, "adFormat must not be null.");
            if (r.Currency == null) return Errors.Invalid(Errors.ArgumentNull, "currency must not be null.");
            if (r.RevenueMicros < 0) return Errors.Invalid(Errors.AdRevenueRevenue, "revenueMicros must be zero or more.");
            if (!Currency.IsMatch(r.Currency)) return Errors.Invalid(Errors.AdRevenueCurrency, "currency must be a 3-letter code.");
            foreach (var s in new[] { r.Network, r.AdUnitId, r.Placement, r.NetworkPlacement, r.CreativeId, r.AdvertiserDomain, r.ImpressionId })
            {
                if (s != null && s.Length > 200) return Errors.Invalid(Errors.AdRevenueLength, "Each string field must be at most 200 characters.");
            }
            // No NUL character, which the server refuses in any string
            foreach (var s in new[] { r.Network, r.AdUnitId, r.Placement, r.NetworkPlacement, r.CreativeId, r.AdvertiserDomain, r.ImpressionId })
            {
                if (s != null && s.IndexOf('\0') >= 0) return Errors.Invalid(Errors.AdRevenueFormat, "A string field must not contain a NUL character.");
            }
            return null;
        }

        /// <summary>
        /// <c>LogPurchase</c>'s string is a StoreKit 2 transaction's JWS, three base64url parts
        /// whose payload names a <c>transactionId</c> and a <c>productId</c>; null is <c>argument.null</c>. The signature is
        /// the server's to check.
        /// </summary>
        internal static NGAError PurchaseJws(string jws)
        {
            if (jws == null) return Errors.Invalid(Errors.ArgumentNull, "jws must not be null.");
            var parts = jws.Split('.');
            if (parts.Length == 3 && parts[0].Length > 0 && parts[2].Length > 0)
            {
                try
                {
                    var payload = parts[1].Replace('-', '+').Replace('_', '/');
                    payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                    var text = Encoding.UTF8.GetString(Convert.FromBase64String(payload)).Trim();
                    if (text.StartsWith("{"))
                    {
                        var fields = JsonUtility.FromJson<JwsPayload>(text);
                        if (!string.IsNullOrEmpty(fields?.transactionId) && !string.IsNullOrEmpty(fields.productId)) return null;
                    }
                }
                catch (Exception)
                {
                    // not base64url, or not JSON: not a JWS
                }
            }
            return Errors.Invalid(Errors.PurchaseJws, "jws is not a StoreKit 2 transaction's JWS.");
        }

        [Serializable]
        sealed class JwsPayload
        {
            public string transactionId;
            public string productId;
        }
    }
}
