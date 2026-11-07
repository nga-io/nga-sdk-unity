// One loaded ad: a sealed class,
// one wrapper per opportunity id, so a callback always receives the same instance for the same ad, and equality is by
// OpportunityId. A wrapper leaves the cache after its last callback.

using System;
using System.Collections.Generic;
using NGA.Internal;

namespace NGA
{
    public sealed class NGAAd : IEquatable<NGAAd>
    {
        public string PlacementId { get; }

        /// <summary>The single-use decision id; the correlation id from load through impression.</summary>
        public string OpportunityId { get; }

        /// <summary>The wall-clock rendering of the expiry; expiry itself runs on the monotonic clock.</summary>
        public long ExpiresAtMillis { get; }

        /// <summary>The native handle (an Android <c>NGAAd</c>, an iOS handle id), null outside a player.</summary>
        internal readonly object Native;

        internal NGAAd(string placementId, string opportunityId, long expiresAtMillis, object native)
        {
            PlacementId = placementId;
            OpportunityId = opportunityId;
            ExpiresAtMillis = expiresAtMillis;
            Native = native;
        }

        /// <summary>The handle's state, read from the native snapshot. Any thread; never blocks.</summary>
        public NGAAdState State => Backends.Current.State(this);

        /// <summary><c>State == Ready</c> and not expired; nothing else. Any thread; never blocks.</summary>
        public bool IsReady() => Backends.Current.IsReady(this);

        /// <summary>
        /// The show, every format, from Unity's current Activity on Android and its root view controller on iOS. A native
        /// cross-promo handle slides the banner up over the bottom of the screen, above all Unity
        /// UI, until it is closed. On Android Unity pauses while a full-screen ad is in front, so the listener's callbacks
        /// arrive after it closes, in order; on iOS they arrive during the ad. Any thread.
        /// </summary>
        public void Show(NGAAdShowListener listener) => Backends.Current.Show(this, listener);

        /// <summary>
        /// Slides the banner down and ends its show with <c>OnClosed</c>; nothing on any other handle
        /// or state. The game calls this before it opens its own UI over the bottom of the screen. Any thread.
        /// </summary>
        public void Close() => Backends.Current.Close(this);

        public bool Equals(NGAAd other) => other is object && OpportunityId == other.OpportunityId;

        public override bool Equals(object obj) => obj is NGAAd ad && Equals(ad);

        public override int GetHashCode() => OpportunityId == null ? 0 : OpportunityId.GetHashCode();

        public static bool operator ==(NGAAd a, NGAAd b) => a is null ? b is null : a.Equals(b);

        public static bool operator !=(NGAAd a, NGAAd b) => !(a == b);

        public override string ToString() => $"NGAAd({PlacementId}, {OpportunityId})";
    }

    internal static class AdRegistry
    {
        static readonly Dictionary<string, NGAAd> Ads = new Dictionary<string, NGAAd>();

        /// <summary>The one wrapper for this opportunity, made from the native handle the first time.</summary>
        internal static NGAAd For(string opportunityId, Func<NGAAd> make)
        {
            lock (Ads)
            {
                if (Ads.TryGetValue(opportunityId, out var ad)) return ad;
                ad = make();
                Ads[opportunityId] = ad;
                return ad;
            }
        }

        /// <summary>After the wrapper's last callback: OnClosed, a final OnShowFailed, or OnExpired.</summary>
        internal static void Release(string opportunityId)
        {
            lock (Ads) Ads.Remove(opportunityId);
        }

        internal static int Count
        {
            get
            {
                lock (Ads) return Ads.Count;
            }
        }
    }
}
