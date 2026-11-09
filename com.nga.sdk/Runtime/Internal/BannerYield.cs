// The studio's banner yield in C#: the pair is held here, strongly, and each call native asks for runs on the game thread
// through the dispatcher, in the order asked, so a restore never runs before its hide. After a hide has run, or thrown,
// the backend reports it to native; a hide that throws is the game's own error too, so Unity logs it as well.

using System;
using UnityEngine;

namespace NGA.Internal
{
    internal sealed class BannerYield
    {
        internal readonly Action Hide;
        internal readonly Action Restore;

        BannerYield(Action hide, Action restore)
        {
            Hide = hide;
            Restore = restore;
        }

        /// <summary>
        /// The pair to register, or null to clear it. One null alone is ignored with one warning, and <paramref name="valid"/>
        /// is false.
        /// </summary>
        internal static BannerYield Of(Action hide, Action restore, out bool valid)
        {
            valid = (hide == null) == (restore == null);
            if (!valid)
            {
                Debug.LogWarning("[NGA] SetBannerYield ignored: hide and restore are set together, or both null to clear");
                return null;
            }
            return hide == null ? null : new BannerYield(hide, restore);
        }

        /// <summary>Queues hide on the game thread; <paramref name="ran"/> is told whether it threw, after it ran.</summary>
        internal void RunHide(Action<bool> ran) => Dispatcher.Post(() =>
        {
            var threw = true;
            try
            {
                Hide();
                threw = false;
            }
            finally
            {
                ran(threw);
            }
        });

        /// <summary>Queues restore on the game thread, after any hide queued before it.</summary>
        internal void RunRestore() => Dispatcher.Post(Restore);
    }
}
