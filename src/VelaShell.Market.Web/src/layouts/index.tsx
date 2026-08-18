import { useEffect, useState } from 'react';
import { ConfigProvider, Layout, Menu, Button, Space, Avatar, Dropdown, theme, App } from 'antd';
import { AppstoreOutlined, CloudUploadOutlined, SafetyOutlined, UserOutlined, GithubOutlined } from '@ant-design/icons';
import zhCN from 'antd/locale/zh_CN';
import { Outlet, history, useLocation } from 'umi';
import { getUser, login, logout, api, type MarketUser } from '../auth';
import './global.css';

/**
 * 全站骨架。整站只有一处 ConfigProvider,主题令牌与中文语言在这里一次定死 ——
 * 分散到各页去设,迟早会出现"这一页是暗的、那一页是亮的"。
 */
export default function BasicLayout() {
  const [me, setMe] = useState<MarketUser | null>(null);
  const [profile, setProfile] = useState<{ name: string; isModerator: boolean } | null>(null);
  const location = useLocation();

  useEffect(() => {
    getUser().then(async (user) => {
      setMe(user);
      if (!user) {
        setProfile(null);
        return;
      }
      // 是不是审核员由服务端说了算,前端只拿这个结论决定要不要露入口。
      const response = await api('/me');
      if (response.ok) setProfile(await response.json());
    });
  }, [location.pathname]);

  const items = [
    { key: '/', icon: <AppstoreOutlined />, label: '浏览' },
    { key: '/upload', icon: <CloudUploadOutlined />, label: '发布插件' },
    ...(profile?.isModerator ? [{ key: '/moderation', icon: <SafetyOutlined />, label: '审核台' }] : []),
  ];

  return (
    <ConfigProvider
      locale={zhCN}
      theme={{
        algorithm: theme.defaultAlgorithm,
        token: {
          colorPrimary: '#4f46e5',
          borderRadius: 8,
          fontSize: 14,
          colorBgLayout: '#f6f7fb',
        },
        components: {
          Layout: { headerBg: '#ffffff', headerHeight: 56 },
          Card: { boxShadowTertiary: '0 1px 2px rgba(16,24,40,.06)' },
        },
      }}
    >
      <App>
        <Layout style={{ minHeight: '100vh' }}>
          <Layout.Header className="market-header">
            <div className="market-header-inner">
              <div className="market-brand" onClick={() => history.push('/')}>
                <span className="market-brand-mark">V</span>
                <span className="market-brand-text">VelaShell 插件市场</span>
              </div>
              <Menu
                mode="horizontal"
                selectedKeys={[location.pathname === '/' ? '/' : `/${location.pathname.split('/')[1]}`]}
                items={items}
                onClick={(e) => history.push(e.key)}
                style={{ flex: 1, minWidth: 0, borderBottom: 'none', background: 'transparent' }}
              />
              <Space>
                <Button
                  type="text"
                  icon={<GithubOutlined />}
                  href="https://github.com/joesdu/VelaShell"
                  target="_blank"
                />
                {me ? (
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
                    <Space style={{ cursor: 'pointer' }}>
                      <Avatar size="small" icon={<UserOutlined />} style={{ background: '#4f46e5' }} />
                      <span>{profile?.name ?? me.profile?.name ?? '已登录'}</span>
                    </Space>
                  </Dropdown>
                ) : (
                  <Button type="primary" onClick={() => login()}>
                    登录
                  </Button>
                )}
              </Space>
            </div>
          </Layout.Header>

          <Layout.Content>
            <Outlet />
          </Layout.Content>

          <Layout.Footer className="market-footer">
            VelaShell 插件市场 · 所有上传的 .vpx 均经隔离检测后才会发布
          </Layout.Footer>
        </Layout>
      </App>
    </ConfigProvider>
  );
}
