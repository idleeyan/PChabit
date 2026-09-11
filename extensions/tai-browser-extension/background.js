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

const browserName = getBrowserName();

function getBrowserName() {
    if (typeof chrome !== 'undefined') {
        if (navigator.userAgent.includes('Edg/')) return 'Edge';
        if (navigator.userAgent.includes('Firefox/')) return 'Firefox';
        return 'Chrome';
    }
    return 'Unknown';
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
            sendMessage({ type: 'connection', browser: browserName });
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

function sendMessage(data) {
    const message = {
        ...data,
        timestamp: new Date().toISOString(),
        browser: browserName
    };

    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
        return true;
    }

    enqueueOffline(message);
    return false;
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

    return true;
});

chrome.storage.local.set({
    sessionStart: Date.now(),
    pagesViewed: 0,
    connected: false
});

(async () => {
    await loadConfig();
    connect();
})();

chrome.runtime.onInstalled.addListener(async () => {
    console.log('[Tai] Extension installed');
    reconnectAttempts = 0;
    await loadConfig();
    connect();
});

chrome.runtime.onStartup.addListener(async () => {
    console.log('[Tai] Browser started');
    reconnectAttempts = 0;
    await loadConfig();
    connect();
});
