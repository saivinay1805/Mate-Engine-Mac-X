#import <Cocoa/Cocoa.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <unistd.h>

static void logMsg(const char *fmt, ...) {
    FILE *f = fopen("/tmp/matesfbhelper.log", "a");
    if (!f) return;
    va_list args;
    va_start(args, fmt);
    time_t now = time(NULL);
    struct tm *tm_info = localtime(&now);
    char timeStr[20];
    strftime(timeStr, sizeof(timeStr), "%H:%M:%S", tm_info);
    fprintf(f, "[%s] ", timeStr);
    vfprintf(f, fmt, args);
    va_end(args);
    fclose(f);
}

static NSString* extractBetween(NSString* text, NSString* start, NSString* end) {
    if (!text || !start || !end) return nil;
    NSRange rStart = [text rangeOfString:start];
    if (rStart.location == NSNotFound) return nil;
    NSUInteger pStart = rStart.location + rStart.length;
    NSRange rEnd = [text rangeOfString:end options:0 range:NSMakeRange(pStart, text.length - pStart)];
    if (rEnd.location == NSNotFound) return nil;
    return [text substringWithRange:NSMakeRange(pStart, rEnd.location - pStart)];
}

@interface SFBAppDelegate : NSObject <NSApplicationDelegate>
@property (nonatomic, copy) NSString *scriptContent;
@end

@implementation SFBAppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)aNotification {
    logMsg("applicationDidFinishLaunching called\n");
    [NSApp activateIgnoringOtherApps:YES];

    BOOL isSave = self.scriptContent && [self.scriptContent containsString:@"choose file name"];
    BOOL isFolder = self.scriptContent && !isSave && [self.scriptContent containsString:@"choose folder"];
    BOOL isChooseFile = !isSave && !isFolder;
    BOOL multiselect = self.scriptContent && [self.scriptContent containsString:@"with multiple selections allowed"];

    NSString *prompt = extractBetween(self.scriptContent, @"prompt \"", @"\"");
    NSString *defaultLoc = extractBetween(self.scriptContent, @"POSIX file \"", @"\"");
    NSString *defaultName = extractBetween(self.scriptContent, @"default name \"", @"\"");

    logMsg("Dialog setup: isSave=%d, isFolder=%d, isChooseFile=%d, prompt='%s'\n",
           isSave, isFolder, isChooseFile, prompt ? [prompt UTF8String] : "");

    if (isSave) {
        NSSavePanel *panel = [NSSavePanel savePanel];
        panel.canCreateDirectories = YES;
        if (prompt) panel.title = prompt;
        if (defaultName) panel.nameFieldStringValue = defaultName;
        if (defaultLoc && [[NSFileManager defaultManager] fileExistsAtPath:defaultLoc]) {
            panel.directoryURL = [NSURL fileURLWithPath:defaultLoc];
        } else {
            panel.directoryURL = [NSURL fileURLWithPath:[NSHomeDirectory() stringByAppendingPathComponent:@"Downloads"]];
        }

        [panel center];

        logMsg("Calling [savePanel beginWithCompletionHandler:]...\n");
        [panel beginWithCompletionHandler:^(NSModalResponse result) {
            logMsg("SavePanel completed with result: %ld\n", (long)result);
            if (result == NSModalResponseOK && panel.URL) {
                printf("%s", [panel.URL.path UTF8String]);
                fflush(stdout);
                logMsg("Save output: %s\n", [panel.URL.path UTF8String]);
            }
            [NSApp terminate:nil];
        }];

        panel.level = 1000;
        [panel orderFrontRegardless];
        [panel makeKeyAndOrderFront:nil];
        [NSApp activateIgnoringOtherApps:YES];

        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.1 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
            panel.level = 1000;
            [panel orderFrontRegardless];
            [panel makeKeyAndOrderFront:nil];
            [NSApp activateIgnoringOtherApps:YES];
        });
    } else {
        NSOpenPanel *panel = [NSOpenPanel openPanel];
        panel.canChooseFiles = isChooseFile;
        panel.canChooseDirectories = isFolder;
        panel.allowsMultipleSelection = multiselect;
        panel.allowsOtherFileTypes = YES;

        if (prompt) {
            panel.title = prompt;
            panel.message = prompt;
        } else {
            panel.title = @"Select Model File";
            panel.message = @"Select a VRM or Model file";
        }

        if (defaultLoc && [[NSFileManager defaultManager] fileExistsAtPath:defaultLoc]) {
            panel.directoryURL = [NSURL fileURLWithPath:defaultLoc];
        } else {
            panel.directoryURL = [NSURL fileURLWithPath:[NSHomeDirectory() stringByAppendingPathComponent:@"Downloads"]];
        }

        [panel center];

        logMsg("Calling [openPanel beginWithCompletionHandler:]...\n");
        [panel beginWithCompletionHandler:^(NSModalResponse result) {
            logMsg("OpenPanel completed with result: %ld\n", (long)result);
            if (result == NSModalResponseOK) {
                NSMutableArray<NSString *> *paths = [NSMutableArray array];
                for (NSURL *url in panel.URLs) {
                    [paths addObject:url.path];
                }
                NSString *joined = [paths componentsJoinedByString:@"\x1c"];
                printf("%s", [joined UTF8String]);
                fflush(stdout);
                logMsg("Selected output: %s\n", [joined UTF8String]);
            } else {
                logMsg("User cancelled dialog\n");
            }
            [NSApp terminate:nil];
        }];

        panel.level = 1000;
        [panel orderFrontRegardless];
        [panel makeKeyAndOrderFront:nil];
        [NSApp activateIgnoringOtherApps:YES];

        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.1 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
            panel.level = 1000;
            [panel orderFrontRegardless];
            [panel makeKeyAndOrderFront:nil];
            [NSApp activateIgnoringOtherApps:YES];
        });
    }
}

@end

int main(int argc, const char * argv[]) {
    @autoreleasepool {
        logMsg("=== matesfbhelper started ===\n");
        for (int i = 0; i < argc; i++) {
            logMsg("arg[%d]: %s\n", i, argv[i]);
        }

        NSString *scriptContent = nil;
        NSString *cleanPath = nil;

        for (int i = 1; i < argc; i++) {
            NSString *arg = [NSString stringWithUTF8String:argv[i]];
            NSString *trimmed = [arg stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"\"' \t\r\n"]];
            if ([[NSFileManager defaultManager] fileExistsAtPath:trimmed]) {
                cleanPath = trimmed;
                break;
            }
        }

        if (!cleanPath && argc > 1) {
            NSMutableString *combined = [NSMutableString string];
            for (int i = 1; i < argc; i++) {
                if (i > 1) [combined appendString:@" "];
                [combined appendString:[NSString stringWithUTF8String:argv[i]]];
            }
            NSString *trimmed = [combined stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"\"' \t\r\n"]];
            if ([[NSFileManager defaultManager] fileExistsAtPath:trimmed]) {
                cleanPath = trimmed;
            }
        }

        if (cleanPath) {
            logMsg("Found script at: %s\n", [cleanPath UTF8String]);
            scriptContent = [NSString stringWithContentsOfFile:cleanPath encoding:NSUTF8StringEncoding error:nil];
        }

        NSApplication *app = [NSApplication sharedApplication];
        [app setActivationPolicy:NSApplicationActivationPolicyRegular];

        SFBAppDelegate *delegate = [[SFBAppDelegate alloc] init];
        delegate.scriptContent = scriptContent;
        app.delegate = delegate;

        logMsg("Starting [app run] event loop...\n");
        [app run];
        logMsg("Exited [app run]\n");
    }
    return 0;
}
