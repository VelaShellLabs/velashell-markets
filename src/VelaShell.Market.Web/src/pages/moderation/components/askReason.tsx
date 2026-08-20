import { Alert, App, Input, Space, Typography } from 'antd';

import type { ReactNode } from 'react';

type AppApi = ReturnType<typeof App.useApp>;

/** 一次审核操作的弹窗描述。 */
export type ReasonPromptOptions = {
  title: ReactNode;
  /** 说明这次操作到底会做什么 —— 尤其是"删了就没了"这种,不能只靠按钮名字暗示。 */
  description?: ReactNode;
  /** 不可逆操作的红色警示条。 */
  danger?: string;
  placeholder: string;
  okText: string;
};

/**
 * 弹一个"必须填原因"的确认框,填了才让过。
 *
 * 审核动作一律要原因,这不是形式主义:下架/隐藏对作者是单方面的处置,
 * 不给原因就等于让人盲目猜。原因去向各不相同(有的展示给作者、有的只进服务端日志),
 * 但"必须写"这条对五个动作是一样的,所以集中在这里,不在每个面板里各写一遍。
 */
export function askReason(api: Pick<AppApi, 'modal' | 'message'>, options: ReasonPromptOptions, onConfirm: (reason: string) => Promise<void>) {
  // 受控 state 会让每次输入都重渲整个 Modal;这里只需要在点确定时读一次最终值。
  let reason = '';
  api.modal.confirm({
    title: options.title,
    width: 520,
    icon: null,
    content: (
      <Space direction="vertical" size={12} style={{ width: '100%', marginTop: 8 }}>
        {options.danger ? <Alert type="error" showIcon title={options.danger} /> : null}
        {options.description ? <Typography.Text type="secondary">{options.description}</Typography.Text> : null}
        <Input.TextArea rows={3} placeholder={options.placeholder} maxLength={500} showCount onChange={(e) => (reason = e.target.value)} />
      </Space>
    ),
    okText: options.okText,
    okButtonProps: { danger: true },
    cancelText: '取消',
    onOk: async () => {
      if (!reason.trim()) {
        api.message.warning('请填写原因');
        // 抛错让 Modal 留在原地 —— 直接 return 的话框会关掉,用户以为操作生效了。
        throw new Error('reason required');
      }
      await onConfirm(reason.trim());
    },
  });
}
