#import <Cocoa/Cocoa.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreAudio/CoreAudio.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreGraphics/CoreGraphics.h>
#import <Foundation/Foundation.h>
#import <fcntl.h>
#import <unistd.h>
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#endif

#import "fishhook.h"
#import <stdarg.h>
#import <sys/socket.h>
#import <sys/un.h>
#import <sys/poll.h>

static int (*orig_close)(int) = NULL;
static int (*orig_open)(const char *, int, ...) = NULL;
static int (*orig_connect)(int, const struct sockaddr *, socklen_t) = NULL;

static const char *FindRealDiscordSocket(void)
{
    static char foundPath[PATH_MAX] = {0};
    const char *tmp = getenv("TMPDIR");
    if (!tmp) tmp = "/tmp/";
    char candidate[PATH_MAX];
    for (int i = 0; i < 10; i++) {
        snprintf(candidate, sizeof(candidate), "%sdiscord-ipc-%d", tmp, i);
        if (access(candidate, F_OK) == 0) {
            strncpy(foundPath, candidate, sizeof(foundPath) - 1);
            return foundPath;
        }
    }
    for (int i = 0; i < 10; i++) {
        snprintf(candidate, sizeof(candidate), "/tmp/discord-ipc-%d", i);
        if (access(candidate, F_OK) == 0) {
            strncpy(foundPath, candidate, sizeof(foundPath) - 1);
            return foundPath;
        }
    }
    return NULL;
}

static BOOL SendDiscordFrame(int sock, uint32_t opcode, NSData *jsonData)
{
    if (sock < 0 || !jsonData) return NO;
    uint32_t len = (uint32_t)[jsonData length];
    uint32_t header[2];
    header[0] = CFSwapInt32HostToLittle(opcode);
    header[1] = CFSwapInt32HostToLittle(len);
    if (send(sock, header, sizeof(header), 0) != sizeof(header)) return NO;
    const char *bytes = (const char *)[jsonData bytes];
    size_t total = 0;
    while (total < len) {
        ssize_t sent = send(sock, bytes + total, len - total, 0);
        if (sent <= 0) return NO;
        total += sent;
    }
    return YES;
}

static BOOL ReadDiscordFrame(int sock, uint32_t *outOpcode, char *outBuffer, size_t maxLen)
{
    if (sock < 0) return NO;
    struct pollfd pfd;
    pfd.fd = sock;
    pfd.events = POLLIN;
    pfd.revents = 0;
    int pr = poll(&pfd, 1, 1500);
    if (pr <= 0) return NO;
    uint32_t header[2];
    ssize_t recvd = recv(sock, header, sizeof(header), MSG_WAITALL);
    if (recvd != sizeof(header)) return NO;
    uint32_t op = CFSwapInt32LittleToHost(header[0]);
    uint32_t len = CFSwapInt32LittleToHost(header[1]);
    if (outOpcode) *outOpcode = op;
    if (len >= maxLen) len = (uint32_t)(maxLen - 1);
    recvd = recv(sock, outBuffer, len, MSG_WAITALL);
    if (recvd <= 0) return NO;
    outBuffer[recvd] = '\0';
    return YES;
}

static void DrainDiscordReplies(int sock)
{
    if (sock < 0) return;
    char drainBuf[1024];
    while (recv(sock, drainBuf, sizeof(drainBuf), MSG_DONTWAIT) > 0) {
    }
}

static BOOL IsDiscordRPCEnabledInSettings(void)
{
    NSString *home = NSHomeDirectory();
    NSString *settingsPath = [home stringByAppendingPathComponent:@"Library/Application Support/com.Shinymoon.MateEngineX/settings.json"];
    NSData *data = [NSData dataWithContentsOfFile:settingsPath];
    if (!data) return YES;
    
    NSError *err = nil;
    NSDictionary *dict = [NSJSONSerialization JSONObjectWithData:data options:0 error:&err];
    if (dict && [dict isKindOfClass:[NSDictionary class]]) {
        NSNumber *val = dict[@"enableDiscordRPC"];
        if (val && [val isKindOfClass:[NSNumber class]]) {
            return [val boolValue];
        }
    }
    return YES;
}

static int64_t gGameStartTimeMs = 0;

static void StartDiscordBackgroundService(void)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        gGameStartTimeMs = (int64_t)([[NSDate date] timeIntervalSince1970] * 1000);
        dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_LOW, 0), ^{
            int sock = -1;
            BOOL hasSentInitialPresence = NO;
            BOOL lastDragging = NO;
            
            while (1) {
                @autoreleasepool {
                    BOOL isEnabled = IsDiscordRPCEnabledInSettings();
                    if (isEnabled) {
                        if (sock < 0) {
                            const char *sockPath = FindRealDiscordSocket();
                            if (sockPath) {
                                int s = socket(AF_UNIX, SOCK_STREAM, 0);
                                if (s >= 0) {
                                    if (s <= 2) {
                                        int safe = fcntl(s, F_DUPFD, 3);
                                        if (safe >= 3) {
                                            close(s);
                                            s = safe;
                                        }
                                    }
                                    int nosig = 1;
                                    setsockopt(s, SOL_SOCKET, SO_NOSIGPIPE, &nosig, sizeof(nosig));
                                    
                                    struct sockaddr_un addr;
                                    memset(&addr, 0, sizeof(addr));
                                    addr.sun_len = sizeof(struct sockaddr_un);
                                    addr.sun_family = AF_UNIX;
                                    strncpy(addr.sun_path, sockPath, sizeof(addr.sun_path) - 1);
                                    
                                    int cr = orig_connect ? orig_connect(s, (const struct sockaddr *)&addr, sizeof(addr))
                                                          : connect(s, (const struct sockaddr *)&addr, sizeof(addr));
                                    if (cr == 0) {
                                        NSDictionary *hsDict = @{@"v": @1, @"client_id": @"1358821058102296816"};
                                        NSData *hsData = [NSJSONSerialization dataWithJSONObject:hsDict options:0 error:nil];
                                        if (SendDiscordFrame(s, 0, hsData)) {
                                            uint32_t op = 0;
                                            char replyBuf[2048];
                                            if (ReadDiscordFrame(s, &op, replyBuf, sizeof(replyBuf))) {
                                                sock = s;
                                                hasSentInitialPresence = NO;
                                                lastDragging = NO;
                                                NSLog(@"[MacDiscord] Connected to Discord IPC successfully!");
                                            } else {
                                                close(s);
                                            }
                                        } else {
                                            close(s);
                                        }
                                    } else {
                                        close(s);
                                    }
                                }
                            }
                        }
                        
                        if (sock >= 0) {
                            BOOL isMouseDown = CGEventSourceButtonState(kCGEventSourceStateCombinedSessionState, kCGMouseButtonLeft);
                            NSRunningApplication *frontApp = [[NSWorkspace sharedWorkspace] frontmostApplication];
                            BOOL isFront = [frontApp.bundleIdentifier isEqualToString:@"com.Shinymoon.MateEngineX"];
                            BOOL isDragging = (isMouseDown && isFront);
                            
                            if (!hasSentInitialPresence || isDragging != lastDragging) {
                                hasSentInitialPresence = YES;
                                lastDragging = isDragging;
                                
                                NSString *details = isDragging ? @"Throwing around my desktop pet!" : @"Playing with my desktop pet";
                                NSString *state = isDragging ? @"Help!!" : @"Just vibing";
                                
                                NSDictionary *actDict = @{
                                    @"cmd": @"SET_ACTIVITY",
                                    @"args": @{
                                        @"pid": @(getpid()),
                                        @"activity": @{
                                            @"state": state,
                                            @"details": details,
                                            @"timestamps": @{@"start": @(gGameStartTimeMs)},
                                            @"assets": @{
                                                @"large_image": @"logo",
                                                @"large_text": @"MateEngine",
                                                @"small_image": @"steam-icon",
                                                @"small_text": @"Steam Edition"
                                            },
                                            @"buttons": @[
                                                @{@"label": @"Visit Website", @"url": @"https://mateengine.com"}
                                            ]
                                        }
                                    },
                                    @"nonce": [NSString stringWithFormat:@"%lld", (long long)[[NSDate date] timeIntervalSince1970]]
                                };
                                NSData *actData = [NSJSONSerialization dataWithJSONObject:actDict options:0 error:nil];
                                if (!SendDiscordFrame(sock, 1, actData)) {
                                    close(sock);
                                    sock = -1;
                                    hasSentInitialPresence = NO;
                                } else {
                                    NSLog(@"[MacDiscord] Sent presence: %@ / %@", details, state);
                                }
                            }
                            
                            if (sock >= 0) {
                                DrainDiscordReplies(sock);
                                int err = 0;
                                socklen_t elen = sizeof(err);
                                if (getsockopt(sock, SOL_SOCKET, SO_ERROR, &err, &elen) != 0 || err != 0) {
                                    close(sock);
                                    sock = -1;
                                    hasSentInitialPresence = NO;
                                }
                            }
                        }
                    } else {
                        if (sock >= 0) {
                            NSDictionary *clearDict = @{
                                @"cmd": @"SET_ACTIVITY",
                                @"args": @{
                                    @"pid": @(getpid()),
                                    @"activity": [NSNull null]
                                },
                                @"nonce": [NSString stringWithFormat:@"%lld", (long long)[[NSDate date] timeIntervalSince1970]]
                            };
                            NSData *clearData = [NSJSONSerialization dataWithJSONObject:clearDict options:0 error:nil];
                            SendDiscordFrame(sock, 1, clearData);
                            close(sock);
                            sock = -1;
                            hasSentInitialPresence = NO;
                            NSLog(@"[MacDiscord] RPC disabled in settings; disconnected.");
                        }
                    }
                }
                [NSThread sleepForTimeInterval:2.0];
            }
        });
    });
}

static int my_connect(int sockfd, const struct sockaddr *addr, socklen_t addrlen)
{
    if (addr) {
        const struct sockaddr_un *un = (const struct sockaddr_un *)addr;
        if (addr->sa_family == AF_UNIX || un->sun_family == AF_UNIX) {
            if (strstr(un->sun_path, "discord-ipc") != NULL || strstr(un->sun_path, "CoreFxPipe") != NULL) {
                // Block Mono's broken NamedPipe attempts so it fails cleanly without spinning or closing fds
                errno = ECONNREFUSED;
                return -1;
            }
        }
    }
    int res = orig_connect ? orig_connect(sockfd, addr, addrlen) : connect(sockfd, addr, addrlen);
    return res;
}

static int my_close(int fd)
{
    if (fd >= 0 && fd <= 2) {
        NSLog(@"[FD_GUARD] Blocked attempt to close standard fd %d!", fd);
        return 0; // Prevent standard descriptors from ever being closed!
    }
    if (orig_close) {
        return orig_close(fd);
    }
    return close(fd);
}

static int my_open(const char *path, int oflag, ...)
{
    mode_t mode = 0;
    if (oflag & O_CREAT) {
        va_list args;
        va_start(args, oflag);
        mode = (mode_t)va_arg(args, int);
        va_end(args);
    }

    int fd;
    if (orig_open) {
        fd = (oflag & O_CREAT) ? orig_open(path, oflag, mode) : orig_open(path, oflag);
    } else {
        fd = (oflag & O_CREAT) ? open(path, oflag, mode) : open(path, oflag);
    }

    if (fd >= 0 && fd <= 2) {
        NSLog(@"[FD_GUARD] open(\"%s\") returned standard fd %d! Reallocating to safe fd...", path, fd);
        int safe_fd = fcntl(fd, F_DUPFD, 3);
        if (safe_fd >= 3) {
            int devnull = (orig_open ? orig_open("/dev/null", O_RDWR) : open("/dev/null", O_RDWR));
            if (devnull >= 0) {
                dup2(devnull, fd);
                if (devnull > 2) {
                    if (orig_close) orig_close(devnull); else close(devnull);
                }
            }
            NSLog(@"[FD_GUARD] Moved file \"%s\" from fd %d -> safe fd %d", path, safe_fd, fd);
            return safe_fd;
        }
    }
    return fd;
}

// Guard standard file descriptors 0, 1, 2 on startup.
void MacAudio_FixStandardFileDescriptors(void)
{
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        int devnull = open("/dev/null", O_RDWR);
        if (devnull >= 0) {
            for (int fd = 0; fd <= 2; fd++) {
                if (fcntl(fd, F_GETFL) < 0) {
                    dup2(devnull, fd);
                    NSLog(@"[MacAudioMonitor] Initial dup2 redirected closed fd %d to /dev/null", fd);
                }
            }
            if (devnull > 2) close(devnull);
        }

        struct rebinding rebindings[] = {
            {"close",   (void *)my_close,   (void **)&orig_close},
            {"open",    (void *)my_open,    (void **)&orig_open},
            {"connect", (void *)my_connect, (void **)&orig_connect}
        };
        rebind_symbols(rebindings, 3);
        NSLog(@"[MacAudioMonitor] Standard fd guards + fishhook close/open/connect hooks installed successfully.");
    });
}

@interface MacAudioFDGuard : NSObject
@end

@implementation MacAudioFDGuard
+ (void)load
{
    MacAudio_FixStandardFileDescriptors();
    StartDiscordBackgroundService();
}
@end

// ─────────────────────────────────────────────────────────────────────────────
// System-wide audio capture via ScreenCaptureKit (macOS 13+).
//
// The old implementation tapped an empty AVAudioEngine mixer, which can only
// hear audio routed through that engine — i.e. nothing — so macOS could never
// react to music from other apps. An SCStream with capturesAudio=YES captures
// the whole system output mix (any music player), which is what "dance to the
// music the user is playing" actually needs.
//
// Exposed C functions (see MacAudioMonitorBinding.cs):
//   MacAudio_Start()                 — start the SCStream capture (lazy, retries)
//   MacAudio_Stop()                  — stop and release everything
//   MacAudio_IsOutputActive()        — 1 = sound above threshold,
//                                      0 = capture running but silent,
//                                     -1 = capture unavailable (no permission /
//                                          macOS < 13 / failed to start)
//   MacAudio_SystemCaptureAvailable()— 1 if the capture stream is running
//   MacAudio_HasCapturePermission()  — 1 if Screen Recording is authorized
//   MacAudio_GetDefaultDeviceName()  — unchanged (CoreAudio device name)
// ─────────────────────────────────────────────────────────────────────────────

static volatile float gPeakLevel = 0.0f; // written on the audio queue, read from the poll thread
static id gSystemAudioStream = nil;      // SCStream, kept alive while capturing
static id gAudioOutputDelegate = nil;    // SCStreamOutput + SCStreamDelegate
static dispatch_queue_t gAudioQueue = nil;
static int gCaptureState = 0;            // 0 = not started, 1 = starting, 2 = running, 3 = unavailable
static BOOL gRequestedPermission = NO;

#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
@interface MacAudioTapDelegate : NSObject <SCStreamOutput, SCStreamDelegate>
@end

@implementation MacAudioTapDelegate

- (void)stream:(SCStream *)stream didOutputSampleBuffer:(CMSampleBufferRef)sampleBuffer ofType:(SCStreamOutputType)type API_AVAILABLE(macos(13.0))
{
    (void)stream;
    if (@available(macOS 13.0, *)) {
        if (type != SCStreamOutputTypeAudio || sampleBuffer == NULL) return;

        // SCK audio sample buffers are PLAIN data buffers (no AudioBufferList),
        // so read the raw CMBlockBuffer and scan all Float32 samples. This works
        // for both interleaved and planar layouts since every sample is present.
        CMBlockBufferRef dataBuffer = CMSampleBufferGetDataBuffer(sampleBuffer);
        if (!dataBuffer) {
            NSLog(@"[MacAudioMonitor] no data buffer in audio sample");
            return;
        }
        size_t lenAtOffset = 0;
        size_t totalLen = 0;
        char *bytes = NULL;
        OSStatus status = CMBlockBufferGetDataPointer(dataBuffer, 0, &lenAtOffset, &totalLen, &bytes);
        if (status != noErr || bytes == NULL || lenAtOffset == 0) {
            NSLog(@"[MacAudioMonitor] data buffer read failed: status=%d len=%zu", (int)status, lenAtOffset);
            return;
        }

        float peak = 0.0f;
        const float *samples = (const float *)bytes;
        size_t count = lenAtOffset / sizeof(float);
        for (size_t j = 0; j < count; j++) {
            float v = fabsf(samples[j]);
            if (v > peak) peak = v;
        }

        gPeakLevel = peak;
    }
}

- (void)stream:(SCStream *)stream didStopWithError:(NSError *)error API_AVAILABLE(macos(13.0))
{
    (void)stream;
    if (error) {
        NSLog(@"[MacAudioMonitor] SCStream stopped with error: %@", error);
    }
    gCaptureState = 0; // reset so the next poll can retry
}

@end
#endif

static void MacAudio_StartCapture(void)
{
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
    if (@available(macOS 13.0, *)) {
        if (gCaptureState != 0) return;

        if (!CGPreflightScreenCaptureAccess()) {
            // One-time prompt for Screen Recording permission (main thread).
            NSLog(@"[MacAudioMonitor] Screen Recording permission missing; requesting...");
            if (!gRequestedPermission) {
                gRequestedPermission = YES;
                CGRequestScreenCaptureAccess();
            }
            gCaptureState = 3; // unavailable for now; retried lazily if granted later
            return;
        }

        gCaptureState = 1;
        [SCShareableContent getShareableContentExcludingDesktopWindows:YES
                                                  onScreenWindowsOnly:NO
                                                  completionHandler:^(SCShareableContent *content, NSError *error) {
            if (!content || error) {
                NSLog(@"[MacAudioMonitor] SCShareableContent error: %@", error);
                gCaptureState = 3;
                return;
            }
            SCDisplay *display = content.displays.firstObject;
            if (!display) {
                NSLog(@"[MacAudioMonitor] No display found for system audio capture");
                gCaptureState = 3;
                return;
            }

            SCContentFilter *filter = [[SCContentFilter alloc] initWithDisplay:display excludingWindows:@[]];
            SCStreamConfiguration *config = [[SCStreamConfiguration alloc] init];
            config.width = 2;   // audio-only; minimal video to satisfy SCK
            config.height = 2;
            config.capturesAudio = YES;
            config.excludesCurrentProcessAudio = YES; // don't self-trigger on the avatar's own TTS/sounds
            config.sampleRate = 48000;
            config.channelCount = 2;

            gAudioQueue = dispatch_queue_create("com.shinymoon.mateengine.audiocapture", DISPATCH_QUEUE_SERIAL);
            gAudioOutputDelegate = [[MacAudioTapDelegate alloc] init];
            SCStream *stream = [[SCStream alloc] initWithFilter:filter configuration:config delegate:gAudioOutputDelegate];
            if (!stream) {
                NSLog(@"[MacAudioMonitor] Failed to create SCStream");
                gCaptureState = 3;
                return;
            }
            gSystemAudioStream = stream;
            NSError *outputError = nil;
            [stream addStreamOutput:gAudioOutputDelegate
                               type:SCStreamOutputTypeAudio
                 sampleHandlerQueue:gAudioQueue
                              error:&outputError];
            if (outputError) {
                NSLog(@"[MacAudioMonitor] addStreamOutput error: %@", outputError);
                gSystemAudioStream = nil;
                gCaptureState = 3;
                return;
            }
            [stream startCaptureWithCompletionHandler:^(NSError *startError) {
                if (startError) {
                    NSLog(@"[MacAudioMonitor] SCStream start error: %@", startError);
                    gSystemAudioStream = nil;
                    gCaptureState = 3;
                } else {
                    NSLog(@"[MacAudioMonitor] SCStream system audio capture started.");
                    gCaptureState = 2;
                }
            }];
        }];
        return;
    }
#endif
    gCaptureState = 3; // macOS < 13: no SCK audio capture
}

static void MacAudio_StopCapture(void)
{
#if __has_include(<ScreenCaptureKit/ScreenCaptureKit.h>)
    if (@available(macOS 13.0, *)) {
        SCStream *stream = (SCStream *)gSystemAudioStream;
        if (stream) {
            [stream stopCaptureWithCompletionHandler:nil];
        }
    }
#endif
    gSystemAudioStream = nil;
    gAudioOutputDelegate = nil;
    gAudioQueue = nil;
    gCaptureState = 0;
    gPeakLevel = 0.0f;
}

void MacAudio_Start(void)
{
    MacAudio_FixStandardFileDescriptors();
    if (gCaptureState == 2) return;
    // If we were marked unavailable only because permission was missing, retry
    // now that it may have been granted (System Settings → Privacy → Screen Recording).
    if (gCaptureState == 3) {
        if (@available(macOS 13.0, *)) {
            if (CGPreflightScreenCaptureAccess()) {
                gCaptureState = 0;
                MacAudio_StartCapture();
            }
        }
        return;
    }
    MacAudio_StartCapture();
}

void MacAudio_Stop(void)
{
    MacAudio_StopCapture();
}

// 1 = system audio above threshold, 0 = capture running but silent, -1 = unavailable.
int MacAudio_IsOutputActive(void)
{
    if (gCaptureState == 0) MacAudio_Start();
    if (gCaptureState != 2) return -1;
    return gPeakLevel > 0.01f ? 1 : 0;
}

// 1 if the SCStream audio capture is currently running, else 0.
int MacAudio_SystemCaptureAvailable(void)
{
    return (gCaptureState == 2) ? 1 : 0;
}

// 1 if Screen Recording permission is granted.
int MacAudio_HasCapturePermission(void)
{
    if (@available(macOS 10.15, *)) {
        return CGPreflightScreenCaptureAccess() ? 1 : 0;
    }
    return 1;
}

// Returns the name of the default output device.
int MacAudio_GetDefaultDeviceName(char* buf, int bufLen)
{
    MacAudio_FixStandardFileDescriptors();
    if (!buf || bufLen <= 0) return -1;
    buf[0] = '\0';

    AudioObjectPropertyAddress addr = {
        kAudioHardwarePropertyDefaultOutputDevice,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain
    };
    AudioDeviceID deviceID = kAudioObjectUnknown;
    UInt32 dataSize = sizeof(AudioDeviceID);
    OSStatus status = AudioObjectGetPropertyData(kAudioObjectSystemObject, &addr, 0, NULL, &dataSize, &deviceID);
    if (status != noErr || deviceID == kAudioObjectUnknown) {
        strncpy(buf, "<unknown>", bufLen - 1);
        return -1;
    }

    AudioObjectPropertyAddress nameAddr = {
        kAudioObjectPropertyName,
        kAudioObjectPropertyScopeGlobal,
        kAudioObjectPropertyElementMain
    };
    CFStringRef cfName = NULL;
    dataSize = sizeof(CFStringRef);
    status = AudioObjectGetPropertyData(deviceID, &nameAddr, 0, NULL, &dataSize, &cfName);
    if (status != noErr || cfName == NULL) {
        strncpy(buf, "<name error>", bufLen - 1);
        return -1;
    }
    CFStringGetCString(cfName, buf, bufLen, kCFStringEncodingUTF8);
    CFRelease(cfName);
    return 0;
}
