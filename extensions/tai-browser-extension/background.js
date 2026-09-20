// Chrome MV3：经典 SW 用 importScripts 加载书签同步模块
// Firefox MV2：manifest scripts 数组已按序加载 browser-sync.js
if (typeof importScripts === 'function' && typeof BrowserSync === 'undefined') {
    try { importScripts('browser-sync.js'); } catch (e) { console.error('[Tai] load browser-sync failed', e); }
}

const DEFAULT_WS_PORT = 8765;
const RECONNECT_DELAY = 3000;
const MAX_RECONNECT_ATTEMPTS = 100;
const MAX_OFFLINE_QUEUE = 200;
const HEARTBEAT_INTERVAL_MS = 15000;

let ws = null;
let reconnectAttempts = 0;
let isConnected = false;
let wsUrl = `ws://localhost:${DEFAULT_WS_PORT}`;
let heartbeatTimer = null;

/** tabId -> { url, title, favIconUrl } */
const tabState = new Map();

const browserName = { current: 'Chrome', override: null };

async function loadBrowserName() {
    try {
        const r = await chrome.storage.local.get(['browserNameOverride']);
        browserName.override = r.browserNameOverride || null;
    } catch { /* ignore */ }
    browserName.current = resolveBrowserName();
    return browserName.current;
}

function resolveBrowserName() {
    if (browserName.override && browserName.override.trim())
        return browserName.override.trim();

    if (typeof chrome === 'undefined') return 'Unknown';
    const ua = navigator.userAgent || '';

    // UA Client Hints brands（更准）
    try {
        const brands = navigator.userAgentData?.brands;
        if (Array.isArray(brands)) {
            const names = brands.map(b => (b.brand || '').toLowerCase()).join(' ');
            if (names.includes('doubao') || names.includes('bytedance')) return '豆包浏览器';
            if (names.includes('microsoft edge') || names.includes('edge')) return 'Edge';
            if (names.includes('brave')) return 'Brave';
            if (names.includes('opera')) return 'Opera';
            if (names.includes('vivaldi')) return 'Vivaldi';
        }
    } catch { /* ignore */ }

    if (ua.includes('Edg/')) return 'Edge';
    if (ua.includes('Firefox/')) return 'Firefox';
    if (/Doubao|DBBrowser|ByteDance|DoubaoBrowser/i.test(ua)) return '豆包浏览器';
    if (ua.includes('Brave/')) return 'Brave';
    if (ua.includes('OPR/') || ua.includes('Opera')) return 'Opera';
    if (ua.includes('Vivaldi/')) return 'Vivaldi';
    if (ua.includes('QQBrowser') || ua.includes('QHB')) return 'QQ浏览器';
    if (ua.includes('QIHU') || ua.includes('360SE')) return '360浏览器';
    if (ua.includes('Quark')) return '夸克';

    // 兜底：不再冒充 Chrome。Chromium 壳浏览器（豆包等）的 UA 常与 Chrome 完全相同，
    // 真实身份由桌面端按进程 exe 路径判定；这里返回中性的 Chromium，避免误报。
    return 'Chromium';
}

function currentBrowserName() {
    return browserName.current || resolveBrowserName();
}

function hasUserOverride() {
    return !!(browserName.override && browserName.override.trim());
}

function autoDetectedName() {
    const saved = browserName.override;
    browserName.override = null;
    const auto = resolveBrowserName();
    browserName.override = saved;
    return auto;
}

function sendMessage(data) {
    const override = hasUserOverride();
    const message = {
        ...data,
        timestamp: new Date().toISOString(),
        browser: currentBrowserName(),
        browserSource: override ? 'override' : 'auto',
        isUserOverride: override,
        autoDetected: autoDetectedName()
    };

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
        return true;
    }

    enqueueOffline(message);
    return false;
}

async function loadConfig() {
    try {
        const result = await chrome.storage.local.get(['wsPort', 'offlineQueue']);
        if (result.wsPort) {
            const port = parseInt(result.wsPort, 10);
            if (!Number.isNaN(port) && port > 0 && port < 65536) {
                wsUrl = `ws://localhost:${port}`;
            }
        }
        return result.offlineQueue || [];
    } catch {
        return [];
    }
}

async function connect() {
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;

    try {
        ws = new WebSocket(wsUrl);

        ws.onopen = async () => {
            console.log('[Tai] Connected to', wsUrl);
            reconnectAttempts = 0;
            isConnected = true;
            sendMessage({ type: 'connection', browser: currentBrowserName(), ua: navigator.userAgent || '' });
            if (typeof BrowserSync !== 'undefined') {
                sendMessage({
                    type: 'browser_sync_ready',
                    browser: currentBrowserName(),
                    ua: navigator.userAgent || '',
                    extVersion: BrowserSync.EXT_VERSION
                });
            }
            updateStorage({ connected: true });
            await flushOfflineQueue();
            startHeartbeat();
        };

        ws.onclose = () => {
            console.log('[Tai] Disconnected from Tai server');
            isConnected = false;
            stopHeartbeat();
            updateStorage({ connected: false });
            scheduleReconnect();
        };

        ws.onerror = (error) => {
            console.error('[Tai] WebSocket error:', error);
            isConnected = false;
        };

        ws.onmessage = (event) => {
            try {
                handleMessage(JSON.parse(event.data));
            } catch (e) {
                console.error('[Tai] Failed to parse message:', e);
            }
        };
    } catch (error) {
        console.error('[Tai] Connection error:', error);
        isConnected = false;
        scheduleReconnect();
    }
}

function updateStorage(data) {
    chrome.storage.local.set(data);
}

function scheduleReconnect() {
    if (reconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
        reconnectAttempts++;
        setTimeout(connect, RECONNECT_DELAY);
    }
}

function isTrackableUrl(url) {
    if (!url) return false;
    const lower = url.toLowerCase();
    return !(lower.startsWith('chrome://') ||
             lower.startsWith('chrome-extension://') ||
             lower.startsWith('edge://') ||
             lower.startsWith('about:') ||
             lower.startsWith('devtools://') ||
             lower.startsWith('view-source:'));
}

async function enqueueOffline(message) {
    if (message.type === 'heartbeat' || message.type === 'scroll' || message.type === 'click') {
        return;
    }
    try {
        const result = await chrome.storage.local.get(['offlineQueue']);
        const queue = result.offlineQueue || [];
        queue.push(message);
        while (queue.length > MAX_OFFLINE_QUEUE) queue.shift();
        await chrome.storage.local.set({ offlineQueue: queue });
    } catch { /* storage may be unavailable */ }
}

async function flushOfflineQueue() {
    try {
        const result = await chrome.storage.local.get(['offlineQueue']);
        const queue = result.offlineQueue || [];
        if (!queue.length) return;
        await chrome.storage.local.set({ offlineQueue: [] });
        for (const msg of queue) {
            if (ws && ws.readyState === WebSocket.OPEN) {
                ws.send(JSON.stringify(msg));
            }
        }
        console.log(`[Tai] Flushed ${queue.length} offline messages`);
    } catch { /* ignore */ }
}

function handleMessage(message) {
    switch (message.type) {
        case 'ping':
            sendMessage({ type: 'pong' });
            break;
        case 'getStatus':
            sendMessage({
                type: 'status',
                activeTab: true,
                tabsCount: true
            });
            break;
        case 'bookmarks_request_export':
            handleBookmarksRequestExport(message);
            break;
        case 'bookmarks_apply':
            handleBookmarksApply(message);
            break;
        case 'history_request_export':
            handleHistoryRequestExport(message);
            break;
    }
}

async function handleHistoryRequestExport(message) {
    if (typeof BrowserSync === 'undefined' || !chrome.history) return;
    try {
        await BrowserSync.exportHistoryInBatches(
            message.requestId,
            sendMessage,
            currentBrowserName(),
            message.startTime
        );
        console.log('[Tai] history_export 完成');
    } catch (e) {
        console.error('[Tai] history_export 失败:', e);
    }
}

async function handleBookmarksRequestExport(message) {
    if (typeof BrowserSync === 'undefined') {
        sendMessage({
            type: 'bookmarks_export',
            browser: currentBrowserName(),
            items: [],
            batchIndex: 0,
            batchCount: 1,
            requestId: message.requestId,
            total: 0,
            error: 'BrowserSync 模块未加载'
        });
        return;
    }
    try {
        const result = await BrowserSync.exportBookmarksInBatches(
            message.requestId,
            sendMessage,
            currentBrowserName()
        );
        console.log(`[Tai] bookmarks_export 完成: ${result.total} 条 / ${result.batchCount} 批`);
    } catch (e) {
        console.error('[Tai] bookmarks_export 失败:', e);
        sendMessage({
            type: 'bookmarks_export',
            browser: currentBrowserName(),
            items: [],
            batchIndex: 0,
            batchCount: 1,
            requestId: message.requestId,
            total: 0,
            error: e.message || String(e)
        });
    }
}

async function handleBookmarksApply(message) {
    if (typeof BrowserSync === 'undefined') {
        sendMessage({
            type: 'bookmarks_apply_result',
            requestId: message.requestId,
            browser: currentBrowserName(),
            ok: false,
            added: 0,
            removed: 0,
            renamed: 0,
            errors: ['BrowserSync 模块未加载']
        });
        return;
    }
    try {
        await BrowserSync.applyPlan(message, sendMessage, currentBrowserName());
    } catch (e) {
        console.error('[Tai] bookmarks_apply 失败:', e);
        sendMessage({
            type: 'bookmarks_apply_result',
            requestId: message.requestId,
            browser: currentBrowserName(),
            ok: false,
            added: 0,
            removed: 0,
            renamed: 0,
            errors: [e.message || String(e)]
        });
    }
}

async function getTabInfo(tabId) {
    try {
        const tab = await chrome.tabs.get(tabId);
        return {
            tabId: tab.id,
            url: tab.url,
            title: tab.title,
            favIconUrl: tab.favIconUrl
        };
    } catch {
        return null;
    }
}

function rememberTab(tabInfo) {
    if (!tabInfo || !tabInfo.tabId || !isTrackableUrl(tabInfo.url)) return;
    tabState.set(tabInfo.tabId, {
        url: tabInfo.url,
        title: tabInfo.title || '',
        favIconUrl: tabInfo.favIconUrl || null
    });
}

function forgetTab(tabId) {
    tabState.delete(tabId);
}

function getTabLastUrl(tabId) {
    return tabState.get(tabId) || null;
}

chrome.tabs.onActivated.addListener(async (activeInfo) => {
    // Deactivate other tabs of this browser for the host
    for (const [id, info] of tabState.entries()) {
        if (id !== activeInfo.tabId && isTrackableUrl(info.url)) {
            sendMessage({
                type: 'pageClose',
                tabId: id,
                url: info.url,
                title: info.title,
                favIconUrl: info.favIconUrl,
                reason: 'tabDeactivated'
            });
        }
    }

    const tabInfo = await getTabInfo(activeInfo.tabId);
    if (tabInfo && isTrackableUrl(tabInfo.url)) {
        rememberTab(tabInfo);
        sendMessage({
            type: 'tabActivate',
            ...tabInfo
        });
    }
});

chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
    if (changeInfo.status === 'complete' && isTrackableUrl(tab.url)) {
        const prev = getTabLastUrl(tabId);
        if (prev && prev.url && prev.url !== tab.url) {
            sendMessage({
                type: 'pageClose',
                tabId,
                url: prev.url,
                title: prev.title,
                favIconUrl: prev.favIconUrl,
                reason: 'navigatedAway'
            });
        }

        rememberTab({ tabId, url: tab.url, title: tab.title, favIconUrl: tab.favIconUrl });
        sendMessage({
            type: 'pageView',
            tabId: tab.id,
            url: tab.url,
            title: tab.title,
            favIconUrl: tab.favIconUrl
        });

        chrome.storage.local.get(['pagesViewed'], (result) => {
            const count = (result.pagesViewed || 0) + 1;
            chrome.storage.local.set({ pagesViewed: count });
        });
    }
});

chrome.tabs.onRemoved.addListener((tabId) => {
    const last = getTabLastUrl(tabId);
    sendMessage({
        type: 'tabClose',
        tabId,
        url: last?.url || null,
        title: last?.title || null,
        favIconUrl: last?.favIconUrl || null
    });
    forgetTab(tabId);
});

chrome.webNavigation.onCompleted.addListener((details) => {
    if (details.frameId === 0 && isTrackableUrl(details.url)) {
        sendMessage({
            type: 'navigation',
            tabId: details.tabId,
            url: details.url
        });
    }
});

function startHeartbeat() {
    stopHeartbeat();
    heartbeatTimer = setInterval(async () => {
        try {
            const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
            if (!tab || !isTrackableUrl(tab.url)) {
                sendMessage({
                    type: 'heartbeat',
                    tabId: tab?.id ?? null,
                    url: null,
                    active: false,
                    visible: false
                });
                return;
            }
            rememberTab(tab);
            sendMessage({
                type: 'heartbeat',
                tabId: tab.id,
                url: tab.url,
                title: tab.title,
                favIconUrl: tab.favIconUrl,
                active: true,
                visible: true
            });
        } catch { /* ignore */ }
    }, HEARTBEAT_INTERVAL_MS);
}

function stopHeartbeat() {
    if (heartbeatTimer) {
        clearInterval(heartbeatTimer);
        heartbeatTimer = null;
    }
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.from === 'content') {
        sendMessage({
            ...message.data,
            tabId: sender.tab?.id,
            url: message.data?.url || sender.tab?.url,
            title: message.data?.title || sender.tab?.title,
            favIconUrl: message.data?.favIconUrl || sender.tab?.favIconUrl
        });
    }

    if (message.type === 'ping') {
        sendResponse({ connected: isConnected });
        return true;
    }

    if (message.type === 'reconnect') {
        reconnectAttempts = 0;
        if (ws) ws.close();
        setTimeout(connect, 100);
        sendResponse({ reconnecting: true });
        return true;
    }

    if (message.type === 'getStatus') {
        sendResponse({
            connected: isConnected,
            wsState: ws ? ws.readyState : -1
        });
        return true;
    }

    if (message.type === 'setPort') {
        const port = parseInt(message.port, 10);
        if (!Number.isNaN(port) && port > 0 && port < 65536) {
            chrome.storage.local.set({ wsPort: port }, () => {
                wsUrl = `ws://localhost:${port}`;
                if (ws) ws.close();
                reconnectAttempts = 0;
                setTimeout(connect, 100);
                sendResponse({ ok: true, port });
            });
            return true;
        }
        sendResponse({ ok: false });
        return true;
    }

    if (message.type === 'setBrowserName') {
        const name = (message.name || '').trim();
        if (name && name.length > 20) {
            sendResponse({ ok: false, error: '名称最多 20 字' });
            return true;
        }
        chrome.storage.local.set({ browserNameOverride: name || null }, () => {
            browserName.override = name || null;
            browserName.current = resolveBrowserName();
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendMessage({
                    type: 'browser_sync_ready',
                    browser: currentBrowserName(),
                    extVersion: typeof BrowserSync !== 'undefined' ? BrowserSync.EXT_VERSION : '2.1.0'
                });
            }
            sendResponse({
                ok: true,
                browser: currentBrowserName(),
                override: browserName.override,
                isUserOverride: hasUserOverride(),
                autoDetected: autoDetectedName()
            });
        });
        return true;
    }

    if (message.type === 'clearBrowserName') {
        chrome.storage.local.set({ browserNameOverride: null }, () => {
            browserName.override = null;
            browserName.current = resolveBrowserName();
            if (ws && ws.readyState === WebSocket.OPEN) {
                sendMessage({
                    type: 'browser_sync_ready',
                    browser: currentBrowserName(),
                    extVersion: typeof BrowserSync !== 'undefined' ? BrowserSync.EXT_VERSION : '2.1.0'
                });
            }
            sendResponse({ ok: true, browser: currentBrowserName(), autoDetected: autoDetectedName() });
        });
        return true;
    }

    if (message.type === 'getBrowserName') {
        sendResponse({
            browser: currentBrowserName(),
            override: browserName.override,
            isUserOverride: hasUserOverride(),
            autoDetected: autoDetectedName()
        });
        return true;
    }

    return true;
});

chrome.storage.local.set({
    sessionStart: Date.now(),
    pagesViewed: 0,
    connected: false
});

(async () => {
    await loadConfig();
    await loadBrowserName();
    connect();
})();

chrome.runtime.onInstalled.addListener(async () => {
    console.log('[Tai] Extension installed');
    reconnectAttempts = 0;
    await loadConfig();
    await loadBrowserName();
    connect();
});

chrome.runtime.onStartup.addListener(async () => {
    console.log('[Tai] Browser started');
    reconnectAttempts = 0;
    await loadConfig();
    await loadBrowserName();
    connect();
});
