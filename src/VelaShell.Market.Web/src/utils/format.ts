/** 展示层的格式化。这些函数原先散在四个页面里各写一遍,口径还不完全一致。 */

/** 包大小。1MB 以下按 KB 显示,再小也至少写 1 KB —— "0 KB"看起来像上传失败。 */
export function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '—';
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

/** 日期时间。空值统一显示破折号,免得页面上出现 "Invalid Date"。 */
export function formatDateTime(value?: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
}

/** 只到日的日期。 */
export function formatDate(value?: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString();
}

/** 评分摘要:没人评过就说"暂无评价",不要显示 "0 / 5"(那看起来像被打了满堂差评)。 */
export function formatRating(average: number, count: number): string {
  return count > 0 ? `${average.toFixed(1)} · ${count} 条` : '暂无评价';
}

/**
 * 相对时间:「3 天前」。
 *
 * 列表与卡片上用它而不是绝对日期 —— 读者关心的是"这东西还在维护吗",
 * 而 "2026-08-24" 要在脑子里减一次才回答得了这个问题。超过一个月就回到绝对日期:
 * 「7 个月前」这种说法既不精确,也不比日期本身更好读。
 */
export function formatRelative(value?: string | null): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  const minutes = Math.floor((Date.now() - date.getTime()) / 60000);
  if (minutes < 1) return '刚刚';
  if (minutes < 60) return `${minutes} 分钟前`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} 小时前`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days} 天前`;
  return date.toLocaleDateString();
}

/** 大数字加千位分隔符。下载量到四位数以后不分隔就很难一眼读出量级。 */
export function formatCount(value?: number | null): string {
  return typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString() : '—';
}

/**
 * 紧凑数字:41200 → 41.2k。首屏那三个大数字用它 ——
 * 那里要的是量级,写全反而把三个数字的宽度拉得参差不齐。
 */
export function formatCompact(value?: number | null): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—';
  if (value < 1000) return String(value);
  if (value < 1000000) return `${(value / 1000).toFixed(value < 10000 ? 1 : 0)}k`;
  return `${(value / 1000000).toFixed(1)}M`;
}
