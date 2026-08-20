import { getNavTheme, useThemeMode } from '@/utils/theme';
import { useModel } from '@umijs/max';
import { useEffect } from 'react';

/**
 * 主题同步组件(无渲染输出,取自参考项目)。
 *
 * 订阅主题存储,把当前模式映射成 AntD Pro 布局的 navTheme(light / realDark)写进
 * `initialState.settings`。ProLayout 的顶栏与菜单配色由它自己管,不吃外层 ConfigProvider
 * 的暗色算法 —— 不同步的话,深色页面上会留一条白顶栏,或者深顶栏上一排深色菜单文字。
 *
 * 为什么不在 app.tsx 的 `layout()` 里直接算:那是个普通函数,拿不到订阅,
 * 只有在别的原因引起重渲时才会被重新调用一遍,系统配色变化时会漏掉。
 */
export default function ThemeSync() {
  const { setInitialState } = useModel('@@initialState');
  // 订阅主题:模式变化与(auto 下的)系统配色变化都会让这里重渲。
  const { mode } = useThemeMode();
  const navTheme = getNavTheme(mode);

  useEffect(() => {
    setInitialState((state: any) => {
      // 值没变就原样返回:setInitialState 每次都产生新引用,不比一下会把整棵树重渲一遍。
      if (state?.settings?.navTheme === navTheme) return state;
      return { ...state, settings: { ...state?.settings, navTheme } };
    });
  }, [navTheme, setInitialState]);

  return null;
}
