import { completeLogin } from '@/utils/auth';
import { history, useModel } from '@umijs/max';
import { Button, Result, Spin, Typography } from 'antd';
import { useEffect, useState } from 'react';

/**
 * OIDC 登录回调:用授权码换取令牌、刷新全局用户状态后回首页。
 *
 * 失败要**显式展示**而不是默默跳回首页 —— 回调失败最常见的原因是
 * redirect_uri 不在认证服务的白名单里,那种情况下静默跳转会让人以为"登录了但没生效",
 * 排查方向完全错。
 */
export default function CallbackPage() {
  const [error, setError] = useState<string | null>(null);
  const { initialState, setInitialState } = useModel('@@initialState');

  useEffect(() => {
    completeLogin()
      .then(async () => {
        // 令牌已落地,把 currentUser 灌进全局状态,导航栏立刻变成已登录形态。
        const currentUser = (await initialState?.fetchUserInfo?.()) ?? null;
        await setInitialState((state) => ({ ...state, currentUser }));
        history.replace('/');
      })
      .catch((e: Error) => setError(e.message));
    // 只在挂载时跑一次:授权码是一次性的,重复兑换必然失败。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (error) {
    return (
      <Result
        status="error"
        title="登录未能完成"
        subTitle={error}
        extra={[
          <Button type="primary" key="home" onClick={() => history.replace('/')}>
            回到首页
          </Button>,
        ]}
      >
        <Typography.Paragraph type="secondary">
          最常见的原因是本站的 redirect_uri 不在认证服务的白名单里(它由 <code>WEB_ORIGIN</code> 决定)。配置方式见仓库文档 <code>docs/identity-integration.md</code>。
        </Typography.Paragraph>
      </Result>
    );
  }

  return (
    <div className="market-center-stage">
      <Spin size="large" tip="登录中…">
        <div style={{ height: 40 }} />
      </Spin>
    </div>
  );
}
