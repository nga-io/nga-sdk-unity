// The one way a callback reaches the studio's C# code: posted through Unity's own main-thread
// dispatch, its SynchronizationContext, which keeps the order, holds callbacks while Unity is paused (an NGA full-screen ad
// in front on Android) and runs them on the game thread from Unity's loop, where an exception from the studio's callback
// is logged by Unity, never thrown into a native-to-managed frame. Nothing here runs per frame and nothing polls.

using System;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

namespace NGA.Internal
{
    internal static class Dispatcher
    {
        static SynchronizationContext context;
        static int gameThread = -1;
        /// <summary>Each callback's time on the game thread, the studio's code inside it, in Unity's Profiler.</summary>
        internal static readonly ProfilerMarker Marker = new ProfilerMarker("NGA.Callback");

        static readonly SendOrPostCallback Run = state =>
        {
            using (Marker.Auto()) ((Action)state)();
        };

        /// <summary>Captured on the game thread before the first scene loads; a test may substitute its own.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Capture()
        {
            context = SynchronizationContext.Current;
            gameThread = Thread.CurrentThread.ManagedThreadId;
        }

        internal static SynchronizationContext Context
        {
            get => context;
            set
            {
                context = value;
                gameThread = Thread.CurrentThread.ManagedThreadId;
            }
        }

        internal static bool OnGameThread => Thread.CurrentThread.ManagedThreadId == gameThread;

        /// <summary>
        /// Queues [action] for the game thread, always asynchronously, even when called on it:
        /// a trampoline only enqueues, and the studio's callback never runs inside the call that caused it.
        /// </summary>
        internal static void Post(Action action)
        {
            var ctx = context;
            if (ctx == null)
            {
                // Before the capture (a call from a static constructor of the game's own): Unity's context is the
                // game thread's current one by then whenever we are on it.
                ctx = SynchronizationContext.Current;
                if (ctx == null)
                {
                    Debug.LogError("[NGA] no game-thread dispatcher yet; a callback was dropped");
                    return;
                }
                context = ctx;
                gameThread = Thread.CurrentThread.ManagedThreadId;
            }
            ctx.Post(Run, action);
        }
    }
}
