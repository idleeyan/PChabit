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
        const port = document.getElementById('wsPort')?.value || '8765';
        chrome.tabs.create({ url: `http://localhost:${port}` });
    });

    const portInput = document.getElementById('wsPort');
    const savePortBtn = document.getElementById('savePortBtn');
    if (portInput && savePortBtn) {
        chrome.storage.local.get(['wsPort'], (result) => {
            portInput.value = result.wsPort || '8765';
        });
        savePortBtn.addEventListener('click', async () => {
            const port = parseInt(portInput.value, 10);
            if (Number.isNaN(port) || port < 1 || port > 65535) {
                statusText.textContent = '端口无效';
                return;
            }
            try {
                await chrome.runtime.sendMessage({ type: 'setPort', port });
                statusText.textContent = `端口已切换到 ${port}`;
            } catch (e) {
                statusText.textContent = '端口保存失败';
            }
            setTimeout(checkConnection, 1500);
        });
    }

    // 浏览器名称
    const browserSelect = document.getElementById('browserName');
    const customNameRow = document.getElementById('customNameRow');
    const customNameInput = document.getElementById('customBrowserName');
    const saveBrowserBtn = document.getElementById('saveBrowserBtn');
    const clearBrowserBtn = document.getElementById('clearBrowserBtn');
    const currentBrowserLabel = document.getElementById('currentBrowserLabel');

    async function refreshBrowserNameLabel() {
        try {
            const r = await chrome.runtime.sendMessage({ type: 'getBrowserName' });
            if (currentBrowserLabel && r?.browser) {
                const tag = r.isUserOverride ? '（自定义）' : `（自动${r.autoDetected ? '·' + r.autoDetected : ''}）`;
                currentBrowserLabel.textContent = `当前识别：${r.browser}${tag}`;
            }
            if (browserSelect) {
                const ov = r?.override || '';
                if (!ov) {
                    browserSelect.value = '';
                    if (customNameRow) customNameRow.style.display = 'none';
                } else if (['Chrome', 'Edge', '豆包浏览器', 'Brave', 'Opera', 'Firefox'].includes(ov)) {
                    browserSelect.value = ov;
                    if (customNameRow) customNameRow.style.display = 'none';
                } else {
                    browserSelect.value = '__custom__';
                    if (customNameRow) customNameRow.style.display = 'flex';
                    if (customNameInput) customNameInput.value = ov;
                }
            }
        } catch { /* ignore */ }
    }

    if (browserSelect) {
        browserSelect.addEventListener('change', () => {
            if (customNameRow)
                customNameRow.style.display = browserSelect.value === '__custom__' ? 'flex' : 'none';
        });
    }

    if (saveBrowserBtn) {
        saveBrowserBtn.addEventListener('click', async () => {
            let name = browserSelect?.value || '';
            if (name === '__custom__') {
                name = (customNameInput?.value || '').trim();
                if (!name) {
                    statusText.textContent = '请输入自定义名称';
                    return;
                }
                if (name.length > 20) {
                    statusText.textContent = '名称最多 20 字';
                    return;
                }
            }
            try {
                const r = await chrome.runtime.sendMessage({ type: 'setBrowserName', name });
                if (r && r.ok === false) {
                    statusText.textContent = r.error || '名称无效';
                    return;
                }
                statusText.textContent = r?.isUserOverride
                    ? `已上报桌面：${r.browser}（自定义）`
                    : `浏览器名称：${r?.browser || '自动'}`;
                refreshBrowserNameLabel();
            } catch (e) {
                statusText.textContent = '名称保存失败';
            }
        });
    }

    if (clearBrowserBtn) {
        clearBrowserBtn.addEventListener('click', async () => {
            try {
                const r = await chrome.runtime.sendMessage({ type: 'clearBrowserName' });
                statusText.textContent = `已恢复自动：${r?.browser || '自动'}`;
                if (browserSelect) browserSelect.value = '';
                if (customNameRow) customNameRow.style.display = 'none';
                if (customNameInput) customNameInput.value = '';
                refreshBrowserNameLabel();
            } catch (e) {
                statusText.textContent = '清除失败';
            }
        });
    }

    refreshBrowserNameLabel();
    
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
        // 仅在 sessionStart 就绪后首次更新活跃时间
        updateActiveTime();
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
});
