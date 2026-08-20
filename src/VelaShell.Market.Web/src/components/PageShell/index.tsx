import { Space, Typography } from 'antd';

import type { ReactNode } from 'react';

/**
 * 页面外壳:统一的左右留白 + 标题区。
 *
 * 六个页面原先各写一遍 `<div className="market-page">` 加一个 `Typography.Title level={3}`,
 * 标题下的说明文字与右侧按钮的间距每处都差一点。收进来之后,页面文件里只剩内容本身。
 */
export default function PageShell({ title, description, extra, children }: { title?: ReactNode; description?: ReactNode; extra?: ReactNode; children: ReactNode }) {
  return (
    <div className="market-page">
      {title || extra ? (
        <div className="market-page-head">
          <Space orientation="vertical" size={2}>
            {title ? (
              <Typography.Title level={3} style={{ margin: 0 }}>
                {title}
              </Typography.Title>
            ) : null}
            {description ? <Typography.Text type="secondary">{description}</Typography.Text> : null}
          </Space>
          {extra ? <Space wrap>{extra}</Space> : null}
        </div>
      ) : null}
      {children}
    </div>
  );
}
