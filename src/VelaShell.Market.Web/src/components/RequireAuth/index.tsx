import { login } from '@/utils/auth';
import { Outlet, useModel } from '@umijs/max';
import { Button, Result } from 'antd';

/**
 * 需要登录才有意义的页面的路由包装(config/routes.ts 的 `wrappers`)。
 *
 * 之前这些页面对匿名用户是直接渲染的:发布页能把表单填满、点上传才收到一个 401,
 * "我的上传"则只是一张空表 —— 两种都让人以为是服务出了问题。这里把话说在前面。
 *
 * 这不是权限边界,只是体验:真正的拦截在服务端(API 是 OIDC 资源服务器)。
 */
export default function RequireAuth() {
  const { initialState } = useModel('@@initialState');

  if (initialState?.currentUser) return <Outlet />;

  return (
    <div className="market-page">
      <Result
        status="403"
        title="请先登录"
        subTitle="浏览和搜索不需要登录;上传、评价与管理自己的插件需要一个账号。"
        extra={
          <Button type="primary" onClick={() => login()}>
            去登录
          </Button>
        }
      />
    </div>
  );
}
