/**
 * 浏览器书签导出 / 写回模块。
 * 扩展只负责 export/import；合并与 WebDAV 由 PChabit 桌面端处理。
 * 禁止：全量书签写入 chrome.storage；parentId === "0"；扩展内连 WebDAV。
 */
(function (global) {
    'use strict';

    const SKIP_URL_PREFIXES = [
        'javascript:', 'chrome://', 'chrome-extension://', 'edge://',
        'about:', 'devtools://', 'view-source:', 'resource:', 'edge-untrusted:'
    ];

    const ROOT_ALIASES = [
        ['书签栏', 'bookmarks bar', 'bookmarks', '收藏夹栏', 'favorites bar', 'favorites'],
        ['其他书签', 'other bookmarks', 'other', '其他收藏夹', 'other favorites'],
        ['移动设备书签', 'mobile bookmarks', '移动设备收藏夹', 'mobile favorites']
    ];

    const FALLBACK_FOLDER = 'WebDAV同步';
    const BATCH_SIZE = 200;
    const EXT_VERSION = '2.2.4';

    function shouldSkipUrl(url) {
        if (!url || typeof url !== 'string') return true;
        const lower = url.trim().toLowerCase();
        return SKIP_URL_PREFIXES.some((p) => lower.startsWith(p));
    }

    function normalizeUrl(url) {
        if (!url) return '';
        try {
            const u = new URL(url);
            return u.origin + u.pathname.replace(/\/$/, '') + u.search;
        } catch {
            return String(url).trim();
        }
    }

    function itemKey(item) {
        if (!item) return '';
        if (item.url != null) {
            return 'b:' + normalizeUrl(item.url);
        }
        const path = [...(item.path || []), item.title || ''];
        return 'f:' + path.join('/');
    }

    function flattenWithPaths(nodes, path, out) {
        if (!nodes) return;
        for (const node of nodes) {
            if (!node) continue;
            if (node.url) {
                out.push({
                    type: 'bookmark',
                    id: node.id,
                    title: node.title || node.url,
                    url: node.url,
                    path: path.slice(),
                    dateAdded: node.dateAdded || 0,
                    dateModified: node.dateModified || node.dateAdded || 0
                });
            } else {
                const title = node.title || '';
                out.push({
                    type: 'folder',
                    id: node.id,
                    title,
                    url: null,
                    path: path.slice(),
                    dateAdded: node.dateAdded || 0,
                    dateModified: node.dateGroupModified || node.dateAdded || 0
                });
                if (node.children && node.children.length) {
                    const nextPath = title ? path.concat(title) : path;
                    flattenWithPaths(node.children, nextPath, out);
                }
            }
        }
    }

    function flattenRootChildren(rootChildren) {
        const items = [];
        if (!rootChildren) return items;
        for (const root of rootChildren) {
            if (!root) continue;
            if (root.url) {
                items.push({
                    type: 'bookmark',
                    id: root.id,
                    title: root.title || root.url,
                    url: root.url,
                    path: [],
                    dateAdded: root.dateAdded || 0,
                    dateModified: root.dateModified || root.dateAdded || 0
                });
            } else {
                const title = root.title || '';
                items.push({
                    type: 'folder',
                    id: root.id,
                    title,
                    url: null,
                    path: [],
                    dateAdded: root.dateAdded || 0,
                    dateModified: root.dateGroupModified || root.dateAdded || 0
                });
                if (root.children && root.children.length) {
                    flattenWithPaths(root.children, title ? [title] : [], items);
                }
            }
        }
        return items;
    }

    function toCloudItem(item) {
        return {
            type: item.type,
            title: item.title,
            url: item.url || null,
            path: item.path || [],
            dateAdded: item.dateAdded || 0,
            dateModified: item.dateModified || 0,
            key: itemKey(item)
        };
    }

    async function getBookmarksTree() {
        return await chrome.bookmarks.getTree();
    }

    async function exportBookmarksFlat() {
        const tree = await getBookmarksTree();
        const rootChildren = (tree && tree[0] && tree[0].children) || tree || [];
        const flat = flattenRootChildren(rootChildren);
        return flat.map(toCloudItem);
    }

    async function exportBookmarksInBatches(requestId, sendFn, browserName) {
        const items = await exportBookmarksFlat();
        const total = items.length;
        const batchCount = Math.max(1, Math.ceil(total / BATCH_SIZE));

        for (let i = 0; i < batchCount; i++) {
            const batch = items.slice(i * BATCH_SIZE, (i + 1) * BATCH_SIZE);
            sendFn({
                type: 'bookmarks_export',
                browser: browserName,
                items: batch,
                batchIndex: i,
                batchCount,
                requestId,
                total
            });
        }

        if (total === 0) {
            sendFn({
                type: 'bookmarks_export',
                browser: browserName,
                items: [],
                batchIndex: 0,
                batchCount: 1,
                requestId,
                total: 0
            });
        }

        return { total, batchCount };
    }

    function matchRootSlot(title) {
        if (!title) return -1;
        const lower = title.trim().toLowerCase();
        for (let i = 0; i < ROOT_ALIASES.length; i++) {
            if (ROOT_ALIASES[i].includes(lower)) return i;
        }
        return -1;
    }

    function resolveRootSlots(rootChildren) {
        const slots = [null, null, null];
        const folders = [];

        for (const root of rootChildren || []) {
            if (!root || root.url) continue;
            folders.push(root);

            // 1) 固定 ID
            if (root.id === '1' && !slots[0]) slots[0] = root;
            else if (root.id === '2' && !slots[1]) slots[1] = root;
            else if (root.id === '3' && !slots[2]) slots[2] = root;
        }

        // 2) 标题别名（仅填空）
        for (const folder of folders) {
            const slot = matchRootSlot(folder.title);
            if (slot >= 0 && !slots[slot]) slots[slot] = folder;
        }

        // 3) 顺序兜底（仅填空）
        for (let i = 0; i < folders.length && i < 3; i++) {
            if (!slots[i]) slots[i] = folders[i];
        }

        return slots;
    }

    async function ensureFolder(parentId, title) {
        const children = await chrome.bookmarks.getChildren(parentId);
        const existing = children.find((c) => !c.url && c.title === title);
        if (existing) return existing.id;
        const created = await chrome.bookmarks.create({ parentId, title });
        return created.id;
    }

    async function ensureFallbackFolder(slots, counters) {
        const parents = [slots[1], slots[0], slots[2]].filter(Boolean);
        for (const parent of parents) {
            try {
                const id = await ensureFolder(parent.id, FALLBACK_FOLDER);
                return id;
            } catch {
                // 尝试下一个父目录
            }
        }
        throw new Error('无法创建「' + FALLBACK_FOLDER + '」文件夹');
    }

    function resolveRootParent(rootTitle, slots) {
        const slot = matchRootSlot(rootTitle);
        if (slot >= 0 && slots[slot]) return slots[slot].id;
        if (slots[1]) return slots[1].id;
        if (slots[0]) return slots[0].id;
        return null;
    }

    async function resolveParentByPath(path, slots, folderCache, counters) {
        const segments = (path || []).filter((s) => s != null && s !== '');
        if (!segments.length) {
            return ensureFallbackFolder(slots, counters);
        }

        let parentId = resolveRootParent(segments[0], slots);
        let startIdx = 1;

        if (parentId == null) {
            parentId = await ensureFallbackFolder(slots, counters);
            startIdx = 0;
        }

        for (let i = startIdx; i < segments.length; i++) {
            const cacheKey = parentId + '||' + segments[i];
            if (!folderCache.has(cacheKey)) {
                folderCache.set(cacheKey, await ensureFolder(parentId, segments[i]));
                counters.folderCreated++;
            }
            parentId = folderCache.get(cacheKey);
        }

        return parentId;
    }

    async function applyPlan(request, sendFn, browserName) {
        const counters = {
            added: 0, removed: 0, renamed: 0, folderCreated: 0,
            errors: [], warnings: []
        };

        function pushError(msg) {
            if (counters.errors.length < 8) counters.errors.push(msg);
        }

        function pushWarning(msg) {
            if (counters.warnings.length < 8) counters.warnings.push(msg);
        }

        try {
            if (!chrome.bookmarks || !chrome.bookmarks.create) {
                throw new Error('bookmarks API 不可用');
            }

            const tree = await getBookmarksTree();
            const rootChildren = (tree && tree[0] && tree[0].children) || [];
            const slots = resolveRootSlots(rootChildren);
            const folderCache = new Map();

            // URL→id 索引：一次遍历，避免 200+ 条时逐条 search 导致超时
            const urlIndex = new Map();
            (function indexTree(nodes) {
                if (!nodes) return;
                for (const n of nodes) {
                    if (!n) continue;
                    if (n.url) {
                        try { urlIndex.set(normalizeUrl(n.url), n.id); } catch (e) { /* skip */ }
                    }
                    if (n.children) indexTree(n.children);
                }
            })(rootChildren);

            function findIdFast(url) {
                try { return urlIndex.get(normalizeUrl(url)) || null; }
                catch (e) { return null; }
            }


            // 1) 先建文件夹（按路径深度升序），再建书签
            const toAdd = request.toAdd || [];
            const folders = toAdd
                .filter((i) => i.type === 'folder')
                .sort((a, b) => (a.path || []).length - (b.path || []).length);
            const bookmarks = toAdd.filter((i) => i.type === 'bookmark');

            for (const folder of folders) {
                try {
                    const fullPath = [...(folder.path || []), folder.title];
                    await resolveParentByPath(fullPath, slots, folderCache, counters);
                } catch (e) {
                    pushError('创建文件夹失败 ' + folder.title + ': ' + (e.message || e));
                }
            }

            for (const bm of bookmarks) {
                if (!bm.url || shouldSkipUrl(bm.url)) continue;
                try {
                    const existing = findIdFast(bm.url);
                    if (existing) {
                        // 已存在：同步标题（改名传播），避免广播写回时把改名「跳过」
                        if (bm.title) {
                            try {
                                await chrome.bookmarks.update(existing, { title: bm.title });
                                counters.renamed++;
                            } catch (e) {
                                pushError('更新标题失败 ' + bm.url + ': ' + (e.message || e));
                            }
                        }
                        continue;
                    }
                    const parentId = await resolveParentByPath(bm.path || [], slots, folderCache, counters);
                    await chrome.bookmarks.create({
                        parentId,
                        title: bm.title || bm.url,
                        url: bm.url
                    });
                    counters.added++;
                } catch (e) {
                    pushError('创建书签失败 ' + bm.url + ': ' + (e.message || e));
                }
            }

            // 2) 改名
            for (const r of request.toRename || []) {
                try {
                    const id = findIdFast(r.url);
                    if (id) {
                        await chrome.bookmarks.update(id, { title: r.title });
                        counters.renamed++;
                    }
                } catch (e) {
                    pushError('改名失败 ' + r.url + ': ' + (e.message || e));
                }
            }

            // 3) 删除（仅 bookmark；跳过特殊 URL）
            for (const url of request.toRemoveUrls || []) {
                if (shouldSkipUrl(url)) continue;
                try {
                    const id = findIdFast(url);
                    if (id) {
                        await chrome.bookmarks.remove(id);
                        counters.removed++;
                    }
                } catch (e) {
                    pushError('删除失败 ' + url + ': ' + (e.message || e));
                }
            }
        // 4) 删除空文件夹（非空则跳过——预期保护，记 warning 不记 error）
            for (const folder of request.toRemoveFolders || []) {
                try {
                    const title = folder.title || '';
                    const path = (folder.path || []).filter((s) => s != null && s !== '');
                    const id = await findFolderIdByPathTitle(path, title);
                    if (!id) continue;
                    const children = await chrome.bookmarks.getChildren(id);
                    if (children && children.length > 0) {
                        pushWarning('文件夹非空，跳过删除 ' + title);
                        continue;
                    }
                    await chrome.bookmarks.remove(id);
                    counters.removed++;
                } catch (e) {
                    pushError('删除文件夹失败 ' + (folder.title || '') + ': ' + (e.message || e));
                }
            }
        } catch (e) {
            pushError(e.message || String(e));
        }

        const result = {
            type: 'bookmarks_apply_result',
            requestId: request.requestId,
            browser: browserName,
            ok: counters.errors.length === 0,
            added: counters.added,
            removed: counters.removed,
            renamed: counters.renamed,
            folderCreated: counters.folderCreated,
            errors: counters.errors,
            warnings: counters.warnings
        };

        if (sendFn) sendFn(result);
        return result;
    }

    async function findBookmarkIdByUrl(url) {
        const norm = normalizeUrl(url);
        const results = await chrome.bookmarks.search({ url });
        for (const r of results) {
            if (r.url && normalizeUrl(r.url) === norm) return r.id;
        }
        // search 可能不完全匹配规范化 URL，再扫一遍
        const all = await exportBookmarksFlat();
        const hit = all.find((i) => i.type === 'bookmark' && i.url && normalizeUrl(i.url) === norm);
        return hit ? hit.id : null;
    }

    /** path 为祖先文件夹标题链，title 为文件夹名；返回书签节点 id 或 null。 */
    async function findFolderIdByPathTitle(path, title) {
        if (!title) return null;
        const tree = await getBookmarksTree();
        const rootChildren = (tree && tree[0] && tree[0].children) || [];
        const slots = resolveRootSlots(rootChildren);
        let parentId = resolveRootParent(path[0], slots);
        let startIdx = 1;
        if (parentId == null) {
            // 无祖先：在根槽位下按标题查找
            for (const slot of slots) {
                if (!slot) continue;
                const children = await chrome.bookmarks.getChildren(slot.id);
                const hit = children.find((c) => !c.url && c.title === title);
                if (hit) return hit.id;
            }
            return null;
        }
        for (let i = startIdx; i < path.length; i++) {
            const children = await chrome.bookmarks.getChildren(parentId);
            const hit = children.find((c) => !c.url && c.title === path[i]);
            if (!hit) return null;
            parentId = hit.id;
        }
        const children = await chrome.bookmarks.getChildren(parentId);
        const folder = children.find((c) => !c.url && c.title === title);
        return folder ? folder.id : null;
    }

    /**
     * 简化分批导出：从 startTime 到现在，按天切段，每段 maxResults=10000。
     * 与源项目 history-api 同思路，但保持实现简单可靠。
     */
    async function exportHistorySimple(startTimeMs, maxTotal) {
        const results = [];
        const endTime = Date.now();
        let start = startTimeMs || (endTime - 30 * 24 * 60 * 60 * 1000);
        const segment = 24 * 60 * 60 * 1000;
        const limit = maxTotal || 200000;
        const seen = new Set();

        while (start < endTime && results.length < limit) {
            const segEnd = Math.min(start + segment, endTime);
            try {
                const items = await chrome.history.search({
                    text: '',
                    startTime: start,
                    endTime: segEnd,
                    maxResults: 10000
                });
                for (const h of items) {
                    if (!h.url) continue;
                    const key = `${h.url}|${h.lastVisitTime || start}`;
                    if (seen.has(key)) continue;
                    seen.add(key);
                    results.push({
                        url: h.url,
                        title: h.title || '',
                        // Number() 规范化：部分国产 Chromium（如豆包）可能返回字符串/浮点时间戳
                        visitTime: Number(h.lastVisitTime) || start,
                        visitCount: Number(h.visitCount) || 1
                    });
                }
            } catch (e) {
                console.warn('[Tai] history segment failed', e);
            }
            start = segEnd;
        }

        results.sort((a, b) => b.visitTime - a.visitTime);
        return results;
    }

    async function exportHistoryInBatches(requestId, sendFn, browserName, startTimeMs) {
        const entries = await exportHistorySimple(startTimeMs);
        const total = entries.length;
        const batchCount = Math.max(1, Math.ceil(total / BATCH_SIZE));

        for (let i = 0; i < batchCount; i++) {
            const batch = entries.slice(i * BATCH_SIZE, (i + 1) * BATCH_SIZE);
            sendFn({
                type: 'history_export',
                browser: browserName,
                entries: batch,
                batchIndex: i,
                batchCount,
                requestId,
                total
            });
        }

        if (total === 0) {
            sendFn({
                type: 'history_export',
                browser: browserName,
                entries: [],
                batchIndex: 0,
                batchCount: 1,
                requestId,
                total: 0
            });
        }

        return { total, batchCount };
    }

    global.BrowserSync = {
        EXT_VERSION,
        normalizeUrl,
        itemKey,
        shouldSkipUrl,
        flattenRootChildren,
        exportBookmarksFlat,
        exportBookmarksInBatches,
        exportHistoryInBatches,
        resolveRootSlots,
        applyPlan
    };
})(self);
