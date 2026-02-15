(function() {
    'use strict';
    
    const MIN_SCROLL_INTERVAL = 500;
    let lastScrollTime = 0;
    let lastScrollPosition = 0;
    
    function sendMessage(type, data = {}) {
        chrome.runtime.sendMessage({
            from: 'content',
            data: {
                type: type,
                ...data
            }
        });
    }
    
    function getScrollPercentage() {
        const scrollTop = window.scrollY || document.documentElement.scrollTop;
        const scrollHeight = document.documentElement.scrollHeight - window.innerHeight;
        return scrollHeight > 0 ? Math.round((scrollTop / scrollHeight) * 100) : 0;
    }
    
    function handleScroll() {
        const now = Date.now();
        const currentScroll = window.scrollY || document.documentElement.scrollTop;
        
        if (now - lastScrollTime >= MIN_SCROLL_INTERVAL && 
            Math.abs(currentScroll - lastScrollPosition) > 50) {
            lastScrollTime = now;
            lastScrollPosition = currentScroll;
            
            sendMessage('scroll', {
                percentage: getScrollPercentage(),
                direction: currentScroll > lastScrollPosition ? 'down' : 'up'
            });
        }
    }
    
    function handleClick(event) {
        const target = event.target;
        const tagName = target.tagName.toLowerCase();
        
        const elementInfo = {
            tag: tagName,
            id: target.id || null,
            className: target.className || null,
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
            { host: 'github.com', param: 'q' }
        ];
        
        const url = new URL(window.location.href);
        
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
    
    window.addEventListener('scroll', handleScroll, { passive: true });
    document.addEventListener('click', handleClick, true);
    document.addEventListener('keydown', handleKeydown, true);
    document.addEventListener('submit', handleFormSubmit, true);
    
    if (document.readyState === 'complete') {
        detectSearch();
    } else {
        window.addEventListener('load', detectSearch);
    }
    
    console.log('[Tai] Content script loaded');
})();
