import { login, logout } from '@/utils/auth';
import { UserOutlined } from '@ant-design/icons';
import { history, useModel } from '@umijs/max';
import { Avatar, Button, Dropdown, theme } from 'antd';

/**
 * 顶栏右侧的登录入口 / 用户菜单。
 *
 * 单独成组件而不是写在 app.tsx 的 `layout()` 里:那是个普通函数,用不了 hook,
 * 于是头像底色只能写死一个十六进制值 —— 换主色时改不到,暗色下也不跟着走。
 */
export default function UserMenu() {
  const { initialState } = useModel('@@initialState');
  const { token } = theme.useToken();
  const currentUser = initialState?.currentUser;

  if (!currentUser) {
    return (
      <Button type="primary" onClick={() => login()}>
        登录
      </Button>
    );
  }

  return (
    <Dropdown
      menu={{
        items: [
          { key: 'mine', label: '我的上传', onClick: () => history.push('/mine') },
          { key: 'plugins', label: '我的插件', onClick: () => history.push('/owner') },
          { type: 'divider' as const },
          { key: 'logout', label: '退出登录', danger: true, onClick: () => logout() },
        ],
      }}
    >
      <span className="market-user-entry">
        <Avatar size="small" icon={<UserOutlined />} style={{ background: token.colorPrimary }} />
        <span>{currentUser.name}</span>
      </span>
    </Dropdown>
  );
}
