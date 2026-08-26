/**
 * 首屏占位,解决 JS 到位前的白屏(参考项目同名脚本的简化版)。
 *
 * 它**必须读一次主题**:市场是内容型站点,首屏空得最久;选了暗色的人如果先被闪一屏纯白,
 * 那一下比等待本身更难受。这里用的键与 src/utils/theme.ts 是同一个(configs 里的
 * storageKeys.theme),改名要两处一起改 —— 这个脚本跑在打包产物之外,拿不到模块导入。
 */
(function () {
  var root = document.querySelector('#root');
  if (!root || root.innerHTML !== '') return;

  var mode = 'auto';
  try {
    mode = localStorage.getItem('app.theme') || 'auto';
  } catch (e) {
    // 隐私模式下读不到,按跟随系统处理
  }
  var dark = mode === 'dark' || (mode !== 'light' && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);

  // 这四个值必须与 global.less 顶部那组变量对齐(--canvas / --ink-3 / --hairline / --accent),
  // 否则首屏占位与真正渲染出来的页面之间会有一次可见的跳色。
  var bg = dark ? '#0e1014' : '#f5f6f8';
  var fg = dark ? '#6a7383' : '#8c96a6';
  var ring = dark ? '#272b33' : '#e4e7ec';
  var accent = dark ? '#7c74f2' : '#4f46e5';

  // 顺手把 html 的配色也定下来,原生滚动条不会先亮一下再变暗。
  document.documentElement.style.colorScheme = dark ? 'dark' : 'light';

  root.innerHTML =
    '<div style="position:fixed;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:18px;background:' +
    bg +
    ";font-family:-apple-system,BlinkMacSystemFont,'Segoe UI','PingFang SC','Microsoft YaHei',sans-serif\">" +
    '<div style="width:34px;height:34px;border-radius:50%;border:3px solid ' +
    ring +
    ';border-top-color:' +
    accent +
    ';animation:market-spin .8s linear infinite"></div>' +
    '<div style="font-size:13px;color:' +
    fg +
    '">正在载入插件市场…</div>' +
    '<style>@keyframes market-spin{to{transform:rotate(360deg)}}</style>' +
    '</div>';
})();
