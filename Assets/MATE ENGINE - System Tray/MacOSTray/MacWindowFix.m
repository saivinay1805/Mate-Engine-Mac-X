#import <Cocoa/Cocoa.h>
#import <objc/runtime.h>
#import <fcntl.h>
#import <unistd.h>

// Guard standard file descriptors 0, 1, 2 on startup.
// On macOS, when launched without standard streams (e.g. from Finder or LaunchServices),
// fd 0 (stdin) is closed. If a background socket (such as Discord RPC when Discord is offline)
// closes, fd 0 becomes free. When Mono subsequently opens a file (like saving settings.json),
// the OS allocates fd 0, causing Mono's internal handle table to abort with:
// "mono_fdhandle_insert: duplicate File fd 0".
__attribute__((constructor))
static void FixStandardFileDescriptors(void)
{
    int fd;
    while ((fd = open("/dev/null", O_RDWR)) >= 0 && fd <= 2) {
        // Keeps 0, 1, 2 safely occupied
    }
    if (fd > 2) {
        close(fd);
    }
}

// Swizzle NSWindow's constrainFrameRect:toScreen: to prevent macOS from
// forcing the window back below the menu bar when it loses focus.
static NSRect swizzled_constrainFrameRect(id self, SEL _cmd, NSRect frameRect, NSScreen *screen)
{
    return frameRect;
}

static id gActivity = nil;

void MacWindowFix_Install(void)
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        Method original = class_getInstanceMethod([NSWindow class],
                            @selector(constrainFrameRect:toScreen:));
        if (original) {
            method_setImplementation(original,
                (IMP)swizzled_constrainFrameRect);
        }
        
        NSActivityOptions options = NSActivityUserInitiatedAllowingIdleSystemSleep | NSActivityLatencyCritical;
        gActivity = [[NSProcessInfo processInfo] beginActivityWithOptions:options reason:@"High FPS requirement"];
    });
}
