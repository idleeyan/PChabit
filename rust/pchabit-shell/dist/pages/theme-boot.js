/* 配色引导脚本（4.12.35）
 *
 * 在样式表应用之前把 data-theme / data-accent 写到 <html> 上，避免先闪一下默认配色。
 * 放在 <head> 最前面（同步脚本），再由 <link rel="stylesheet" href="theme.css"> 接管。
 *
 * 取值优先级：URL 查询参数 > localStorage > 默认（azure / auto）。
 * azure = 蓝白（浅色），是 4.12.43 起的默认主题。
 * 参数由导航壳（app.html）在切换 iframe 时带上；localStorage 兜底，
 * 保证独立打开的窗口（如自测页）也能记住上次选择。
 */
(function () {
  function pick(qs, key, storeKey, fallback) {
    var v = qs.get(key);
    if (v) return v;
    try {
      var s = localStorage.getItem(storeKey);
      if (s) return s;
    } catch (e) { /* 存储被禁用时静默降级 */ }
    return fallback;
  }
  var qs = new URLSearchParams(location.search);
  // 两个键的兜底值不同：主题默认蓝白，强调色默认跟随方案（auto）
  var theme = pick(qs, "theme", "pch.theme", "azure");
  var accent = pick(qs, "accent", "pch.accent", "auto");
  var el = document.documentElement;
  el.setAttribute("data-theme", theme);
  // auto = 跟随所选方案自带的强调色，不写属性（让 tokens.css 的主题块生效）
  if (accent && accent !== "auto") el.setAttribute("data-accent", accent);
  else el.removeAttribute("data-accent");
  try {
    localStorage.setItem("pch.theme", theme);
    localStorage.setItem("pch.accent", accent);
  } catch (e) { /* 同上 */ }
})();
