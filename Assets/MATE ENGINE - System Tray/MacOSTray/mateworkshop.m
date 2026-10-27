#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>
#import <unistd.h>

static NSString *gDataPath = nil;

@interface MateWorkshopApp : NSObject <NSApplicationDelegate, WKNavigationDelegate, WKScriptMessageHandler, NSWindowDelegate>
@property (nonatomic, strong) NSWindow *window;
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) NSString *htmlPath;
@property (nonatomic, strong) NSMutableDictionary<NSString *, NSTask *> *activeDownloads;
@property (nonatomic, strong) NSURLSessionDataTask *currentFetchTask;
+ (NSString *)baseDataPath;
@end

@implementation MateWorkshopApp

+ (NSString *)baseDataPath {
    if (gDataPath.length > 0) return gDataPath;
    return [NSHomeDirectory() stringByAppendingPathComponent:@"Library/Application Support/Shinymoon/MateEngineX"];
}

- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    self.activeDownloads = [NSMutableDictionary dictionary];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
    [self setupMainMenu];
    [self setupWindow];
    [self loadUI];
    [NSApp activateIgnoringOtherApps:YES];
}

- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)sender {
    return YES;
}

- (void)windowWillClose:(NSNotification *)notification {
    [NSApp terminate:nil];
}

- (void)setupMainMenu {
    NSMenu *mainMenu = [[NSMenu alloc] init];
    
    // App Menu
    NSMenuItem *appMenuItem = [[NSMenuItem alloc] init];
    NSMenu *appMenu = [[NSMenu alloc] init];
    [appMenu addItemWithTitle:@"About MateEngine Workshop" action:nil keyEquivalent:@""];
    [appMenu addItem:[NSMenuItem separatorItem]];
    [appMenu addItemWithTitle:@"Quit" action:@selector(terminate:) keyEquivalent:@"q"];
    [appMenuItem setSubmenu:appMenu];
    [mainMenu addItem:appMenuItem];
    
    // Edit Menu (for Cmd+C, Cmd+V, Cmd+A)
    NSMenuItem *editMenuItem = [[NSMenuItem alloc] init];
    NSMenu *editMenu = [[NSMenu alloc] initWithTitle:@"Edit"];
    [editMenu addItemWithTitle:@"Undo" action:@selector(undo:) keyEquivalent:@"z"];
    [editMenu addItemWithTitle:@"Redo" action:@selector(redo:) keyEquivalent:@"Z"];
    [editMenu addItem:[NSMenuItem separatorItem]];
    [editMenu addItemWithTitle:@"Cut" action:@selector(cut:) keyEquivalent:@"x"];
    [editMenu addItemWithTitle:@"Copy" action:@selector(copy:) keyEquivalent:@"c"];
    [editMenu addItemWithTitle:@"Paste" action:@selector(paste:) keyEquivalent:@"v"];
    [editMenu addItemWithTitle:@"Select All" action:@selector(selectAll:) keyEquivalent:@"a"];
    [editMenuItem setSubmenu:editMenu];
    [mainMenu addItem:editMenuItem];
    
    // Window Menu
    NSMenuItem *winMenuItem = [[NSMenuItem alloc] init];
    NSMenu *winMenu = [[NSMenu alloc] initWithTitle:@"Window"];
    [winMenu addItemWithTitle:@"Minimize" action:@selector(miniaturize:) keyEquivalent:@"m"];
    [winMenu addItemWithTitle:@"Close" action:@selector(performClose:) keyEquivalent:@"w"];
    [winMenuItem setSubmenu:winMenu];
    [mainMenu addItem:winMenuItem];
    
    [NSApp setMainMenu:mainMenu];
}

- (void)setupWindow {
    NSRect screenRect = [[NSScreen mainScreen] visibleFrame];
    CGFloat w = MIN(1240, screenRect.size.width * 0.88);
    CGFloat h = MIN(840, screenRect.size.height * 0.90);
    NSRect frame = NSMakeRect(0, 0, w, h);
    
    NSWindowStyleMask mask = NSWindowStyleMaskTitled | 
                             NSWindowStyleMaskClosable | 
                             NSWindowStyleMaskMiniaturizable | 
                             NSWindowStyleMaskResizable | 
                             NSWindowStyleMaskFullSizeContentView;
    
    self.window = [[NSWindow alloc] initWithContentRect:frame
                                              styleMask:mask
                                                backing:NSBackingStoreBuffered
                                                  defer:NO];
    self.window.delegate = self;
    [self.window center];
    self.window.title = @"MateEngine Workshop";
    self.window.titlebarAppearsTransparent = YES;
    self.window.titleVisibility = NSWindowTitleHidden;
    self.window.backgroundColor = [NSColor clearColor];
    self.window.opaque = NO;
    self.window.hasShadow = YES;
    self.window.minSize = NSMakeSize(880, 560);
    [self.window setMovableByWindowBackground:YES];
    
    // Liquid Glass background effect view
    NSVisualEffectView *effectView = [[NSVisualEffectView alloc] initWithFrame:self.window.contentView.bounds];
    effectView.material = NSVisualEffectMaterialUnderWindowBackground;
    effectView.blendingMode = NSVisualEffectBlendingModeBehindWindow;
    effectView.state = NSVisualEffectStateActive;
    effectView.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
    [self.window.contentView addSubview:effectView];
    
    // WebKit View Setup
    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    WKUserContentController *contentCtrl = [[WKUserContentController alloc] init];
    [contentCtrl addScriptMessageHandler:self name:@"mateWorkshop"];
    config.userContentController = contentCtrl;
    
    // Allow local file access
    [config.preferences setValue:@YES forKey:@"allowFileAccessFromFileURLs"];
    
    self.webView = [[WKWebView alloc] initWithFrame:effectView.bounds configuration:config];
    self.webView.navigationDelegate = self;
    self.webView.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
    
    // Make WebKit background transparent for true Liquid Glass vibrancy!
    [self.webView setValue:@NO forKey:@"drawsBackground"];
    if (@available(macOS 12.0, *)) {
        self.webView.underPageBackgroundColor = [NSColor clearColor];
    }
    
    [effectView addSubview:self.webView];
    [self.window makeKeyAndOrderFront:nil];
}

- (void)loadUI {
    // Find workshop_ui.html
    NSString *bundlePath = [[NSBundle mainBundle] bundlePath];
    NSArray *candidates = @[
        [[NSBundle mainBundle] pathForResource:@"workshop_ui" ofType:@"html"] ?: @"",
        [bundlePath stringByAppendingPathComponent:@"Contents/Resources/workshop_ui.html"],
        [NSHomeDirectory() stringByAppendingPathComponent:@"Documents/antigravity/clever-archimedes/Assets/MATE ENGINE - System Tray/MacOSTray/workshop_ui.html"],
        @"/Applications/MateEngineX.app/Contents/Resources/workshop_ui.html",
        @"/tmp/workshop_ui.html"
    ];
    
    for (NSString *cand in candidates) {
        if (cand.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:cand]) {
            self.htmlPath = cand;
            break;
        }
    }
    
    if (self.htmlPath) {
        NSURL *fileUrl = [NSURL fileURLWithPath:self.htmlPath];
        [self.webView loadFileURL:fileUrl allowingReadAccessToURL:[NSURL fileURLWithPath:@"/"]];
    } else {
        NSLog(@"[MateWorkshop] workshop_ui.html not found!");
    }
}

// WKScriptMessageHandler
- (void)userContentController:(WKUserContentController *)userContentController didReceiveScriptMessage:(WKScriptMessage *)message {
    if (![message.body isKindOfClass:[NSDictionary class]]) return;
    NSDictionary *body = (NSDictionary *)message.body;
    NSString *action = body[@"action"];
    
    if ([action isEqualToString:@"fetch_items"]) {
        NSString *cat = body[@"category"] ?: @"";
        NSString *sort = body[@"sort"] ?: @"trend";
        NSString *search = body[@"search"] ?: @"";
        int page = [body[@"page"] intValue];
        if (page <= 0) page = 1;
        [self fetchWorkshopItemsCategory:cat sort:sort search:search page:page];
    }
    else if ([action isEqualToString:@"get_installed"]) {
        [self returnInstalledItems];
    }
    else if ([action isEqualToString:@"download_item"]) {
        NSString *itemId = [NSString stringWithFormat:@"%@", body[@"id"] ?: @""];
        NSString *title = body[@"title"] ?: @"";
        [self downloadItemWithId:itemId title:title];
    }
    else if ([action isEqualToString:@"cancel_download"]) {
        NSString *itemId = [NSString stringWithFormat:@"%@", body[@"id"] ?: @""];
        [self cancelDownloadWithId:itemId];
    }
    else if ([action isEqualToString:@"load_model"]) {
        NSString *itemId = [NSString stringWithFormat:@"%@", body[@"id"] ?: @""];
        NSString *path = body[@"path"] ?: @"";
        [self loadModelWithId:itemId path:path];
    }
    else if ([action isEqualToString:@"spawn_avatar"]) {
        NSString *itemId = [NSString stringWithFormat:@"%@", body[@"id"] ?: @""];
        NSString *path = body[@"path"] ?: @"";
        [self spawnNewAvatarWithId:itemId path:path];
    }
    else if ([action isEqualToString:@"show_in_finder"]) {
        NSString *itemId = [NSString stringWithFormat:@"%@", body[@"id"] ?: @""];
        NSString *path = body[@"path"] ?: @"";
        [self showInFinderWithId:itemId path:path];
    }
    else if ([action isEqualToString:@"open_steam"]) {
        NSString *itemId = [NSString stringWithFormat:@"%@", body[@"id"] ?: @""];
        if (itemId.length > 0) {
            NSString *urlStr = [NSString stringWithFormat:@"https://steamcommunity.com/sharedfiles/filedetails/?id=%@", itemId];
            [[NSWorkspace sharedWorkspace] openURL:[NSURL URLWithString:urlStr]];
        }
    }
}

// Query Steam Workshop JSON
- (void)fetchWorkshopItemsCategory:(NSString *)cat sort:(NSString *)sort search:(NSString *)search page:(int)page {
    if (self.currentFetchTask) {
        [self.currentFetchTask cancel];
        self.currentFetchTask = nil;
    }
    
    NSString *urlStr = [NSString stringWithFormat:@"https://steamcommunity.com/workshop/browse/?appid=3625270&browsesort=%@&section=readytouseitems&p=%d", sort, page];
    if (cat.length > 0) {
        urlStr = [urlStr stringByAppendingFormat:@"&requiredtags%%5B0%%5D=%@", [cat stringByAddingPercentEncodingWithAllowedCharacters:[NSCharacterSet URLQueryAllowedCharacterSet]]];
    }
    if (search.length > 0) {
        urlStr = [urlStr stringByAppendingFormat:@"&searchtext=%@", [search stringByAddingPercentEncodingWithAllowedCharacters:[NSCharacterSet URLQueryAllowedCharacterSet]]];
    }
    
    NSMutableURLRequest *req = [NSMutableURLRequest requestWithURL:[NSURL URLWithString:urlStr]];
    [req setValue:@"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)" forHTTPHeaderField:@"User-Agent"];
    req.timeoutInterval = 10.0;
    
    __weak typeof(self) weakSelf = self;
    self.currentFetchTask = [[NSURLSession sharedSession] dataTaskWithRequest:req completionHandler:^(NSData *data, NSURLResponse *res, NSError *err) {
        if (err) {
            if (err.code == NSURLErrorCancelled) {
                // Cancelled due to a newer navigation/filter request. Do not overwrite current items.
                return;
            }
            dispatch_async(dispatch_get_main_queue(), ^{
                __strong typeof(weakSelf) strongSelf = weakSelf;
                if (!strongSelf) return;
                NSDictionary *installedMap = [strongSelf getInstalledMap];
                NSDictionary *fallbackPayload = @{
                    @"page": @(page),
                    @"total_pages": @(1),
                    @"total_count": @(0),
                    @"items": @[],
                    @"installed": installedMap ?: @{}
                };
                NSData *payloadData = [NSJSONSerialization dataWithJSONObject:fallbackPayload options:0 error:nil];
                NSString *base64Str = [payloadData base64EncodedStringWithOptions:0];
                NSString *js = [NSString stringWithFormat:@"window.onWorkshopItemsLoadedBase64('%@');", base64Str];
                [strongSelf.webView evaluateJavaScript:js completionHandler:nil];
            });
            return;
        }
        
        if (!data) return;
        
        NSString *html = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        NSRegularExpression *regex = [NSRegularExpression regularExpressionWithPattern:@"<script [^>]*id=\"valve-ssr-data\"[^>]*>(.*?)</script>" options:NSRegularExpressionDotMatchesLineSeparators error:nil];
        NSTextCheckingResult *match = [regex firstMatchInString:html options:0 range:NSMakeRange(0, html.length)];
        
        NSArray *items = @[];
        int totalPages = 1;
        int totalCount = 0;
        int currentPage = page;
        
        if (match) {
            NSString *jsonStr = [html substringWithRange:[match rangeAtIndex:1]];
            NSDictionary *root = [NSJSONSerialization JSONObjectWithData:[jsonStr dataUsingEncoding:NSUTF8StringEncoding] options:0 error:nil];
            NSString *queryDataStr = root[@"renderContext"][@"queryData"];
            NSDictionary *qd = [NSJSONSerialization JSONObjectWithData:[queryDataStr dataUsingEncoding:NSUTF8StringEncoding] options:0 error:nil];
            NSArray *queries = qd[@"queries"];
            for (NSDictionary *q in queries) {
                id state = q[@"state"];
                if ([state isKindOfClass:[NSDictionary class]]) {
                    id d = state[@"data"];
                    if ([d isKindOfClass:[NSDictionary class]]) {
                        NSArray *results = d[@"results"];
                        if (results && results.count > 0 && results[0][@"publishedfileid"]) {
                            items = results;
                            if (d[@"total_pages"]) totalPages = [d[@"total_pages"] intValue];
                            if (d[@"total_count"]) totalCount = [d[@"total_count"] intValue];
                            if (d[@"current_page"]) currentPage = [d[@"current_page"] intValue];
                            break;
                        }
                    }
                }
            }
        }
        
        dispatch_async(dispatch_get_main_queue(), ^{
            __strong typeof(weakSelf) strongSelf = weakSelf;
            if (!strongSelf) return;
            strongSelf.currentFetchTask = nil;
            NSDictionary *installedMap = [strongSelf getInstalledMap];
            NSDictionary *payload = @{
                @"page": @(currentPage),
                @"total_pages": @(totalPages),
                @"total_count": @(totalCount),
                @"items": items ?: @[],
                @"installed": installedMap ?: @{}
            };
            
            NSData *payloadData = [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil];
            NSString *base64Str = [payloadData base64EncodedStringWithOptions:0];
            NSString *js = [NSString stringWithFormat:@"window.onWorkshopItemsLoadedBase64('%@');", base64Str];
            [strongSelf.webView evaluateJavaScript:js completionHandler:nil];
        });
    }];
    [self.currentFetchTask resume];
}

// Return Installed Items Map
- (NSDictionary *)getInstalledMap {
    NSMutableDictionary *map = [NSMutableDictionary dictionary];
    NSString *base = [MateWorkshopApp baseDataPath];
    NSString *avatarsJson = [base stringByAppendingPathComponent:@"avatars.json"];
    
    if ([[NSFileManager defaultManager] fileExistsAtPath:avatarsJson]) {
        NSData *d = [NSData dataWithContentsOfFile:avatarsJson];
        if (d) {
            NSArray *arr = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
            if ([arr isKindOfClass:[NSArray class]]) {
                for (NSDictionary *entry in arr) {
                    id fileId = entry[@"steamFileId"];
                    if (fileId && [fileId longLongValue] > 0) {
                        map[[NSString stringWithFormat:@"%@", fileId]] = @YES;
                    }
                }
            }
        }
    }
    
    // Check Mods folder for .unity3d files
    NSString *modsDir = [base stringByAppendingPathComponent:@"Mods"];
    NSArray *modFiles = [[NSFileManager defaultManager] contentsOfDirectoryAtPath:modsDir error:nil];
    for (NSString *f in modFiles) {
        if ([f.lowercaseString hasSuffix:@".unity3d"]) {
            // Find numeric ID if present
            NSRegularExpression *re = [NSRegularExpression regularExpressionWithPattern:@"[0-9]{8,12}" options:0 error:nil];
            NSTextCheckingResult *m = [re firstMatchInString:f options:0 range:NSMakeRange(0, f.length)];
            if (m) {
                map[[f substringWithRange:m.range]] = @YES;
            }
        }
    }
    
    return map;
}

- (NSString *)extractThumbnailFromVRM:(NSString *)vrmPath modelName:(NSString *)modelName {
    if (!vrmPath || ![[NSFileManager defaultManager] fileExistsAtPath:vrmPath]) return nil;
    NSFileHandle *fh = [NSFileHandle fileHandleForReadingAtPath:vrmPath];
    if (!fh) return nil;
    
    @try {
        NSData *header = [fh readDataOfLength:12];
        if (header.length < 12) { [fh closeFile]; return nil; }
        const uint32_t *h32 = (const uint32_t *)header.bytes;
        if (h32[0] != 0x46546C67) { [fh closeFile]; return nil; } // "glTF"
        
        NSData *chunk0 = [fh readDataOfLength:8];
        if (chunk0.length < 8) { [fh closeFile]; return nil; }
        const uint32_t *c0 = (const uint32_t *)chunk0.bytes;
        uint32_t jsonLen = c0[0];
        if (c0[1] != 0x4E4F534A) { [fh closeFile]; return nil; } // "JSON"
        
        NSData *jsonData = [fh readDataOfLength:jsonLen];
        if (jsonData.length < jsonLen) { [fh closeFile]; return nil; }
        
        NSData *chunk1 = [fh readDataOfLength:8];
        if (chunk1.length < 8) { [fh closeFile]; return nil; }
        unsigned long long binOffset = [fh offsetInFile];
        
        NSDictionary *json = [NSJSONSerialization JSONObjectWithData:jsonData options:0 error:nil];
        if (![json isKindOfClass:[NSDictionary class]]) { [fh closeFile]; return nil; }
        
        NSNumber *targetImg = nil;
        NSDictionary *ext = json[@"extensions"];
        if (ext[@"VRMC_vrm"][@"meta"][@"thumbnailImage"]) {
            targetImg = ext[@"VRMC_vrm"][@"meta"][@"thumbnailImage"];
        } else if (ext[@"VRM"][@"meta"][@"texture"]) {
            NSInteger texIdx = [ext[@"VRM"][@"meta"][@"texture"] integerValue];
            NSArray *texs = json[@"textures"];
            if (texIdx >= 0 && texIdx < texs.count) {
                targetImg = texs[texIdx][@"source"];
            }
        }
        
        NSArray *images = json[@"images"];
        if (!targetImg && images.count > 0) {
            for (NSInteger i = 0; i < images.count; i++) {
                NSString *inm = images[i][@"name"] ?: @"";
                if ([inm.lowercaseString containsString:@"face"] || [inm.lowercaseString containsString:@"thumb"]) {
                    targetImg = @(i);
                    break;
                }
            }
            if (!targetImg) targetImg = @(0);
        }
        
        if (targetImg && targetImg.integerValue < images.count) {
            NSDictionary *img = images[targetImg.integerValue];
            NSInteger bvIdx = [img[@"bufferView"] integerValue];
            NSArray *bvs = json[@"bufferViews"];
            if (bvIdx >= 0 && bvIdx < bvs.count) {
                NSDictionary *bv = bvs[bvIdx];
                unsigned long long offset = [bv[@"byteOffset"] unsignedLongLongValue];
                NSUInteger length = [bv[@"byteLength"] unsignedIntegerValue];
                
                [fh seekToFileOffset:binOffset + offset];
                NSData *imgData = [fh readDataOfLength:length];
                [fh closeFile];
                
                if (imgData.length > 0) {
                    NSString *thumbsDir = [[MateWorkshopApp baseDataPath] stringByAppendingPathComponent:@"Thumbnails"];
                    [[NSFileManager defaultManager] createDirectoryAtPath:thumbsDir withIntermediateDirectories:YES attributes:nil error:nil];
                    NSString *cleanName = [modelName stringByReplacingOccurrencesOfString:@"/" withString:@"_"];
                    NSString *outPath = [thumbsDir stringByAppendingPathComponent:[NSString stringWithFormat:@"%@_thumb.png", cleanName]];
                    [imgData writeToFile:outPath atomically:YES];
                    return outPath;
                }
            }
        }
    } @catch (NSException *e) {
        NSLog(@"[MateWorkshop] Thumbnail extraction exception: %@", e);
    }
    [fh closeFile];
    return nil;
}

- (void)returnInstalledItems {
    __weak typeof(self) weakSelf = self;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        NSMutableArray *list = [NSMutableArray array];
        NSString *base = [MateWorkshopApp baseDataPath];
        NSString *avatarsJson = [base stringByAppendingPathComponent:@"avatars.json"];
        
        if ([[NSFileManager defaultManager] fileExistsAtPath:avatarsJson]) {
            NSData *d = [NSData dataWithContentsOfFile:avatarsJson];
            if (d) {
                NSArray *arr = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
                if ([arr isKindOfClass:[NSArray class]]) {
                    for (NSDictionary *entry in arr) {
                        NSString *name = entry[@"displayName"] ?: @"Unnamed";
                        id fileId = entry[@"steamFileId"] ?: @"0";
                        NSString *thumb = entry[@"thumbnailPath"] ?: @"";
                        NSString *filePath = entry[@"filePath"] ?: @"";
                        
                        if ((thumb.length == 0 || ![[NSFileManager defaultManager] fileExistsAtPath:thumb]) && filePath.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:filePath]) {
                            __strong typeof(weakSelf) strongSelf = weakSelf;
                            if (strongSelf) {
                                NSString *extracted = [strongSelf extractThumbnailFromVRM:filePath modelName:name];
                                if (extracted.length > 0) {
                                    thumb = extracted;
                                }
                            }
                        }
                        
                        NSString *thumbUri = @"";
                        if (thumb.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:thumb]) {
                            NSData *imgData = [NSData dataWithContentsOfFile:thumb];
                            if (imgData.length > 0) {
                                NSString *b64 = [imgData base64EncodedStringWithOptions:0];
                                NSString *mime = ([thumb.lowercaseString hasSuffix:@".jpg"] || [thumb.lowercaseString hasSuffix:@".jpeg"]) ? @"image/jpeg" : @"image/png";
                                thumbUri = [NSString stringWithFormat:@"data:%@;base64,%@", mime, b64];
                            }
                        }
                        
                        [list addObject:@{
                            @"publishedfileid": [NSString stringWithFormat:@"%@", fileId],
                            @"title": name,
                            @"preview_url": thumbUri,
                            @"filePath": filePath,
                            @"short_description": [NSString stringWithFormat:@"Installed model: %@", filePath],
                            @"tags": @[entry[@"fileType"] ?: @"VRM"],
                            @"total_votes": @0,
                            @"views": @0
                        }];
                    }
                }
            }
        }
        
        NSData *listData = [NSJSONSerialization dataWithJSONObject:list options:0 error:nil];
        NSString *base64Str = [listData base64EncodedStringWithOptions:0];
        
        dispatch_async(dispatch_get_main_queue(), ^{
            __strong typeof(weakSelf) strongSelf = weakSelf;
            if (!strongSelf) return;
            NSString *js = [NSString stringWithFormat:@"window.onInstalledLoadedBase64('%@');", base64Str];
            [strongSelf.webView evaluateJavaScript:js completionHandler:nil];
        });
    });
}

- (void)loadModelWithId:(NSString *)itemId path:(NSString *)path {
    NSString *modelPath = path;
    NSString *modelName = @"";
    NSString *base = [MateWorkshopApp baseDataPath];
    
    if (modelPath.length == 0 || ![[NSFileManager defaultManager] fileExistsAtPath:modelPath]) {
        NSString *avatarsJson = [base stringByAppendingPathComponent:@"avatars.json"];
        if ([[NSFileManager defaultManager] fileExistsAtPath:avatarsJson]) {
            NSData *d = [NSData dataWithContentsOfFile:avatarsJson];
            if (d) {
                NSArray *arr = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
                if ([arr isKindOfClass:[NSArray class]]) {
                    for (NSDictionary *e in arr) {
                        if ([[NSString stringWithFormat:@"%@", e[@"steamFileId"] ?: @""] isEqualToString:itemId] ||
                            [[NSString stringWithFormat:@"%@", e[@"displayName"] ?: @""] isEqualToString:itemId]) {
                            modelPath = e[@"filePath"] ?: @"";
                            modelName = e[@"displayName"] ?: @"";
                            break;
                        }
                    }
                }
            }
        }
    }
    
    if (modelPath.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:modelPath]) {
        if (modelName.length == 0) {
            modelName = [[modelPath lastPathComponent] stringByDeletingPathExtension];
        }
        
        NSString *cmdFile = [base stringByAppendingPathComponent:@"load_command.txt"];
        [modelPath writeToFile:cmdFile atomically:YES encoding:NSUTF8StringEncoding error:nil];
        
        NSString *settingsFile = [base stringByAppendingPathComponent:@"settings.json"];
        if ([[NSFileManager defaultManager] fileExistsAtPath:settingsFile]) {
            NSData *sd = [NSData dataWithContentsOfFile:settingsFile];
            if (sd) {
                NSMutableDictionary *sdict = [[NSJSONSerialization JSONObjectWithData:sd options:NSJSONReadingMutableContainers error:nil] mutableCopy];
                if (sdict) {
                    sdict[@"selectedModelPath"] = modelPath;
                    NSData *newSd = [NSJSONSerialization dataWithJSONObject:sdict options:NSJSONWritingPrettyPrinted error:nil];
                    if (newSd) [newSd writeToFile:settingsFile atomically:YES];
                }
            }
        }
        
        // Check if MateEngineX is running; if not, open it
        NSTask *pgrep = [[NSTask alloc] init];
        pgrep.launchPath = @"/usr/bin/pgrep";
        pgrep.arguments = @[@"-x", @"MateEngineX"];
        [pgrep launch];
        [pgrep waitUntilExit];
        if (pgrep.terminationStatus != 0) {
            [[NSWorkspace sharedWorkspace] openApplicationAtURL:[NSURL fileURLWithPath:@"/Applications/MateEngineX.app"] configuration:[NSWorkspaceOpenConfiguration configuration] completionHandler:nil];
        }
        
        dispatch_async(dispatch_get_main_queue(), ^{
            NSString *js = [NSString stringWithFormat:@"window.onModelLoaded('%@', true, '%@');", itemId, [modelName stringByReplacingOccurrencesOfString:@"'" withString:@"\\'"]];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
    } else {
        dispatch_async(dispatch_get_main_queue(), ^{
            NSString *js = [NSString stringWithFormat:@"window.onModelLoaded('%@', false, 'Model file not found on disk');", itemId];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
    }
}

- (void)spawnNewAvatarWithId:(NSString *)itemId path:(NSString *)path {
    NSString *modelPath = path;
    NSString *base = [MateWorkshopApp baseDataPath];
    
    if (modelPath.length == 0 || ![[NSFileManager defaultManager] fileExistsAtPath:modelPath]) {
        NSString *avatarsJson = [base stringByAppendingPathComponent:@"avatars.json"];
        if ([[NSFileManager defaultManager] fileExistsAtPath:avatarsJson]) {
            NSData *d = [NSData dataWithContentsOfFile:avatarsJson];
            if (d) {
                NSArray *arr = [NSJSONSerialization JSONObjectWithData:d options:0 error:nil];
                if ([arr isKindOfClass:[NSArray class]]) {
                    for (NSDictionary *e in arr) {
                        if ([[NSString stringWithFormat:@"%@", e[@"steamFileId"] ?: @""] isEqualToString:itemId] ||
                            [[NSString stringWithFormat:@"%@", e[@"displayName"] ?: @""] isEqualToString:itemId]) {
                            modelPath = e[@"filePath"] ?: @"";
                            break;
                        }
                    }
                }
            }
        }
    }
    
    if (modelPath.length == 0 || ![[NSFileManager defaultManager] fileExistsAtPath:modelPath]) {
        dispatch_async(dispatch_get_main_queue(), ^{
            NSString *js = [NSString stringWithFormat:@"window.onAvatarSpawned('%@', false, 'Model file not found');", itemId];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
        return;
    }
    
    // Find next available instance index 1..5 with atomic O_CREAT | O_EXCL reservation
    int nextIdx = -1;
    NSString *reservePidFile = nil;
    for (int i = 1; i <= 5; i++) {
        NSString *pidFile = [base stringByAppendingPathComponent:[NSString stringWithFormat:@"instance_%d.pid", i]];
        const char *cPath = [pidFile UTF8String];
        
        int fd = open(cPath, O_WRONLY | O_CREAT | O_EXCL, 0644);
        if (fd >= 0) {
            NSString *curPidStr = [NSString stringWithFormat:@"%d\n", (int)getpid()];
            write(fd, [curPidStr UTF8String], strlen([curPidStr UTF8String]));
            close(fd);
            nextIdx = i;
            reservePidFile = pidFile;
            break;
        } else if (errno == EEXIST) {
            NSString *pidStr = [NSString stringWithContentsOfFile:pidFile encoding:NSUTF8StringEncoding error:nil];
            pid_t pid = (pid_t)[pidStr intValue];
            if (pid > 0 && kill(pid, 0) == 0) {
                continue;
            }
            
            unlink(cPath);
            fd = open(cPath, O_WRONLY | O_CREAT | O_EXCL, 0644);
            if (fd >= 0) {
                NSString *curPidStr = [NSString stringWithFormat:@"%d\n", (int)getpid()];
                write(fd, [curPidStr UTF8String], strlen([curPidStr UTF8String]));
                close(fd);
                nextIdx = i;
                reservePidFile = pidFile;
                break;
            }
        }
    }

    if (nextIdx < 1 || nextIdx > 5 || !reservePidFile) {
        dispatch_async(dispatch_get_main_queue(), ^{
            NSString *js = [NSString stringWithFormat:@"window.onAvatarSpawned('%@', false, 'Maximum of 5 avatars reached. Please close an avatar first.');", itemId];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
        return;
    }
    
    NSString *instanceDir = [base stringByAppendingPathComponent:[NSString stringWithFormat:@"Instance_%d", nextIdx]];
    [[NSFileManager defaultManager] createDirectoryAtPath:instanceDir withIntermediateDirectories:YES attributes:nil error:nil];
    
    NSString *saveFile = [NSString stringWithFormat:@"settings_instance%d.json", nextIdx];
    NSString *settingsPath = [instanceDir stringByAppendingPathComponent:saveFile];
    
    NSMutableDictionary *sdict = [NSMutableDictionary dictionary];
    if ([[NSFileManager defaultManager] fileExistsAtPath:settingsPath]) {
        NSData *sd = [NSData dataWithContentsOfFile:settingsPath];
        if (sd) {
            NSDictionary *existing = [NSJSONSerialization JSONObjectWithData:sd options:0 error:nil];
            if ([existing isKindOfClass:[NSDictionary class]]) {
                sdict = [existing mutableCopy];
            }
        }
    }
    sdict[@"selectedModelPath"] = modelPath;
    NSData *outData = [NSJSONSerialization dataWithJSONObject:sdict options:NSJSONWritingPrettyPrinted error:nil];
    [outData writeToFile:settingsPath atomically:YES];
    
    NSString *rootSettings = [base stringByAppendingPathComponent:saveFile];
    [outData writeToFile:rootSettings atomically:YES];
    
    NSTask *openTask = [[NSTask alloc] init];
    openTask.launchPath = @"/usr/bin/open";
    openTask.arguments = @[
        @"-n",
        @"/Applications/MateEngineX.app",
        @"--args",
        @"--instance", [NSString stringWithFormat:@"%d", nextIdx],
        @"--savefile", saveFile,
        @"--datadir", [NSString stringWithFormat:@"Instance_%d", nextIdx]
    ];
    
    NSError *err = nil;
    [openTask launchAndReturnError:&err];
    if (err) {
        [[NSFileManager defaultManager] removeItemAtPath:reservePidFile error:nil];
    }
    
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!err) {
            NSString *name = [[modelPath lastPathComponent] stringByDeletingPathExtension];
            NSString *js = [NSString stringWithFormat:@"window.onAvatarSpawned('%@', true, '%@');", itemId, [name stringByReplacingOccurrencesOfString:@"'" withString:@"\\'"]];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        } else {
            NSString *js = [NSString stringWithFormat:@"window.onAvatarSpawned('%@', false, '%@');", itemId, err.localizedDescription];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        }
    });
}

- (void)showInFinderWithId:(NSString *)itemId path:(NSString *)path {
    NSString *modelPath = path;
    if (modelPath.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:modelPath]) {
        [[NSWorkspace sharedWorkspace] selectFile:modelPath inFileViewerRootedAtPath:[modelPath stringByDeletingLastPathComponent]];
    }
}

- (void)downloadItemWithId:(NSString *)itemId title:(NSString *)title {
    if (!self.activeDownloads) {
        self.activeDownloads = [NSMutableDictionary dictionary];
    }
    if (self.activeDownloads[itemId]) {
        return; // Already in progress
    }
    
    NSString *scriptPath = nil;
    NSString *bundlePath = [[NSBundle mainBundle] bundlePath];
    NSArray *candidates = @[
        [[NSBundle mainBundle] pathForResource:@"download_workshop" ofType:@"sh"] ?: @"",
        [bundlePath stringByAppendingPathComponent:@"Contents/Resources/download_workshop.sh"],
        [bundlePath stringByAppendingPathComponent:@"Contents/MacOS/download_workshop.sh"],
        [NSHomeDirectory() stringByAppendingPathComponent:@"Documents/antigravity/clever-archimedes/Tools/download_workshop.sh"],
        @"/Applications/MateEngineX.app/Contents/Resources/download_workshop.sh",
        @"/tmp/download_workshop.sh"
    ];
    
    for (NSString *c in candidates) {
        if (c.length > 0 && [[NSFileManager defaultManager] fileExistsAtPath:c]) {
            scriptPath = c;
            break;
        }
    }
    
    if (!scriptPath) {
        NSString *js = [NSString stringWithFormat:@"window.onDownloadCompleted('%@', false, 'download_workshop.sh not found');", itemId];
        [self.webView evaluateJavaScript:js completionHandler:nil];
        return;
    }
    
    NSTask *task = [[NSTask alloc] init];
    task.launchPath = @"/bin/bash";
    task.arguments = @[scriptPath, itemId, [MateWorkshopApp baseDataPath]];
    
    NSMutableDictionary *env = [[[NSProcessInfo processInfo] environment] mutableCopy];
    env[@"PATH"] = @"/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin";
    env[@"MATE_PERSISTENT_DATA_PATH"] = [MateWorkshopApp baseDataPath];
    task.environment = env;
    
    NSPipe *pipe = [NSPipe pipe];
    task.standardOutput = pipe;
    task.standardError = pipe;
    
    self.activeDownloads[itemId] = task;
    
    dispatch_async(dispatch_get_main_queue(), ^{
        NSString *js = [NSString stringWithFormat:@"window.onDownloadProgress('%@', 5.0, 'Connecting to Steam...');", itemId];
        [self.webView evaluateJavaScript:js completionHandler:nil];
    });
    
    pipe.fileHandleForReading.readabilityHandler = ^(NSFileHandle *handle) {
        NSData *chunk = [handle availableData];
        if (chunk.length == 0) return;
        NSString *outStr = [[NSString alloc] initWithData:chunk encoding:NSUTF8StringEncoding];
        if (!outStr) return;
        
        NSArray *lines = [outStr componentsSeparatedByCharactersInSet:[NSCharacterSet newlineCharacterSet]];
        for (NSString *line in lines) {
            if ([line containsString:@"Installing to MateEngineX"]) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    NSString *js = [NSString stringWithFormat:@"window.onDownloadProgress('%@', 95.0, 'Installing...');", itemId];
                    [self.webView evaluateJavaScript:js completionHandler:nil];
                });
            } else if ([line containsString:@"progress:"]) {
                NSRange r = [line rangeOfString:@"progress:"];
                if (r.location != NSNotFound) {
                    NSString *sub = [[line substringFromIndex:r.location + r.length] stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
                    double val = [sub doubleValue];
                    if (val > 0) {
                        float mapped = 10.0f + (float)val * 0.82f;
                        dispatch_async(dispatch_get_main_queue(), ^{
                            NSString *js = [NSString stringWithFormat:@"window.onDownloadProgress('%@', %.1f, 'Downloading %.0f%%...');", itemId, mapped, val];
                            [self.webView evaluateJavaScript:js completionHandler:nil];
                        });
                    }
                }
            }
        }
    };
    
    task.terminationHandler = ^(NSTask *t) {
        pipe.fileHandleForReading.readabilityHandler = nil;
        int code = [t terminationStatus];
        [self.activeDownloads removeObjectForKey:itemId];
        
        dispatch_async(dispatch_get_main_queue(), ^{
            BOOL success = (code == 0);
            if (success) NSBeep();
            NSString *js = [NSString stringWithFormat:@"window.onDownloadCompleted('%@', %s, 'Exit code %d');", itemId, success ? "true" : "false", code];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
    };
    
    NSError *err = nil;
    [task launchAndReturnError:&err];
    if (err) {
        [self.activeDownloads removeObjectForKey:itemId];
        dispatch_async(dispatch_get_main_queue(), ^{
            NSString *js = [NSString stringWithFormat:@"window.onDownloadCompleted('%@', false, '%@');", itemId, err.localizedDescription];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
    }
}

- (void)cancelDownloadWithId:(NSString *)itemId {
    NSTask *task = self.activeDownloads[itemId];
    if (task) {
        pid_t pid = [task processIdentifier];
        [task terminate];
        if (pid > 0) {
            kill(pid, SIGTERM);
            NSTask *killTask = [[NSTask alloc] init];
            killTask.launchPath = @"/usr/bin/pkill";
            killTask.arguments = @[@"-P", [NSString stringWithFormat:@"%d", pid]];
            [killTask launch];
        }
        [self.activeDownloads removeObjectForKey:itemId];
        dispatch_async(dispatch_get_main_queue(), ^{
            NSString *js = [NSString stringWithFormat:@"window.onDownloadCancelled('%@');", itemId];
            [self.webView evaluateJavaScript:js completionHandler:nil];
        });
    }
}

@end

int main(int argc, const char * argv[]) {
    @autoreleasepool {
        for (int i = 1; i < argc; i++) {
            NSString *arg = [NSString stringWithUTF8String:argv[i]];
            if ([arg isEqualToString:@"--datapath"] && i + 1 < argc) {
                gDataPath = [NSString stringWithUTF8String:argv[i + 1]];
                i++;
            }
        }
        NSApplication *app = [NSApplication sharedApplication];
        MateWorkshopApp *delegate = [[MateWorkshopApp alloc] init];
        app.delegate = delegate;
        [app run];
    }
    return 0;
}
