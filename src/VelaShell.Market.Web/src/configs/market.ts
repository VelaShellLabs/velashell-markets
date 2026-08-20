/** 市场域的展示常量:分页大小、排序项、各种状态到标签的映射。 */

/** 浏览页一页 24 个:内容区铺满窗口后一行能排 4–6 张卡片,12 个只够两三行,翻页太频繁。 */
export const PLUGIN_PAGE_SIZE = 24;

/** 评价列表一页 10 条。 */
export const REVIEW_PAGE_SIZE = 10;

/** 审核台表格默认一页 20 行。 */
export const MODERATION_PAGE_SIZE = 20;

/** 浏览页的排序项。value 直接透传给后端。 */
export const SORT_OPTIONS = [
  { label: '最近更新', value: 'updated' },
  { label: '下载最多', value: 'downloads' },
  { label: '评分最高', value: 'rating' },
  { label: '最新发布', value: 'created' },
] as const;

export type TagPreset = { color?: string; text: string };

/** 上传记录的状态。 */
export const STATUS_TAG: Record<string, TagPreset> = {
  Quarantined: { color: 'processing', text: '隔离中' },
  Scanning: { color: 'processing', text: '检测中' },
  Rejected: { color: 'error', text: '已拒收' },
  Published: { color: 'success', text: '已发布' },
  Withdrawn: { color: 'default', text: '已撤回' },
};

/** 检测结论。 */
export const VERDICT_TAG: Record<string, TagPreset> = {
  Pending: { color: 'default', text: '排队中' },
  Passed: { color: 'success', text: '通过' },
  NeedsReview: { color: 'warning', text: '待人工复核' },
  Failed: { color: 'error', text: '未通过' },
  Errored: { color: 'error', text: '检测出错' },
};

/** 检测发现项的严重级别。 */
export const SEVERITY_TAG: Record<MarketAPI.Severity, TagPreset> = {
  Blocking: { color: 'error', text: '阻断' },
  Warning: { color: 'warning', text: '待复核' },
  Info: { color: 'default', text: '提示' },
};

/** 签名状态。tip 解释这个状态到底意味着什么 —— 光看"自签名"三个字没人知道差别。 */
export const SIGNATURE_TAG: Record<string, TagPreset & { tip?: string }> = {
  Trusted: { color: 'success', text: '已签名', tip: '包带有效签名,升级时可校验发布者身份连续性' },
  Untrusted: { color: 'warning', text: '自签名', tip: '签名有效,但公钥不在受信任列表里' },
  None: { text: '未签名', tip: '作者未对该包签名。市场当前允许未签名的包上架' },
};

/**
 * 插件图标的渐变色。市场不存图标资源,用插件 id 稳定映射到其中一个 ——
 * 同一插件每次都长一个样,比整墙灰色首字母更容易在列表里认出来。
 */
export const PLUGIN_GRADIENTS = [
  'linear-gradient(135deg, #4f46e5, #7c3aed)',
  'linear-gradient(135deg, #0ea5e9, #2563eb)',
  'linear-gradient(135deg, #059669, #0d9488)',
  'linear-gradient(135deg, #ea580c, #dc2626)',
  'linear-gradient(135deg, #d946ef, #9333ea)',
  'linear-gradient(135deg, #f59e0b, #ea580c)',
  'linear-gradient(135deg, #06b6d4, #0891b2)',
  'linear-gradient(135deg, #e11d48, #be185d)',
];

/** 审核台"上架/下架"筛选。中文既当值又当显示文本,Segmented 直接吃。 */
export const PLUGIN_SCOPES = ['全部', '已上架', '已下架'] as const;
export type PluginScope = (typeof PLUGIN_SCOPES)[number];
export const PLUGIN_SCOPE_FLAG: Record<PluginScope, boolean | undefined> = {
  全部: undefined,
  已上架: false,
  已下架: true,
};

/** 审核台"显示/隐藏"筛选。 */
export const REVIEW_SCOPES = ['全部', '显示中', '已隐藏'] as const;
export type ReviewScope = (typeof REVIEW_SCOPES)[number];
export const REVIEW_SCOPE_FLAG: Record<ReviewScope, boolean | undefined> = {
  全部: undefined,
  显示中: false,
  已隐藏: true,
};
