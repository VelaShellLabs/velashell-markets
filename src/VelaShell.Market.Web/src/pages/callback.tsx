import { useEffect } from 'react';
import { history } from 'umi';
import { completeLogin } from '../auth';

/** OIDC 登录回调:用授权码换取令牌后回首页。 */
export default function CallbackPage() {
  useEffect(() => {
    completeLogin().finally(() => history.replace('/'));
  }, []);
  return <div style={{ padding: 32 }}>登录中…</div>;
}
