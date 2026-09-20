// report.js - 可视化报告页面逻辑（自包含版本）

// ==================== 工具函数 ====================
function extractDomain(url) {
  try {
    const urlObj = new URL(url);
    return urlObj.hostname.replace(/^www\./, '');
  } catch (error) {
    return '未知域名';
  }
}

function getFallbackLogoDataUrl() {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><rect width="32" height="32" rx="6" fill="#e5e7eb"/><path d="M16 8c3.3 0 5.8 1.8 5.8 4.6 0 2.1-1.3 3.5-3 4.3-1.3.6-1.6 1-1.6 2v1.1h-2.4v-1.4c0-1.6.7-2.5 2.4-3.3 1.3-.6 2.1-1.4 2.1-2.6 0-1.5-1.3-2.5-3.3-2.5-1.8 0-3.1.6-4.3 1.8l-1.7-1.7C11 9.2 13.2 8 16 8zm-1.4 14h2.8v2.8h-2.8V22z" fill="#9ca3af"/></svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

function getDomainLogoUrl(domain) {
  if (!domain || domain === '未知域名') {
    return getFallbackLogoDataUrl();
  }
  const domainUrl = domain.startsWith('http') ? domain : `https://${domain}`;
  return `https://www.google.com/s2/favicons?sz=32&domain_url=${encodeURIComponent(domainUrl)}`;
}

function getDateString(timestamp) {
  const date = new Date(timestamp);
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function getHour(timestamp) {
  return new Date(timestamp).getHours();
}

function filterByTimeRange(historyData, days) {
  const cutoffTime = Date.now() - days * 24 * 60 * 60 * 1000;
  return historyData.filter(item => item.lastVisitTime >= cutoffTime);
}

function filterByDateRange(historyData, startDate, endDate) {
  const start = new Date(startDate + 'T00:00:00').getTime();
  const end = new Date(endDate + 'T23:59:59').getTime();
  return historyData.filter(item => item.lastVisitTime >= start && item.lastVisitTime <= end);
}

function generateColors(count) {
  const baseColors = [
    '#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6',
    '#EC4899', '#06B6D4', '#84CC16', '#F97316', '#6366F1',
  ];
  const colors = [];
  for (let i = 0; i < count; i++) {
    colors.push(baseColors[i % baseColors.length]);
  }
  return colors;
}

// HTML转义函数
function escapeHtml(text) {
  if (text == null) return '';
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// ==================== 数据分析函数 ====================
function analyzeHistoryData(historyData) {
  if (!Array.isArray(historyData) || historyData.length === 0) {
    return {
      totalCount: 0,
      uniqueDomains: 0,
      dateRange: { start: null, end: null },
      domainStats: [],
      hourlyDistribution: new Array(24).fill(0),
      dailyTrend: [],
      topWebsites: []
    };
  }

  const totalCount = historyData.length;
  const timestamps = historyData.map(item => item.lastVisitTime);
  const dateRange = {
    start: Math.min(...timestamps),
    end: Math.max(...timestamps)
  };

  const domainMap = new Map();
  historyData.forEach(item => {
    const domain = extractDomain(item.url);
    if (!domainMap.has(domain)) {
      domainMap.set(domain, { domain, count: 0, urls: new Set(), lastVisit: 0 });
    }
    const stats = domainMap.get(domain);
    stats.count++;
    stats.urls.add(item.url);
    if (item.lastVisitTime > stats.lastVisit) stats.lastVisit = item.lastVisitTime;
  });

  const domainStats = Array.from(domainMap.values())
    .map(stats => ({
      domain: stats.domain,
      count: stats.count,
      uniqueUrls: stats.urls.size,
      lastVisit: stats.lastVisit,
      percentage: ((stats.count / totalCount) * 100).toFixed(1)
    }))
    .sort((a, b) => b.count - a.count);

  const hourlyDistribution = new Array(24).fill(0);
  historyData.forEach(item => hourlyDistribution[getHour(item.lastVisitTime)]++);

  const dailyMap = new Map();
  historyData.forEach(item => {
    const date = getDateString(item.lastVisitTime);
    if (!dailyMap.has(date)) dailyMap.set(date, { date, count: 0 });
    dailyMap.get(date).count++;
  });

  const dailyTrend = Array.from(dailyMap.values()).sort((a, b) => a.date.localeCompare(b.date));

  return {
    totalCount,
    uniqueDomains: domainStats.length,
    dateRange,
    domainStats,
    hourlyDistribution,
    dailyTrend,
    topWebsites: domainStats.slice(0, 10)
  };
}

function generateChartData(stats) {
  return {
    domainPie: {
      labels: stats.domainStats.slice(0, 10).map(d => d.domain),
      datasets: [{ data: stats.domainStats.slice(0, 10).map(d => d.count), backgroundColor: generateColors(10) }]
    },
    hourlyBar: {
      labels: Array.from({ length: 24 }, (_, i) => `${i}:00`),
      datasets: [{
        label: '访问次数',
        data: stats.hourlyDistribution,
        backgroundColor: 'rgba(59, 130, 246, 0.6)',
        borderColor: 'rgba(59, 130, 246, 1)',
        borderWidth: 1
      }]
    },
    dailyLine: {
      labels: stats.dailyTrend.map(d => d.date),
      datasets: [{
        label: '每日访问数',
        data: stats.dailyTrend.map(d => d.count),
        borderColor: 'rgba(16, 185, 129, 1)',
        backgroundColor: 'rgba(16, 185, 129, 0.1)',
        tension: 0.4,
        fill: true
      }]
    },
    topSitesBar: {
      labels: stats.topWebsites.map(w => w.domain.length > 15 ? w.domain.substring(0, 15) + '...' : w.domain),
      datasets: [{
        label: '访问次数',
        data: stats.topWebsites.map(w => w.count),
        backgroundColor: generateColors(stats.topWebsites.length)
      }]
    }
  };
}

function getSummary(stats) {
  if (stats.totalCount === 0) {
    return { averagePerDay: 0, mostActiveHour: null, mostVisitedDomain: null };
  }
  
  const days = stats.dailyTrend.length || 1;
  let maxHour = 0, maxHourCount = 0;
  stats.hourlyDistribution.forEach((count, hour) => {
    if (count > maxHourCount) { maxHourCount = count; maxHour = hour; }
  });
  
  return {
    averagePerDay: (stats.totalCount / days).toFixed(1),
    mostActiveHour: maxHour,
    mostVisitedDomain: stats.domainStats[0]?.domain || null
  };
}

// ==================== 页面逻辑 ====================
let allHistoryData = [];
let filteredHistoryData = null;
let currentStats = null;
let charts = {};
let currentSort = { field: 'count', direction: 'desc' }; // 默认按访问次数降序
let modalCharts = {}; // 模态框中的图表实例

document.addEventListener('DOMContentLoaded', async () => {
  await loadHistoryData();
  setupEventListeners();
});

// 数据源：由宿主（PChabit）注入 window.__PCHABIT_HISTORY__
// 元素格式：{ u: url, t: title, l: lastVisitTime, v: visitCount }
async function loadHistoryData() {
  try {
    document.body.classList.add('loading');
    const raw = window.__PCHABIT_HISTORY__;
    
    if (Array.isArray(raw) && raw.length > 0) {
      allHistoryData = raw.map(x => ({
        url: x.u,
        title: x.t || '',
        lastVisitTime: x.l,
        visitCount: x.v || 1
      }));
      updateReport(30);
    } else {
      showEmptyState();
    }
  } catch (error) {
    console.error('加载历史数据失败:', error);
    showError('加载数据失败: ' + error.message);
  } finally {
    document.body.classList.remove('loading');
  }
}

function updateReport(days) {
  if (allHistoryData.length === 0) return;
  
  filteredHistoryData = days === 'all' ? allHistoryData : filterByTimeRange(allHistoryData, parseInt(days));
  
  if (filteredHistoryData.length === 0) {
    showNoDataForRange();
    return;
  }
  
  currentStats = analyzeHistoryData(filteredHistoryData);
  updateSummaryCards(currentStats);
  updateCharts(currentStats);
  updateTable(currentStats);
}

function updateSummaryCards(stats) {
  document.getElementById('totalCount').textContent = stats.totalCount.toLocaleString();
  document.getElementById('uniqueDomains').textContent = stats.uniqueDomains.toLocaleString();
  
  const summary = getSummary(stats);
  document.getElementById('avgPerDay').textContent = summary.averagePerDay;
  document.getElementById('mostActiveHour').textContent = 
    summary.mostActiveHour !== null ? `${summary.mostActiveHour}:00 - ${summary.mostActiveHour + 1}:00` : '-';
}

function updateCharts(stats) {
  const chartData = generateChartData(stats);
  
  // 每日趋势折线图
  createChart('dailyTrendChart', 'line', chartData.dailyLine, false);
  
  // 时段分布柱状图
  createChart('hourlyChart', 'bar', chartData.hourlyBar, false);
  
  // TOP10网站横向柱状图
  createChart('topSitesChart', 'bar', chartData.topSitesBar, true);
  
  // 域名分布饼图
  const ctx = document.getElementById('domainPieChart').getContext('2d');
  if (charts['domainPieChart']) charts['domainPieChart'].destroy();
  charts['domainPieChart'] = new Chart(ctx, {
    type: 'doughnut',
    data: chartData.domainPie,
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'right', labels: { boxWidth: 12, font: { size: 11 } } }
      }
    }
  });
}

function createChart(canvasId, type, data, horizontal) {
  const ctx = document.getElementById(canvasId).getContext('2d');
  if (charts[canvasId]) charts[canvasId].destroy();
  
  charts[canvasId] = new Chart(ctx, {
    type: type,
    data: data,
    options: {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: horizontal ? 'y' : 'x',
      plugins: { legend: { display: false } },
      scales: { y: { beginAtZero: true, ticks: { precision: 0 } } }
    }
  });
}

function updateTable(stats) {
  const tbody = document.getElementById('domainTableBody');
  
  // 根据当前排序设置对数据进行排序
  let sortedData = [...stats.domainStats];
  sortedData.sort((a, b) => {
    let valA, valB;
    if (currentSort.field === 'count') {
      valA = a.count;
      valB = b.count;
    } else if (currentSort.field === 'lastVisit') {
      valA = a.lastVisit;
      valB = b.lastVisit;
    }
    return currentSort.direction === 'asc' ? valA - valB : valB - valA;
  });
  
  // 更新表头排序指示器
  document.querySelectorAll('th.sortable').forEach(th => {
    const sortField = th.dataset.sort;
    let indicator = ' ↕';
    if (sortField === currentSort.field) {
      indicator = currentSort.direction === 'asc' ? ' ↑' : ' ↓';
    }
    // 重置文本，保留字段名
    const baseText = sortField === 'count' ? '访问次数' : '最后访问';
    th.textContent = baseText + indicator;
  });
  
  tbody.innerHTML = sortedData.map((item, index) => `
    <tr class="border-b border-gray-100" data-domain="${escapeHtml(item.domain)}">
      <td class="px-4 py-3 text-gray-600">${index + 1}</td>
      <td class="px-4 py-3 font-medium text-gray-800">
        <div class="domain-cell">
          <img class="domain-logo" src="${getDomainLogoUrl(item.domain)}" alt="" onerror="this.onerror=null;this.src='${getFallbackLogoDataUrl()}'">
          <span class="domain-link" data-domain="${escapeHtml(item.domain)}" title="点击查看详情">${item.domain}</span>
        </div>
      </td>
      <td class="px-4 py-3 text-right text-blue-600 font-medium">${item.count}</td>
      <td class="px-4 py-3 text-right text-gray-600">${item.uniqueUrls}</td>
      <td class="px-4 py-3 text-right">
        <span class="inline-block px-2 py-1 bg-blue-100 text-blue-700 rounded text-xs">${item.percentage}%</span>
      </td>
      <td class="px-4 py-3 text-gray-600">${new Date(item.lastVisit).toLocaleString('zh-CN', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</td>
    </tr>
  `).join('');
}

function setupEventListeners() {
  // 域名点击事件（事件委托）
  const tableBody = document.getElementById('domainTableBody');
  if (tableBody) {
    tableBody.addEventListener('click', (e) => {
      const domainLink = e.target.closest('.domain-link');
      if (domainLink) {
        const domain = domainLink.dataset.domain;
        if (domain) {
          showDomainDetail(domain);
        }
      }
    });
  }
  
  // 点击模态框外部关闭
  const modal = document.getElementById('domainModal');
  if (modal) {
    modal.addEventListener('click', (e) => {
      if (e.target === modal) {
        closeDomainModal();
      }
    });
  }
  
  // 关闭按钮点击事件
  const modalCloseBtn = document.getElementById('modalCloseBtn');
  if (modalCloseBtn) {
    modalCloseBtn.addEventListener('click', (e) => {
      e.stopPropagation(); // 阻止事件冒泡到模态框
      closeDomainModal();
    });
  }
  
  // ESC键关闭模态框
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      closeDomainModal();
    }
  });
  
  // 表格排序功能
  document.querySelectorAll('th.sortable').forEach(th => {
    th.addEventListener('click', () => {
      const sortField = th.dataset.sort;
      if (currentSort.field === sortField) {
        // 切换排序方向
        currentSort.direction = currentSort.direction === 'asc' ? 'desc' : 'asc';
      } else {
        // 切换排序字段，默认降序
        currentSort.field = sortField;
        currentSort.direction = 'desc';
      }
      // 重新渲染表格
      if (currentStats) {
        updateTable(currentStats);
      }
    });
  });
  
  // 时间筛选按钮
  document.querySelectorAll('.filter-btn').forEach(btn => {
    btn.addEventListener('click', (e) => {
      document.querySelectorAll('.filter-btn').forEach(b => {
        b.classList.remove('active');
      });
      e.target.classList.add('active');
      updateReport(e.target.dataset.days);
    });
  });
  
  // 自定义日期
  document.getElementById('customDateBtn').addEventListener('click', () => {
    const start = document.getElementById('startDate').value;
    const end = document.getElementById('endDate').value;
    
    if (start && end) {
      const filtered = filterByDateRange(allHistoryData, start, end);
      if (filtered.length > 0) {
        const stats = analyzeHistoryData(filtered);
        updateSummaryCards(stats);
        updateCharts(stats);
        updateTable(stats);
      } else {
        showNoDataForRange();
      }
    }
  });
  
  // 刷新按钮
  document.getElementById('refreshBtn').addEventListener('click', loadHistoryData);
  
  // 搜索功能
  const searchInput = document.getElementById('searchInput');
  const searchType = document.getElementById('searchType');
  const searchBtn = document.getElementById('searchBtn');
  const clearSearchBtn = document.getElementById('clearSearchBtn');
  
  function performSearch() {
    const keyword = searchInput.value.trim();
    if (!keyword) {
      // 如果搜索框为空，显示全部数据
      updateReport('all');
      return;
    }
    
    const type = searchType.value;
    const searchIn = type === 'all' ? ['title', 'url', 'domain'] : [type];
    const filtered = searchHistory(filteredHistoryData || allHistoryData, keyword, { searchIn });
    
    if (filtered.length > 0) {
      const stats = analyzeHistoryData(filtered);
      updateSummaryCards(stats);
      updateCharts(stats);
      updateTable(stats);
    } else {
      showNoDataForRange();
    }
  }
  
  searchBtn.addEventListener('click', performSearch);
  searchInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') performSearch();
  });
  clearSearchBtn.addEventListener('click', () => {
    searchInput.value = '';
    updateReport('all');
  });
  
  // 导出JSON按钮
  document.getElementById('exportBtn').addEventListener('click', () => {
    if (!currentStats) return;
    
    const exportData = {
      exportTime: new Date().toISOString(),
      summary: { totalCount: currentStats.totalCount, uniqueDomains: currentStats.uniqueDomains, dateRange: currentStats.dateRange },
      domainStats: currentStats.domainStats,
      hourlyDistribution: currentStats.hourlyDistribution.map((count, hour) => ({ hour, count })),
      dailyTrend: currentStats.dailyTrend,
      topWebsites: currentStats.topWebsites
    };
    
    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `history-report-${new Date().toISOString().split('T')[0]}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  });
  
  // 导出CSV按钮
  document.getElementById('exportCSVBtn').addEventListener('click', () => {
    if (!currentStats) return;
    const csv = exportToCSV(filteredHistoryData || allHistoryData);
    const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `history-report-${new Date().toISOString().split('T')[0]}.csv`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  });
  
  // 导出HTML按钮
  document.getElementById('exportHTMLBtn').addEventListener('click', () => {
    if (!currentStats) return;
    const html = exportToHTML(filteredHistoryData || allHistoryData, currentStats);
    const blob = new Blob([html], { type: 'text/html;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `history-report-${new Date().toISOString().split('T')[0]}.html`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  });
}

// 搜索函数
function searchHistory(historyData, keyword, options = {}) {
  const { searchIn = ['title', 'url', 'domain'] } = options;
  const searchTerm = keyword.toLowerCase();
  
  return historyData.filter(item => {
    const title = (item.title || '').toLowerCase();
    const url = item.url.toLowerCase();
    const domain = extractDomain(item.url).toLowerCase();
    
    if (searchIn.includes('title') && title.includes(searchTerm)) return true;
    if (searchIn.includes('url') && url.includes(searchTerm)) return true;
    if (searchIn.includes('domain') && domain.includes(searchTerm)) return true;
    
    return false;
  });
}

// 导出CSV函数
function exportToCSV(historyData) {
  const headers = ['标题', 'URL', '域名', '访问时间', '访问次数'];
  const rows = historyData.map(item => [
    `"${(item.title || '').replace(/"/g, '""')}"`,
    `"${item.url}"`,
    `"${extractDomain(item.url)}"`,
    `"${new Date(item.lastVisitTime).toLocaleString('zh-CN')}"`,
    item.visitCount || 1
  ]);
  
  return [headers.join(','), ...rows.map(row => row.join(','))].join('\n');
}

// 导出HTML函数
function exportToHTML(historyData, stats) {
  const rows = historyData.map(item => `
    <tr>
      <td>${escapeHtml(item.title || '无标题')}</td>
      <td><a href="${escapeHtml(item.url)}" target="_blank">${escapeHtml(item.url)}</a></td>
      <td>${escapeHtml(extractDomain(item.url))}</td>
      <td>${new Date(item.lastVisitTime).toLocaleString('zh-CN')}</td>
      <td>${item.visitCount || 1}</td>
    </tr>
  `).join('');
  
  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <title>浏览历史报告 - ${new Date().toLocaleDateString('zh-CN')}</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 40px; background: #f5f5f5; }
    .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }
    h1 { color: #333; border-bottom: 2px solid #3b82f6; padding-bottom: 10px; }
    .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 20px; margin: 20px 0; }
    .stat-box { background: #f8fafc; padding: 15px; border-radius: 6px; border-left: 4px solid #3b82f6; }
    .stat-label { color: #64748b; font-size: 14px; }
    .stat-value { color: #1e293b; font-size: 24px; font-weight: bold; margin-top: 5px; }
    table { width: 100%; border-collapse: collapse; margin-top: 20px; }
    th { background: #f1f5f9; text-align: left; padding: 12px; font-weight: 600; color: #475569; border-bottom: 2px solid #e2e8f0; }
    td { padding: 12px; border-bottom: 1px solid #e2e8f0; color: #334155; }
    tr:hover { background: #f8fafc; }
    a { color: #3b82f6; text-decoration: none; }
    a:hover { text-decoration: underline; }
    .footer { margin-top: 30px; text-align: center; color: #94a3b8; font-size: 12px; }
  </style>
</head>
<body>
  <div class="container">
    <h1>浏览历史报告</h1>
    <p>生成时间：${new Date().toLocaleString('zh-CN')}</p>
    
    <div class="summary">
      <div class="stat-box">
        <div class="stat-label">总访问次数</div>
        <div class="stat-value">${stats?.totalCount || historyData.length}</div>
      </div>
      <div class="stat-box">
        <div class="stat-label">独立域名</div>
        <div class="stat-value">${stats?.uniqueDomains || 'N/A'}</div>
      </div>
      <div class="stat-box">
        <div class="stat-label">日均访问</div>
        <div class="stat-value">${stats?.averagePerDay || 'N/A'}</div>
      </div>
    </div>
    
    <table>
      <thead>
        <tr>
          <th>标题</th>
          <th>URL</th>
          <th>域名</th>
          <th>访问时间</th>
          <th>访问次数</th>
        </tr>
      </thead>
      <tbody>
        ${rows}
      </tbody>
    </table>
    
    <div class="footer">
      由 WebDAV历史同步扩展生成
    </div>
  </div>
</body>
</html>`;
}

function showEmptyState() {
  document.querySelector('.container').innerHTML = `
    <div style="text-align: center; padding: 60px 20px;">
      <svg style="opacity: 0.3; margin-bottom: 20px; width: 80px; height: 80px; margin-left: auto; margin-right: auto;" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"></path>
      </svg>
      <h3 style="font-size: 20px; color: #4b5563; margin-bottom: 8px;">暂无数据</h3>
      <p style="color: #9ca3af; margin-bottom: 20px;">本机还没有历史数据，请先在「数据管理」页执行历史同步</p>
    </div>
  `;
}

function showNoDataForRange() {
  document.getElementById('totalCount').textContent = '0';
  document.getElementById('uniqueDomains').textContent = '0';
  document.getElementById('avgPerDay').textContent = '0';
  document.getElementById('mostActiveHour').textContent = '-';
  
  Object.values(charts).forEach(chart => chart.destroy());
  charts = {};
  
  document.getElementById('domainTableBody').innerHTML = `
    <tr><td colspan="6" style="text-align: center; padding: 32px 16px; color: #9ca3af;">该时间范围内没有数据</td></tr>
  `;
}

function showChartError() {
  const chartContainers = ['dailyTrendChart', 'hourlyChart', 'topSitesChart', 'domainPieChart'];
  chartContainers.forEach(id => {
    const container = document.getElementById(id);
    if (container) {
      container.style.display = 'none';
      const parent = container.parentElement;
      const errorMsg = document.createElement('div');
      errorMsg.style.cssText = 'text-align: center; padding: 40px; color: #9ca3af; font-size: 14px;';
      errorMsg.innerHTML = `
        <svg width="48" height="48" style="margin-bottom: 12px; opacity: 0.5;" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"></path>
        </svg>
        <p>图表加载失败</p>
        <p style="font-size: 12px; margin-top: 4px;">Chart.js 库未能加载，请检查网络连接后刷新页面</p>
      `;
      parent.appendChild(errorMsg);
    }
  });
}

function showError(message) {
  const errorDiv = document.createElement('div');
  errorDiv.style.cssText = 'background: #fef2f2; border: 1px solid #fecaca; color: #b91c1c; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;';
  errorDiv.textContent = message;
  document.querySelector('.container').insertBefore(errorDiv, document.querySelector('.container').firstChild);
}

// ==================== 域名详情模态框功能 ====================

function showDomainDetail(domain) {
  // 获取该域名的所有访问记录
  const domainData = (filteredHistoryData || allHistoryData).filter(item => {
    const itemDomain = extractDomain(item.url);
    return itemDomain === domain;
  });
  
  if (domainData.length === 0) return;
  
  // 分析域名数据
  const domainStats = analyzeDomainData(domain, domainData);
  
  // 填充模态框内容
  document.getElementById('modalDomainName').textContent = domain;
  document.getElementById('modalDomainLogo').src = getDomainLogoUrl(domain);
  document.getElementById('modalTotalVisits').textContent = domainStats.totalVisits;
  document.getElementById('modalUniqueUrls').textContent = domainStats.uniqueUrls;
  document.getElementById('modalFirstVisit').textContent = new Date(domainStats.firstVisit).toLocaleDateString('zh-CN', { month: 'short', day: 'numeric' });
  document.getElementById('modalLastVisit').textContent = new Date(domainStats.lastVisit).toLocaleDateString('zh-CN', { month: 'short', day: 'numeric' });
  
  // 渲染访问趋势图
  renderModalTrendChart(domainStats.dailyTrend);
  
  // 渲染时段分布图
  renderModalHourlyChart(domainStats.hourlyDistribution);
  
  // 渲染URL列表
  renderModalUrlList(domainStats.urlStats);
  
  // 显示模态框
  document.getElementById('domainModal').classList.add('active');
}

function closeDomainModal() {
  document.getElementById('domainModal').classList.remove('active');
  // 销毁模态框中的图表
  Object.values(modalCharts).forEach(chart => chart.destroy());
  modalCharts = {};
}

function analyzeDomainData(domain, domainData) {
  // 按URL分组统计
  const urlMap = new Map();
  const dailyMap = new Map();
  const hourlyDistribution = new Array(24).fill(0);
  
  let firstVisit = Infinity;
  let lastVisit = 0;
  
  domainData.forEach(item => {
    // URL统计
    if (!urlMap.has(item.url)) {
      urlMap.set(item.url, { 
        url: item.url, 
        title: item.title || '无标题', 
        count: 0, 
        lastVisit: 0 
      });
    }
    const urlStat = urlMap.get(item.url);
    urlStat.count += (item.visitCount || 1);
    if (item.lastVisitTime > urlStat.lastVisit) {
      urlStat.lastVisit = item.lastVisitTime;
    }
    
    // 每日统计
    const date = getDateString(item.lastVisitTime);
    if (!dailyMap.has(date)) {
      dailyMap.set(date, { date, count: 0 });
    }
    dailyMap.get(date).count += (item.visitCount || 1);
    
    // 时段统计
    hourlyDistribution[getHour(item.lastVisitTime)] += (item.visitCount || 1);
    
    // 首次/最后访问时间
    if (item.lastVisitTime < firstVisit) firstVisit = item.lastVisitTime;
    if (item.lastVisitTime > lastVisit) lastVisit = item.lastVisitTime;
  });
  
  // URL排序（按访问次数）
  const urlStats = Array.from(urlMap.values())
    .sort((a, b) => b.count - a.count)
    .slice(0, 20); // 只取前20个
  
  // 每日趋势排序
  const dailyTrend = Array.from(dailyMap.values())
    .sort((a, b) => a.date.localeCompare(b.date));
  
  return {
    domain,
    totalVisits: domainData.reduce((sum, item) => sum + (item.visitCount || 1), 0),
    uniqueUrls: urlMap.size,
    firstVisit,
    lastVisit,
    urlStats,
    dailyTrend,
    hourlyDistribution
  };
}

function renderModalTrendChart(dailyTrend) {
  const ctx = document.getElementById('modalTrendChart').getContext('2d');
  if (modalCharts['modalTrendChart']) modalCharts['modalTrendChart'].destroy();
  
  modalCharts['modalTrendChart'] = new Chart(ctx, {
    type: 'line',
    data: {
      labels: dailyTrend.map(d => d.date.slice(5)), // 只显示月-日
      datasets: [{
        label: '访问次数',
        data: dailyTrend.map(d => d.count),
        borderColor: 'rgba(59, 130, 246, 1)',
        backgroundColor: 'rgba(59, 130, 246, 0.1)',
        tension: 0.4,
        fill: true,
        pointRadius: 3,
        pointHoverRadius: 5
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        y: { beginAtZero: true, ticks: { precision: 0 } },
        x: { grid: { display: false } }
      }
    }
  });
}

function renderModalHourlyChart(hourlyDistribution) {
  const ctx = document.getElementById('modalHourlyChart').getContext('2d');
  if (modalCharts['modalHourlyChart']) modalCharts['modalHourlyChart'].destroy();
  
  modalCharts['modalHourlyChart'] = new Chart(ctx, {
    type: 'bar',
    data: {
      labels: Array.from({ length: 24 }, (_, i) => `${i}:00`),
      datasets: [{
        label: '访问次数',
        data: hourlyDistribution,
        backgroundColor: 'rgba(16, 185, 129, 0.6)',
        borderColor: 'rgba(16, 185, 129, 1)',
        borderWidth: 1
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        y: { beginAtZero: true, ticks: { precision: 0 } },
        x: { grid: { display: false }, ticks: { maxTicksLimit: 12 } }
      }
    }
  });
}

function renderModalUrlList(urlStats) {
  const container = document.getElementById('modalUrlList');
  container.innerHTML = urlStats.map(item => `
    <div class="url-item">
      <span class="url-item-count">${item.count}</span>
      <div class="url-item-info">
        <div class="url-item-title" title="${escapeHtml(item.title)}">${escapeHtml(item.title)}</div>
        <a href="${item.url}" target="_blank" class="url-item-link" title="${item.url}">${item.url}</a>
      </div>
      <span class="url-item-time">${new Date(item.lastVisit).toLocaleDateString('zh-CN', { month: 'short', day: 'numeric' })}</span>
    </div>
  `).join('');
}


