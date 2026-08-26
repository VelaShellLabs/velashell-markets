/**
 * 应用主题存储(取自参考项目 deeplogic.datacollector.webui 的 utils/theme)。
 *
 * 用 localStorage 持久化主题选择,并提供订阅机制以便运行时切换(明亮 / 暗黑 / 跟随系统)。
 *
 * 刻意**不放进 initialState / model**:主题最终要落在 antd 的 ConfigProvider 上,
 * 而那个 Provider 必须包住整棵树 —— 包括 message / modal 这些从根上挂出去的东西。
 * 放进 initialState 的话,读它的地方只能在 initialState Provider 内部,包不住根,
 * 于是会出现"页面暗了、弹窗还是亮的"。一个极小的外部 store 就绕开了这个层级问题。
 */
import { storageKeys } from '@/configs';
import { useSyncExternalStore } from 'react';

/** 主题模式 */
export type ThemeMode = 'light' | 'dark' | 'auto';

const STORAGE_KEY = storageKeys.theme;

type Listener = () => void;

const listeners = new Set<Listener>();

const media: MediaQueryList | null = typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia('(prefers-color-scheme: dark)') : null;

function readStored(): ThemeMode {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'light' || saved === 'dark' || saved === 'auto') {
      return saved;
    }
  } catch {
    // localStorage 不可用(隐私模式等)时回退到默认值
  }
  return 'auto';
}

let mode: ThemeMode = readStored();

function emit(): void {
  listeners.forEach((fn) => fn());
}

// 系统配色变化一直订阅着:取消再重订的成本比一次无谓的重渲染高,
// 而非 auto 模式下快照里的"最终是否为暗"不会因为系统变化而改变。
media?.addEventListener('change', emit);

/** 读取已保存的主题,默认跟随系统 */
export function getThemeMode(): ThemeMode {
  return mode;
}

/** 保存主题并通知所有订阅者 */
export function setThemeMode(next: ThemeMode): void {
  mode = next;
  try {
    localStorage.setItem(STORAGE_KEY, next);
  } catch {
    // 忽略持久化失败:切换本身仍然生效,只是刷新后回到默认
  }
  emit();
}

/** 订阅主题变化,返回取消订阅函数 */
export function subscribeThemeMode(listener: Listener): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** 判断给定模式下当前是否应使用暗色(auto 跟随系统) */
export function isDarkMode(value: ThemeMode = mode): boolean {
  if (value === 'dark') return true;
  if (value === 'light') return false;
  return !!media?.matches;
}

/**
 * AntD Pro 布局主题值。
 * 'light' 用默认算法,'realDark' 触发 antd darkAlgorithm(由 ProLayout 内部的 ProConfigProvider 应用)。
 */
export type NavTheme = 'light' | 'realDark';

/** 将应用主题模式映射为 AntD Pro 布局所需的 navTheme 值 */
export function getNavTheme(value: ThemeMode = mode): NavTheme {
  return isDarkMode(value) ? 'realDark' : 'light';
}

/** 快照必须是稳定的原始值,所以把"模式 + 系统当前是不是暗"编码成一个字符串。 */
function getSnapshot(): string {
  return `${mode}:${media?.matches ? 'dark' : 'light'}`;
}

function getServerSnapshot(): string {
  return 'auto:light';
}

/**
 * 订阅主题。返回当前模式、最终是否为暗色,以及切换函数。
 *
 * 比参考项目多这一个 hook:那边每个用到主题的组件都要自己 useState + useEffect + 订阅,
 * useSyncExternalStore 把这套样板收进来一次,顺带解决并发渲染下的撕裂。
 */
export function useThemeMode(): { mode: ThemeMode; dark: boolean; setMode: (next: ThemeMode) => void } {
  const snapshot = useSyncExternalStore(subscribeThemeMode, getSnapshot, getServerSnapshot);
  const current = snapshot.split(':')[0] as ThemeMode;
  return { mode: current, dark: isDarkMode(current), setMode: setThemeMode };
}

/**
 * 两套 antd 主题令牌。整站只在这里定一次。
 *
 * **不能放回 config/config.ts 的 antd.configProvider** —— 那是构建期的静态配置,切不了主题;
 * 而且 umi 的 ConfigProvider 在根主题的**内部**,内层写死的 colorBgLayout 会把外层算出来的
 * 暗色背景覆盖回浅色。
 */
/**
 * 字体栈。Inter 走拉丁字形与数字,中文自然回落到系统字体 ——
 * 不引 web font:一个内网/离线部署的插件市场不该为了字形去连 Google Fonts。
 */
const fontFamily = "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC', 'Microsoft YaHei', sans-serif";

/** 等宽字体。id、版本号、哈希、包内路径一律用它 —— 这些是要被复制和逐字比对的东西。 */
export const monoFontFamily = "'JetBrains Mono', 'Cascadia Code', 'SFMono-Regular', Consolas, 'Liberation Mono', monospace";

/**
 * 两套 antd 主题令牌。整站只在这里定一次,并与 global.less 顶部那组 CSS 变量**逐项对齐** ——
 * 自定义区块(卡片、评价条、Markdown 正文)拿不到 antd 令牌,只能走 CSS 变量,
 * 两边取值不一致的话,antd 组件和自定义区块放在一起就像两张皮。
 *
 * 这里显式给 colorBgContainer / colorBorder 而不是只给 colorBgBase 让算法去推:
 * 推出来的值每次升级 antd 都可能变一点,而这套界面到处是"发丝线 + 白底卡片"的组合,
 * 边框色差一档就能看出来。
 */
export const themeTokens = {
  light: {
    colorPrimary: '#4f46e5',
    colorInfo: '#4f46e5',
    colorSuccess: '#0e9f6e',
    colorWarning: '#c2740a',
    colorError: '#d92d20',
    colorLink: '#4338ca',
    colorText: '#0f1419',
    colorTextSecondary: '#5a6474',
    colorTextTertiary: '#8c96a6',
    colorBgLayout: '#f5f6f8',
    colorBgContainer: '#ffffff',
    colorBgElevated: '#ffffff',
    colorFillQuaternary: '#eff1f4',
    colorBorder: '#e4e7ec',
    colorBorderSecondary: '#eff1f4',
    borderRadius: 9,
    borderRadiusLG: 13,
    borderRadiusSM: 7,
    fontSize: 14,
    fontFamily,
    fontFamilyCode: monoFontFamily,
    controlHeight: 36,
  },
  dark: {
    // 暗色下把主色提亮一档:#4f46e5 在深色背景上对比度不够,链接和主按钮会发闷。
    colorPrimary: '#7c74f2',
    colorInfo: '#7c74f2',
    colorSuccess: '#34d399',
    colorWarning: '#f5b547',
    colorError: '#f97066',
    colorLink: '#b7b1fa',
    colorText: '#eaedf2',
    colorTextSecondary: '#98a2b3',
    colorTextTertiary: '#6a7383',
    colorBgLayout: '#0e1014',
    colorBgContainer: '#16181d',
    colorBgElevated: '#1c1f26',
    colorFillQuaternary: '#1c1f26',
    colorBorder: '#272b33',
    colorBorderSecondary: '#202429',
    borderRadius: 9,
    borderRadiusLG: 13,
    borderRadiusSM: 7,
    fontSize: 14,
    fontFamily,
    fontFamilyCode: monoFontFamily,
    controlHeight: 36,
  },
} as const;
