// The C-callable shim between the C# layer and the iOS SDK, compiled into the Unity Xcode project. It calls the SDK's
// Objective-C entry points, which take a nil or an unnamed enum as a null argument. Values cross as UTF-8 text: fields joined by \x1f, a nil
// field \x1e. Every callback reaches C# on the main thread, Unity's game thread, through one function pointer, and C#
// only enqueues. `LogPurchase` is in NGABridgePurchase.swift.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <NGASDK/NGASDK-Swift.h>

#if !__has_feature(objc_arc)
#error NGABridge.mm needs -fobjc-arc: its .meta file sets it
#endif

extern "C" UIViewController* UnityGetGLViewController(void);

typedef void (*NGABridgeCallback)(int64_t token, int32_t kind, const char* text);

// The kinds C# switches on (Runtime/Internal/IOSBackend.cs).
enum {
    kInitialized = 0, kError = 1, kClosedAfterRestart = 2, kLoaded = 3, kLoadFailed = 4, kExpired = 5, kShowStarted = 6,
    kClicked = 7, kReward = 8, kCompleted = 9, kClosed = 10, kShowFailed = 11, kImpression = 12, kRelease = 13,
};

static NGABridgeCallback bridgeCallback;

static NSString* Join(NSArray* fields)
{
    NSMutableArray* parts = [NSMutableArray arrayWithCapacity:fields.count];
    for (id f in fields)
    {
        if (f == [NSNull null]) { [parts addObject:@"\x1e"]; continue; }
        NSString* s = [f description];
        s = [s stringByReplacingOccurrencesOfString:@"\x1f" withString:@" "];
        [parts addObject:[s stringByReplacingOccurrencesOfString:@"\x1e" withString:@" "]];
    }
    return [parts componentsJoinedByString:@"\x1f"];
}

static id Field(id value) { return value ?: [NSNull null]; }

static NSArray* ErrorFields(NGAError* e)
{
    if (e == nil) return @[ [NSNull null], [NSNull null], [NSNull null], [NSNull null], [NSNull null] ];
    return @[ @((NSInteger)e.code), e.detail, e.message, @(e.retryable ? 1 : 0), Field(e.retryAfterMillis) ];
}

static void Send(int64_t token, int32_t kind, NSString* text)
{
    if (bridgeCallback != NULL) bridgeCallback(token, kind, text.UTF8String);
}

static char* Copy(NSString* text) { return strdup(text.UTF8String); } // the marshaller frees what a P/Invoke returns

static NSString* Str(const char* s) { return s == NULL ? nil : [NSString stringWithUTF8String:s]; }

// --- the native handles C# holds, one id per delivery; C#'s wrapper releases its own when it is collected

static NSMutableDictionary<NSNumber*, id<NGAAd>>* handles;
static int64_t nextHandle;

static int64_t Retain(id<NGAAd> ad)
{
    @synchronized([NSObject class])
    {
        if (handles == nil) handles = [NSMutableDictionary dictionary];
        int64_t handle = ++nextHandle;
        handles[@(handle)] = ad;
        return handle;
    }
}

static id<NGAAd> Ad(int64_t handle)
{
    @synchronized([NSObject class]) { return handles[@(handle)]; }
}

static NSString* AdText(id<NGAAd> ad)
{
    return Join(@[ @(Retain(ad)), ad.placementId, ad.opportunityId, @(ad.expiresAtMillis) ]);
}

// --- the proxies: each keeps its token, and the moment native lets go of one, C# lets go of the callback it stands for,
// as a Java proxy's collection does on Android.

@interface NGABridgeProxy : NSObject
@property (nonatomic) int64_t token;
@end

@implementation NGABridgeProxy
- (void)dealloc
{
    int64_t token = _token;
    if (token == 0) return;
    dispatch_async(dispatch_get_main_queue(), ^{ Send(token, kRelease, @""); });
}
@end

@interface NGABridgeListener : NGABridgeProxy <NGAListener>
@end

@implementation NGABridgeListener
- (void)onInitialized:(NGAInitResult*)result
{
    NSMutableArray* fields = [NSMutableArray arrayWithArray:@[ @(result.success ? 1 : 0), @(result.fromCache ? 1 : 0) ]];
    [fields addObjectsFromArray:ErrorFields(result.error)];
    Send(self.token, kInitialized, Join(fields));
}
- (void)onError:(NGAError*)error { Send(self.token, kError, Join(ErrorFields(error))); }
- (void)onAdClosedAfterRestartWithPlacementId:(NSString*)placementId opportunityId:(NSString*)opportunityId rewardGranted:(BOOL)rewardGranted
{
    Send(self.token, kClosedAfterRestart, Join(@[ placementId, opportunityId, @(rewardGranted ? 1 : 0) ]));
}
@end

@interface NGABridgeLoad : NGABridgeProxy <NGAAdLoadCallback>
@end

@implementation NGABridgeLoad
- (void)onLoaded:(id<NGAAd>)ad { Send(self.token, kLoaded, AdText(ad)); }
- (void)onFailedWithPlacementId:(NSString*)placementId error:(NGAError*)error
{
    NSMutableArray* fields = [NSMutableArray arrayWithObject:placementId];
    [fields addObjectsFromArray:ErrorFields(error)];
    Send(self.token, kLoadFailed, Join(fields));
}
- (void)onExpired:(id<NGAAd>)ad { Send(self.token, kExpired, AdText(ad)); }
@end

@interface NGABridgeShow : NGABridgeProxy <NGAAdShowListener>
@end

@implementation NGABridgeShow
- (void)onShowStarted:(id<NGAAd>)ad { Send(self.token, kShowStarted, @""); }
- (void)onClicked:(id<NGAAd>)ad { Send(self.token, kClicked, @""); }
- (void)onRewardResult:(id<NGAAd>)ad result:(NGARewardResult*)result
{
    Send(self.token, kReward, Join(@[ @(result.granted ? 1 : 0), @((NSInteger)result.denial) ]));
}
- (void)onCompleted:(id<NGAAd>)ad completion:(enum NGAShowCompletion)completion
{
    Send(self.token, kCompleted, Join(@[ @((NSInteger)completion) ]));
}
- (void)onClosed:(id<NGAAd>)ad { Send(self.token, kClosed, @""); }
- (void)onShowFailed:(id<NGAAd>)ad error:(NGAError*)error
{
    NSMutableArray* fields = [NSMutableArray arrayWithObject:@((NSInteger)ad.state)];
    [fields addObjectsFromArray:ErrorFields(error)];
    Send(self.token, kShowFailed, Join(fields));
}
@end

// One proxy for one C# callback object while native holds it, so native sees one registration for it on every placement.
static NGABridgeLoad* LoadProxy(int64_t token)
{
    static NSMapTable<NSNumber*, NGABridgeLoad*>* proxies;
    @synchronized([NGABridgeLoad class])
    {
        if (proxies == nil) proxies = [NSMapTable strongToWeakObjectsMapTable];
        NGABridgeLoad* proxy = [proxies objectForKey:@(token)];
        if (proxy == nil)
        {
            proxy = [NGABridgeLoad new];
            proxy.token = token;
            [proxies setObject:proxy forKey:@(token)];
        }
        return proxy;
    }
}

static NSString* ResultText(NGAResult* r)
{
    if (r == nil) return @"\x1e";
    NSMutableArray* fields = [NSMutableArray arrayWithObject:@(r.accepted ? 1 : 0)];
    [fields addObjectsFromArray:ErrorFields(r.error)];
    return Join(fields);
}

static NGAConsent* Consent(int32_t analytics, int32_t ads)
{
    return analytics < 0 ? nil : [[NGAConsent alloc] initWithAnalytics:(enum NGAConsentState)analytics adsPersonalization:(enum NGAConsentState)ads];
}

extern "C" {

void nga_bridge_set_callback(NGABridgeCallback callback) { bridgeCallback = callback; }

// `hasConfig` 0 passes nil, as a C# null config is.
void nga_initialize(int32_t hasConfig, const char* appKey, int32_t analytics, int32_t ads, int32_t testMode, int64_t listener)
{
    NGAConfig* config = nil;
    if (hasConfig)
    {
        NGAConfigBuilder* builder = [[NGAConfigBuilder alloc] initWithAppKey:Str(appKey) consent:Consent(analytics, ads)];
        config = [[builder testMode:testMode != 0] build];
    }
    NGABridgeListener* proxy = nil;
    if (listener != 0)
    {
        proxy = [NGABridgeListener new];
        proxy.token = listener;
    }
    [NGASDK initializeWithConfig:config listener:proxy];
}

char* nga_sdk_version(void) { return Copy([NGASDK getSdkVersion]); }

void nga_set_log_level(int32_t level) { [NGASDK setLogLevel:(enum NGALogLevel)level]; }

void nga_set_consent(int32_t analytics, int32_t ads) { [NGASDK setConsent:Consent(analytics, ads)]; }

char* nga_get_consent(void)
{
    NGAConsent* c = [NGASDK getConsent];
    return Copy(Join(@[ @((NSInteger)c.analytics), @((NSInteger)c.adsPersonalization) ]));
}

void nga_set_age(int32_t age) { [NGASDK setAge:(enum NGAAgeState)age]; }

int32_t nga_get_age(void) { return (int32_t)[NGASDK getAge]; }

char* nga_set_customer_user_id(const char* userId) { return Copy(ResultText([NGASDK setCustomerUserId:Str(userId)])); }

char* nga_log_ad_revenue(int32_t hasRevenue, int32_t mediation, int64_t micros, const char* currency, int32_t precision, const char* network,
    const char* adUnitId, int32_t adFormat, const char* placement, const char* networkPlacement, const char* creativeId, const char* advertiserDomain,
    const char* impressionId)
{
    if (!hasRevenue) return Copy(ResultText([NGASDK logAdRevenue:nil]));
    NGAAdRevenueBuilder* b = [[NGAAdRevenueBuilder alloc] initWithMediation:(enum NGAAdRevenueMediation)mediation revenueMicros:micros
                                                                   currency:Str(currency) precision:(enum NGARevenuePrecision)precision];
    if (network != NULL) [b network:Str(network)];
    if (adUnitId != NULL) [b adUnitId:Str(adUnitId)];
    if (adFormat != 0) [b adFormat:(enum NGAAdFormat)adFormat];
    if (placement != NULL) [b placement:Str(placement)];
    if (networkPlacement != NULL) [b networkPlacement:Str(networkPlacement)];
    if (creativeId != NULL) [b creativeId:Str(creativeId)];
    if (advertiserDomain != NULL) [b advertiserDomain:Str(advertiserDomain)];
    if (impressionId != NULL) [b impressionId:Str(impressionId)];
    return Copy(ResultText([NGASDK logAdRevenue:[b build]]));
}

void nga_load_ad(const char* placementId, int64_t callback)
{
    [NGASDK loadAd:Str(placementId) callback:callback == 0 ? nil : LoadProxy(callback)];
}

void nga_set_impression_listener(int32_t on)
{
    if (!on)
    {
        NGASDK.impressionListener = nil;
        return;
    }
    NGASDK.impressionListener = ^(NGAImpression* i) {
        Send(0, kImpression, Join(@[ i.placementId, i.opportunityId, @((NSInteger)i.format), @(i.revenueMicros), i.currency, @((NSInteger)i.precision),
            Field(i.campaignId), Field(i.creativeId), Field(i.advertiserBundle), Field(i.advertiserDomain) ]));
    };
}

void nga_set_muted(int32_t muted) { [NGASDK setMuted:muted != 0]; }

int32_t nga_is_muted(void) { return [NGASDK isMuted] ? 1 : 0; }

int32_t nga_ad_state(int64_t handle) { return (int32_t)Ad(handle).state; }

int32_t nga_ad_is_ready(int64_t handle) { return Ad(handle).isReady ? 1 : 0; }

// Full-screen ads and the banner are shown from Unity's root view controller, read on the main thread.
void nga_ad_show(int64_t handle, int64_t listener)
{
    id<NGAAd> ad = Ad(handle);
    NGABridgeShow* proxy = [NGABridgeShow new];
    proxy.token = listener;
    dispatch_async(dispatch_get_main_queue(), ^{ [ad showFromViewController:UnityGetGLViewController() listener:proxy]; });
}

void nga_ad_close(int64_t handle) { [Ad(handle) close]; }

void nga_ad_release(int64_t handle)
{
    @synchronized([NSObject class]) { [handles removeObjectForKey:@(handle)]; }
}

}
