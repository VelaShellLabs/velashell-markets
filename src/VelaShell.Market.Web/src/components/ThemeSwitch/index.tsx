import { useThemeMode, type ThemeMode } from '@/utils/theme';
import { DesktopOutlined, MoonOutlined, SunOutlined } from '@ant-design/icons';
import { Button, Dropdown, Tooltip } from 'antd';
import type { ReactNode } from 'react';

/** 与参考项目(以及 VelaShell 宿主端)一致的三档:明亮 / 暗黑 / 跟随系统。 */
const OPTIONS: { key: ThemeMode; label: string; icon: ReactNode }[] = [
  { key: 'light', label: '明亮主题', icon: <SunOutlined /> },
  { key: 'dark', label: '暗黑主题', icon: <MoonOutlined /> },
  { key: 'auto', label: '跟随系统', icon: <DesktopOutlined /> },
];

/**
 * 顶栏的外观切换。三态而不是两态:少了"跟随系统"这一档,用户在系统里换了配色之后
 * 还得回来手动再切一次,而这恰恰是绝大多数人想要的默认行为。
 *
 * 按钮上显示的是**当前实际生效**的那个图标(跟随系统时显示日/月),而不是模式本身 ——
 * 一眼能看出现在是亮是暗,比看到一个显示器图标有用。
 */
export default function ThemeSwitch() {
  const { mode, dark, setMode } = useThemeMode();
  const current = OPTIONS.find((option) => option.key === mode);

  return (
    <Dropdown
      placement="bottomRight"
      menu={{
        selectedKeys: [mode],
        items: OPTIONS.map((option) => ({ key: option.key, label: option.label, icon: option.icon })),
        onClick: ({ key }) => setMode(key as ThemeMode),
      }}
    >
      <Tooltip title={`外观:${current?.label ?? '跟随系统'}`} placement="bottom">
        <Button type="text" icon={dark ? <MoonOutlined /> : <SunOutlined />} aria-label="切换外观" />
      </Tooltip>
    </Dropdown>
  );
}
