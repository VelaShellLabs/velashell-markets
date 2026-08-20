import { useSyncExternalStore } from 'react';

/** 主题模式。`system` 跟随操作系统,是默认值。 */
export type ThemeMode = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'velashell-market.theme';

/**
 * 主题状态。刻意**不放进 initialState / model**:
 *
 * 主题要在 ConfigProvider 上生效,而 ConfigProvider 必须包住整棵树 —— 包括 message /
 * modal 这些从根上挂出去的东西。放进 initialState 的话,读它的地方只能在 initialState
 * Provider 内部,包不住根;于是会出现"页面暗了、弹窗还是亮的"。
 *
 * 一个极小的外部 store + useSyncExternalStore 就绕开了这个层级问题,
 * 顺便让"跟随系统"能直接订阅 matchMedia。
 */
const media: MediaQueryList | null =
  typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null;

const listeners = new Set<() => void>();

function readStored(): ThemeMode {
  if (typeof localStorage === 'undefined') return 'system';
  const raw = localStorage.getItem(STORAGE_KEY);
  return raw === 'light' || raw === 'dark' || raw === 'system' ? raw : 'system';
}

let mode: ThemeMode = readStored();

function emit() {
  listeners.forEach((fn) => fn());
}

// 只在"跟随系统"时才需要理会系统变化,但订阅本身一直挂着 —— 取消再重订的成本
// 比一次无谓的重渲染高,而 emit 在模式不是 system 时不会改变快照。
media?.addEventListener('change', emit);

/** 当前模式(可能是 `system`)。 */
export function getMode(): ThemeMode {
  return mode;
}

/** 切换模式并持久化。 */
export function setMode(next: ThemeMode) {
  mode = next;
  try {
    localStorage.setItem(STORAGE_KEY, next);
  } catch {
    // 隐私模式下 localStorage 可能不可写。切换本身仍然生效,只是刷新后回到默认。
  }
  emit();
}

/** 快照必须是稳定的原始值,所以把"模式 + 系统当前是不是暗"编码成一个字符串。 */
function getSnapshot(): string {
  return `${mode}:${media?.matches ? 'dark' : 'light'}`;
}

function getServerSnapshot(): string {
  return 'system:light';
}

function subscribe(fn: () => void): () => void {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

/** 由快照算出"现在到底该不该是暗的"。 */
export function resolveDark(snapshot: string): boolean {
  const [current, system] = snapshot.split(':');
  return current === 'dark' || (current === 'system' && system === 'dark');
}

/**
 * 当前是不是暗色 —— **不是 hook**,给 app.tsx 的 `layout` 运行期配置用:
 * 那是个普通函数,里面不能调 hook。
 *
 * 它拿到的值是新的:ThemeProvider 在最外层,主题一变整棵树重渲,
 * `layout(...)` 会在这次重渲里被重新调用一遍。
 */
export function isDarkNow(): boolean {
  return resolveDark(getSnapshot());
}

/** 订阅主题。返回当前模式、最终是否为暗色,以及切换函数。 */
export function useTheme(): { mode: ThemeMode; dark: boolean; setMode: (next: ThemeMode) => void } {
  const snapshot = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  return {
    mode: snapshot.split(':')[0] as ThemeMode,
    dark: resolveDark(snapshot),
    setMode,
  };
}

/**
 * 两套主题令牌。整站只在这里定一次 —— 原来放在 config/config.ts 的 antd.configProvider 里,
 * 但那是构建期的静态配置,切不了主题,所以搬到运行期。
 */
export const themeTokens = {
  light: {
    colorPrimary: '#4f46e5',
    borderRadius: 8,
    fontSize: 14,
    colorBgLayout: '#f6f7fb',
  },
  dark: {
    // 暗色下把主色提亮一档:#4f46e5 在深色背景上对比度不够,链接和主按钮会发闷。
    colorPrimary: '#7c74f2',
    borderRadius: 8,
    fontSize: 14,
    colorBgLayout: '#16161a',
  },
} as const;
