// C# `SetBannerYield(hide, restore)` on iOS: the pair lives in C# and runs on Unity's game thread; this hands each call
// to the C# layer and reports `hide` to the SDK once C# says it ran. The SDK's banner yield type is internal (SPI), so it
// sits in a file of its own, as `LogPurchase` does.

import Foundation
@_spi(NGAInternal) import NGASDK

/// C#'s yield trampoline: (generation, token, kind), kind 0 `hide`, 1 `restore`. It only enqueues.
public typealias NGABridgeYieldCallback = @convention(c) (Int64, Int64, Int32) -> Void

private final class CSharpBannerYield: NGABannerYield {
    private static let lock = NSLock()
    private static var nextToken: Int64 = 0
    private static var pending: [Int64: (String?) -> Void] = [:]

    private let generation: Int64
    private let callback: NGABridgeYieldCallback

    init(generation: Int64, callback: NGABridgeYieldCallback) {
        self.generation = generation
        self.callback = callback
    }

    func hide(_ done: @escaping (String?) -> Void) {
        CSharpBannerYield.lock.lock()
        CSharpBannerYield.nextToken += 1
        let token = CSharpBannerYield.nextToken
        CSharpBannerYield.pending[token] = done
        CSharpBannerYield.lock.unlock()
        callback(generation, token, 0)
    }

    func restore() { callback(generation, 0, 1) }

    static func ran(_ token: Int64, threw: Bool) {
        lock.lock()
        let done = pending.removeValue(forKey: token)
        lock.unlock()
        done?(threw ? "an exception in the game's hide" : nil)
    }
}

/// A generation of 0 clears the yield.
@_cdecl("nga_set_banner_yield")
public func ngaSetBannerYield(_ generation: Int64, _ callback: NGABridgeYieldCallback?) {
    guard generation != 0, let callback else { return NGAUnityBridge.setBannerYield(nil) }
    NGAUnityBridge.setBannerYield(CSharpBannerYield(generation: generation, callback: callback))
}

@_cdecl("nga_banner_yield_hide_ran")
public func ngaBannerYieldHideRan(_ token: Int64, _ threw: Int32) {
    CSharpBannerYield.ran(token, threw: threw != 0)
}
