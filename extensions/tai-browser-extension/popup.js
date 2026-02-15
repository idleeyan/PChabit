document.addEventListener('DOMContentLoaded', () => {
    const statusDot = document.getElementById('statusDot');
    const statusText = document.getElementById('statusText');
    const pagesViewed = document.getElementById('pagesViewed');
    const activeTime = document.getElementById('activeTime');
    const pageTitle = document.getElementById('pageTitle');
    const pageUrl = document.getElementById('pageUrl');
    const reconnectBtn = document.getElementById('reconnectBtn');
    const settingsBtn = document.getElementById('settingsBtn');
    
    let sessionStart = Date.now();
    
    function updateConnectionStatus(connected) {
        if (connected) {
            statusDot.classList.add('connected');
            statusText.textContent = '已连接到 Tai 服务器';
        } else {
            statusDot.classList.remove('connected');
            statusText.textContent = '正在连接...';
        }
    }
    
    function formatTime(ms) {
        const minutes = Math.floor(ms / 60000);
        if (minutes < 60) {
            return `${minutes}m`;
        }
        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return `${hours}h ${remainingMinutes}m`;
    }
    
    function updateActiveTime() {
        const elapsed = Date.now() - sessionStart;
        activeTime.textContent = formatTime(elapsed);
    }
    
    async function getCurrentTab() {
        try {
            const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
            if (tab) {
                pageTitle.textContent = tab.title || '-';
                pageUrl.textContent = tab.url || '-';
            }
        } catch (error) {
            console.error('Failed to get current tab:', error);
        }
    }
    
    async function checkConnection() {
        try {
            const response = await chrome.runtime.sendMessage({ type: 'getStatus' });
            updateConnectionStatus(response?.connected ?? false);
        } catch (error) {
            updateConnectionStatus(false);
        }
    }
    
    reconnectBtn.addEventListener('click', async () => {
        statusText.textContent = '正在重新连接...';
        try {
            await chrome.runtime.sendMessage({ type: 'reconnect' });
        } catch (error) {
            console.error('Reconnect failed:', error);
        }
        setTimeout(checkConnection, 1500);
    });
    
    settingsBtn.addEventListener('click', () => {
        chrome.tabs.create({ url: 'https://localhost:8765' });
    });
    
    chrome.storage.local.get(['pagesViewed', 'sessionStart', 'connected'], (result) => {
        if (result.pagesViewed !== undefined) {
            pagesViewed.textContent = result.pagesViewed;
        }
        if (result.sessionStart) {
            sessionStart = result.sessionStart;
        } else {
            sessionStart = Date.now();
            chrome.storage.local.set({ sessionStart: sessionStart });
        }
        if (result.connected !== undefined) {
            updateConnectionStatus(result.connected);
        }
    });
    
    chrome.storage.onChanged.addListener((changes) => {
        if (changes.pagesViewed) {
            pagesViewed.textContent = changes.pagesViewed.newValue;
        }
        if (changes.connected) {
            updateConnectionStatus(changes.connected.newValue);
        }
    });
    
    checkConnection();
    getCurrentTab();
    
    setInterval(updateActiveTime, 60000);
    setInterval(checkConnection, 5000);
    setInterval(getCurrentTab, 2000);
    
    updateActiveTime();
});
