const WS_URL = 'ws://localhost:8765';
let ws = null;
let reconnectAttempts = 0;
const MAX_RECONNECT_ATTEMPTS = 100;
const RECONNECT_DELAY = 3000;
let isConnected = false;

const browserName = getBrowserName();

function getBrowserName() {
    if (typeof chrome !== 'undefined') {
        if (navigator.userAgent.includes('Edg/')) return 'Edge';
        if (navigator.userAgent.includes('Firefox/')) return 'Firefox';
        return 'Chrome';
    }
    return 'Unknown';
}

function connect() {
    if (ws && ws.readyState === WebSocket.OPEN) return;
    
    try {
        ws = new WebSocket(WS_URL);
        
        ws.onopen = () => {
            console.log('[Tai] Connected to Tai server');
            reconnectAttempts = 0;
            isConnected = true;
            sendMessage({ type: 'connection', browser: browserName });
            updateStorage({ connected: true });
        };
        
        ws.onclose = () => {
            console.log('[Tai] Disconnected from Tai server');
            isConnected = false;
            updateStorage({ connected: false });
            scheduleReconnect();
        };
        
        ws.onerror = (error) => {
            console.error('[Tai] WebSocket error:', error);
            isConnected = false;
        };
        
        ws.onmessage = (event) => {
            try {
                const message = JSON.parse(event.data);
                handleMessage(message);
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
        console.log(`[Tai] Reconnecting in ${RECONNECT_DELAY}ms (attempt ${reconnectAttempts})`);
        setTimeout(connect, RECONNECT_DELAY);
    }
}

function sendMessage(data) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        const message = {
            ...data,
            timestamp: new Date().toISOString(),
            browser: browserName
        };
        ws.send(JSON.stringify(message));
        return true;
    }
    return false;
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
    } catch (error) {
        return null;
    }
}

chrome.tabs.onActivated.addListener(async (activeInfo) => {
    const tabInfo = await getTabInfo(activeInfo.tabId);
    if (tabInfo && tabInfo.url && !tabInfo.url.startsWith('chrome://') && !tabInfo.url.startsWith('chrome-extension://')) {
        sendMessage({
            type: 'tabActivate',
            ...tabInfo
        });
    }
});

chrome.tabs.onUpdated.addListener(async (tabId, changeInfo, tab) => {
    if (changeInfo.status === 'complete' && tab.url && !tab.url.startsWith('chrome://') && !tab.url.startsWith('chrome-extension://')) {
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
    sendMessage({
        type: 'tabClose',
        tabId: tabId
    });
});

chrome.webNavigation.onCompleted.addListener((details) => {
    if (details.frameId === 0) {
        sendMessage({
            type: 'navigation',
            tabId: details.tabId,
            url: details.url
        });
    }
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.from === 'content') {
        sendMessage({
            ...message.data,
            tabId: sender.tab?.id,
            url: sender.tab?.url
        });
    }
    
    if (message.type === 'ping') {
        sendResponse({ connected: isConnected });
        return true;
    }
    
    if (message.type === 'reconnect') {
        reconnectAttempts = 0;
        if (ws) {
            ws.close();
        }
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
    
    return true;
});

chrome.storage.local.set({ 
    sessionStart: Date.now(),
    pagesViewed: 0,
    connected: false
});

connect();

chrome.runtime.onInstalled.addListener(() => {
    console.log('[Tai] Extension installed');
    reconnectAttempts = 0;
    connect();
});

chrome.runtime.onStartup.addListener(() => {
    console.log('[Tai] Browser started');
    reconnectAttempts = 0;
    connect();
});
