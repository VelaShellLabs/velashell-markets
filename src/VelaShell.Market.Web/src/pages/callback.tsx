import { useEffect, useState } from 'react';
import { Button, Result, Spin } from 'antd';
import { history } from 'umi';
import { completeLogin } from '../auth';

/**
 * OIDC 登录回调:用授权码换取令牌后回首页。
 *
 * 失败要**显式展示**而不是默默跳回首页 —— 回调失败最常见的原因是
 * redirect_uri 不在认证服务的白名单里,那种情况下静默跳转会让人以为"登录了但没生效",
 * 排查方向完全错。
 */
export default function CallbackPage() {
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    completeLogin()
      .then(() => history.replace('/'))
      .catch((e: Error) => setError(e.message));
  }, []);

  if (error) {
    return (
      <Result
        status="error"
        title="登录未能完成"
        subTitle={error}
        extra={[
          <Button type="primary" key="home" onClick={() => history.replace('/')}>回到首页</Button>,
        ]}
      >
        <p style={{ color: '#667085' }}>
          最常见的原因是本站的 redirect_uri 不在认证服务的白名单里(它由 <code>WEB_ORIGIN</code> 决定)。
          配置方式见仓库文档 <code>docs/identity-integration.md</code>。
        </p>
      </Result>
    );
  }
  return (
    <div style={{ padding: '120px 0', textAlign: 'center' }}>
      <Spin size="large" tip="登录中…">
        <div style={{ height: 40 }} />
      </Spin>
    </div>
  );
}
