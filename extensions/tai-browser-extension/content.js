(function () {
    'use strict';

    const MIN_SCROLL_INTERVAL = 500;
    const IDLE_THRESHOLD_MS = 15000;
    const SCROLL_DELTA = 50;

    let lastScrollTime = 0;
    let lastScrollPosition = 0;
    let lastActivityAt = Date.now();
    let isIdle = false;
    let pageVisible = document.visibilityState === 'visible';

    function sendMessage(type, data = {}) {
        chrome.runtime.sendMessage({
            from: 'content',
            data: {
                type: type,
                url: window.location.href,
                title: document.title,
                ...data
            }
        });
    }

    function markActive() {
        const wasIdle = isIdle;
        lastActivityAt = Date.now();
        if (wasIdle) {
            isIdle = false;
            sendMessage('idle', { idle: false });
        }
    }

    function getScrollPercentage() {
        const scrollTop = window.scrollY || document.documentElement.scrollTop;
        const scrollHeight = document.documentElement.scrollHeight - window.innerHeight;
        return scrollHeight > 0 ? Math.round((scrollTop / scrollHeight) * 100) : 0;
    }

    function handleScroll() {
        markActive();
        const now = Date.now();
        const currentScroll = window.scrollY || document.documentElement.scrollTop;
        const delta = currentScroll - lastScrollPosition;

        if (now - lastScrollTime >= MIN_SCROLL_INTERVAL && Math.abs(delta) > SCROLL_DELTA) {
            lastScrollTime = now;
            const direction = delta > 0 ? 'down' : 'up';
            lastScrollPosition = currentScroll;

            sendMessage('scroll', {
                percentage: getScrollPercentage(),
                direction: direction
            });
        } else {
            lastScrollPosition = currentScroll;
        }
    }

    function handleClick(event) {
        markActive();
        const target = event.target;
        const tagName = target.tagName.toLowerCase();

        const elementInfo = {
            tag: tagName,
            id: target.id || null,
            className: typeof target.className === 'string' ? target.className : null,
            text: target.innerText?.substring(0, 100) || null
        };

        if (tagName === 'a') {
            elementInfo.href = target.href;
            elementInfo.isExternal = target.hostname !== window.location.hostname;
        }

        if (tagName === 'button' || target.type === 'button' || target.type === 'submit') {
            elementInfo.isButton = true;
        }

        sendMessage('click', {
            element: elementInfo,
            coordinates: {
                x: event.clientX,
                y: event.clientY
            }
        });
    }

    function handleKeydown(event) {
        markActive();
        if (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA') {
            const inputInfo = {
                type: event.target.type || 'text',
                name: event.target.name || event.target.id || null,
                isPassword: event.target.type === 'password'
            };

            if (event.key === 'Enter') {
                sendMessage('formSubmit', {
                    element: inputInfo
                });
            }
        }
    }

    function handleFormSubmit(event) {
        markActive();
        const form = event.target;
        const formInfo = {
            action: form.action,
            method: form.method,
            id: form.id || null,
            name: form.name || null,
            fieldCount: form.elements.length
        };

        sendMessage('formSubmit', {
            element: formInfo
        });
    }

    function detectSearch() {
        const searchEngines = [
            { host: 'google.com', param: 'q' },
            { host: 'bing.com', param: 'q' },
            { host: 'duckduckgo.com', param: 'q' },
            { host: 'baidu.com', param: 'wd' },
            { host: 'github.com', param: 'q' },
            { host: 'sogou.com', param: 'query' },
            { host: 'so.com', param: 'q' },
            { host: 'yahoo.com', param: 'p' },
            { host: 'yandex.com', param: 'text' },
            { host: 'bilibili.com', param: 'keyword' },
            { host: 'zhihu.com', param: 'q' },
            { host: 'taobao.com', param: 'q' },
            { host: 'jd.com', param: 'keyword' },
            { host: 'weibo.com', param: 'q' }
        ];

        let url;
        try {
            url = new URL(window.location.href);
        } catch {
            return;
        }

        for (const engine of searchEngines) {
            if (url.hostname.includes(engine.host)) {
                const query = url.searchParams.get(engine.param);
                if (query) {
                    sendMessage('search', {
                        engine: engine.host,
                        query: query
                    });
                    break;
                }
            }
        }
    }

    function handleVisibilityChange() {
        pageVisible = document.visibilityState === 'visible';
        sendMessage('visibility', {
            visible: pageVisible
        });
        if (pageVisible) {
            markActive();
        }
    }

    function handlePageHide() {
        sendMessage('pageClose', {
            reason: 'pageHide'
        });
    }

    // Idle watchdog
    setInterval(() => {
        if (!isIdle && Date.now() - lastActivityAt >= IDLE_THRESHOLD_MS) {
            isIdle = true;
            sendMessage('idle', { idle: true });
        }
    }, 5000);

    // Activity heartbeat from content (with idle state)
    setInterval(() => {
        sendMessage('heartbeat', {
            active: !isIdle && pageVisible,
            visible: pageVisible,
            idle: isIdle
        });
    }, 15000);

    window.addEventListener('scroll', handleScroll, { passive: true });
    document.addEventListener('click', handleClick, true);
    document.addEventListener('keydown', handleKeydown, true);
    document.addEventListener('submit', handleFormSubmit, true);
    document.addEventListener('visibilitychange', handleVisibilityChange);
    window.addEventListener('pagehide', handlePageHide);
    window.addEventListener('beforeunload', handlePageHide);
    ['mousemove', 'mousedown', 'touchstart'].forEach((evt) => {
        window.addEventListener(evt, markActive, { passive: true, capture: true });
    });

    if (document.readyState === 'complete') {
        detectSearch();
    } else {
        window.addEventListener('load', detectSearch);
    }

    console.log('[Tai] Content script loaded');
})();
