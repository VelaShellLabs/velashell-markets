import { themeTokens, useThemeMode } from '@/utils/theme';
import { ConfigProvider, theme as antdTheme } from 'antd';
import { useEffect, type PropsWithChildren } from 'react';

/**
 * 把主题接到两处:
 *
 * 1. **antd 的 ConfigProvider** —— 组件观感。这一层由 `rootContainer` 挂在整棵树最外面,
 *    所以 message / modal / notification 那些从根上挂出去的东西也跟着变;放在页面内部
 *    就会出现"页面暗了、弹窗还是亮的"。参考项目只靠 ProLayout 内部的 ProConfigProvider,
 *    覆盖不到这些浮层,这里补上。
 * 2. **`<html data-theme>`** —— global.less 里那批自定义类(卡片、评价条、Markdown 正文)
 *    的颜色靠它切换。这些类不是 antd 组件,拿不到主题令牌,只能走 CSS 变量。
 *
 * umi 的 antd 插件自己也会渲染一个 ConfigProvider,它在这一层的内部。antd 的
 * ConfigProvider 是**逐层合并**的:内层没写 algorithm 就继承外层的,所以这里设的暗色算法
 * 会一路生效。
 */
export default function ThemeProvider({ children }: PropsWithChildren) {
  const { dark } = useThemeMode();

  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = dark ? 'dark' : 'light';
    // 让浏览器把滚动条、表单控件等原生 UI 也切过去,不然暗色页面配一条亮色滚动条很突兀。
    root.style.colorScheme = dark ? 'dark' : 'light';
  }, [dark]);

  return (
    <ConfigProvider
      theme={{
        algorithm: dark ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm,
        token: dark ? themeTokens.dark : themeTokens.light,
      }}
    >
      {children}
    </ConfigProvider>
  );
}
