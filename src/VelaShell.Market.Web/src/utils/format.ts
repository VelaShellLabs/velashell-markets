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
