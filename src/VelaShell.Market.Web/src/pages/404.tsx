import { history } from '@umijs/max';
import { Button, Result } from 'antd';

export default function NotFoundPage() {
  return (
    <Result
      status="404"
      title="404"
      subTitle="这个页面不存在。"
      extra={
        <Button type="primary" onClick={() => history.replace('/')}>
          回到首页
        </Button>
      }
    />
  );
}
