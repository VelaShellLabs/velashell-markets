import { useTheme, type ThemeMode } from '@/theme';
import { DesktopOutlined, MoonOutlined, SunOutlined } from '@ant-design/icons';
import { Button, Dropdown, Tooltip } from 'antd';

const OPTIONS: { key: ThemeMode; label: string; icon: React.ReactNode }[] = [
  { key: 'light', label: '浅色', icon: <SunOutlined /> },
  { key: 'dark', label: '深色', icon: <MoonOutlined /> },
  { key: 'system', label: '跟随系统', icon: <DesktopOutlined /> },
];

/**
 * 主题切换。三态而不是两态:少了"跟随系统"这一档,用户在系统里换了配色之后
 * 还得回来手动再切一次,而这恰恰是绝大多数人想要的默认行为。
 *
 * 按钮上显示的是**当前实际生效**的那个图标(跟随系统时显示日/月),
 * 而不是模式本身 —— 一眼能看出现在是亮是暗,比看到一个显示器图标有用。
 */
export default function ThemeSwitch() {
  const { mode, dark, setMode } = useTheme();
  const current = OPTIONS.find((o) => o.key === mode);

  return (
    <Dropdown
      placement="bottomRight"
      menu={{
        selectedKeys: [mode],
        items: OPTIONS.map((o) => ({ key: o.key, label: o.label, icon: o.icon })),
        onClick: ({ key }) => setMode(key as ThemeMode),
      }}
    >
      <Tooltip title={`外观:${current?.label ?? '跟随系统'}`} placement="bottom">
        <Button type="text" icon={dark ? <MoonOutlined /> : <SunOutlined />} aria-label="切换外观" />
      </Tooltip>
    </Dropdown>
  );
}
