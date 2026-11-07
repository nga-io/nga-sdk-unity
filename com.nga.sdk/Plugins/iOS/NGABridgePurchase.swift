// C# `LogPurchase(jws)` on iOS: the SDK's public `logPurchase` takes a StoreKit `VerificationResult`, which no JWS
// string makes, so this one call goes through the SDK's internal bridge for the C# layer. It is the shim's only SPI use, in a file of its own.

import Foundation
@_spi(NGAInternal) import NGASDK

/// The result as text, the shape `NGABridge.mm` gives C#: accepted, then the error's fields, joined by \u{1f}, a nil \u{1e}.
@_cdecl("nga_log_purchase")
public func ngaLogPurchase(_ jws: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    let result = NGAUnityBridge.logPurchase(jws: jws.map { String(cString: $0) })
    let none = "\u{1e}"
    var fields = [result.accepted ? "1" : "0"]
    if let e = result.error {
        fields += [String(e.code.rawValue), e.detail, e.message, e.retryable ? "1" : "0", e.retryAfterMillis.map(String.init) ?? none]
    } else {
        fields += [none, none, none, none, none]
    }
    return strdup(fields.joined(separator: "\u{1f}"))
}
