import { Alert, App, Button, Drawer, Empty, Form, Input, Popconfirm, Select, Space, Table, Typography } from 'antd';
import { DeleteOutlined, GithubOutlined } from '@ant-design/icons';
import { addTrustedPublisher, removeTrustedPublisher } from '@/services/me';
import { useEffect, useMemo } from 'react';

import { formatDateTime } from '@/utils/format';
import { keepResult } from '@/utils/request';
import { useRequest } from '@umijs/max';

interface TrustedPublishersDrawerProps {
  /** 正在管理的插件;为 null 时抽屉关闭。 */
  plugin: MeAPI.MyPlugin | null;
  /** 我名下的全部插件 —— 一个仓库发多个插件时,登记可以一次铺到几个插件上。 */
  allPlugins: MeAPI.MyPlugin[];
  settings: MeAPI.TrustedPublishingSettings;
  onClose: () => void;
  /** 登记或移除之后刷新列表。 */
  onChanged: () => void;
}

type FormValues = {
  repository: string;
  workflow: string;
  environment?: string;
  alsoPluginIds?: string[];
};

/** 生成给作者照抄的工作流片段:申请 OIDC 令牌 → 上传。 */
function workflowSnippet(settings: MeAPI.TrustedPublishingSettings, environment?: string | null) {
  const origin = window.location.origin;
  return [
    'jobs:',
    '  publish-market:',
    '    runs-on: ubuntu-latest',
    ...(environment ? [`    environment: ${environment}`] : []),
    '    permissions:',
    '      id-token: write   # 允许申请 GitHub OIDC 令牌',
    '      contents: read',
    '    steps:',
    '      # …先产出签好名的 .vpx(见发布指南)…',
    '      - name: Publish to VelaShell market',
    '        shell: bash',
    '        run: |',
    `          for vpx in bin/vpx/*.vpx; do`,
    `            token=$(curl -sSf -H "Authorization: Bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \\`,
    `              "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=${settings.audience}" | jq -r .value)`,
    `            curl -sS --fail-with-body -H "Authorization: Bearer $token" \\`,
    `              -F "file=@$vpx" ${origin}${settings.uploadPath}`,
    '          done',
  ].join('\n');
}

/**
 * 可信发布者管理。登记一条 = 授权「这个仓库的这个工作流」凭 GitHub 当场签发的令牌替我发版,
 * CI 里不用存任何商店密钥。包交进来之后照样进隔离区、过全部检测 —— 这里放宽的只是「谁能交包」。
 */
export default function TrustedPublishersDrawer({ plugin, allPlugins, settings, onClose, onChanged }: TrustedPublishersDrawerProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();

  useEffect(() => {
    if (plugin) {
      form.resetFields();
      form.setFieldsValue({ workflow: 'release.yml', alsoPluginIds: [] });
    }
  }, [plugin, form]);

  const otherPlugins = useMemo(() => allPlugins.filter((p) => p.id !== plugin?.id).map((p) => ({ value: p.id, label: `${p.displayName}(${p.id})` })), [allPlugins, plugin]);

  const { run: add, loading: adding } = useRequest(
    (values: FormValues) =>
      addTrustedPublisher({
        pluginIds: [plugin!.id, ...(values.alsoPluginIds ?? [])],
        repository: values.repository,
        workflow: values.workflow,
        environment: values.environment || undefined,
      }),
    {
      manual: true,
      formatResult: keepResult,
      onSuccess: (result: MeAPI.TrustedPublisherResult) => {
        const skipped = result.skipped.map((s) => `${s.pluginId}:${s.reason}`).join(';');
        if (result.added.length > 0) {
          message.success(`已登记到 ${result.added.length} 个插件${skipped ? `;跳过 ${skipped}` : ''}`);
        } else {
          message.warning(`没有新增登记:${skipped}`);
        }
        form.resetFields(['repository', 'environment', 'alsoPluginIds']);
        onChanged();
      },
      // 失败信息已由统一错误处理展示(仓库不存在、私有仓库、限流等原因都在 error 里)。
      onError: () => undefined,
    },
  );

  const { run: remove } = useRequest((publisherId: string) => removeTrustedPublisher(plugin!.id, publisherId), {
    manual: true,
    onSuccess: () => {
      message.success('已移除');
      onChanged();
    },
    onError: () => undefined,
  });

  const publishers = plugin?.trustedPublishers ?? [];

  return (
    <Drawer title={plugin ? `可信发布 · ${plugin.displayName}` : ''} width={720} open={!!plugin} onClose={onClose} destroyOnHidden>
      <Space orientation="vertical" size={20} style={{ width: '100%' }}>
        <Alert
          type="info"
          showIcon
          title="让 CI 直接发版,不用存商店密钥"
          description={
            <>
              登记后,该仓库的该工作流可以凭 GitHub Actions 当场签发的 OIDC 令牌,把这个插件的新版本推进商店。 包仍然会进隔离区、过签名与病毒检测,和手动上传一样。 匹配按仓库的数字 id
              进行,仓库改名不受影响;删库后被人同名重建则不会通过。
              <br />
              建议填写<b>部署环境</b>,并在 GitHub 上给该环境设置审批人或只允许标签触发 —— 否则任何有仓库写权限的人都能改这个工作流来发版。
            </>
          }
        />

        <Table<MeAPI.TrustedPublisher>
          rowKey="id"
          size="small"
          pagination={false}
          dataSource={publishers}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="还没有登记可信发布者" /> }}
          columns={[
            {
              title: '仓库',
              render: (_, row) => (
                <Space size={6}>
                  <GithubOutlined />
                  <a href={`https://github.com/${row.repository}`} target="_blank" rel="noreferrer">
                    {row.repository}
                  </a>
                </Space>
              ),
            },
            { title: '工作流', dataIndex: 'workflow', render: (value) => <span className="mono">{value}</span> },
            { title: '环境', dataIndex: 'environment', width: 110, render: (value) => value || <Typography.Text type="secondary">任意</Typography.Text> },
            { title: '登记于', dataIndex: 'addedAt', width: 170, render: formatDateTime },
            {
              title: '',
              width: 60,
              render: (_, row) => (
                <Popconfirm title="移除后这个工作流就不能再替你发版了。" okText="移除" okButtonProps={{ danger: true }} onConfirm={() => remove(row.id)}>
                  <Button type="text" danger icon={<DeleteOutlined />} aria-label="移除" />
                </Popconfirm>
              ),
            },
          ]}
        />

        <Form form={form} layout="vertical" onFinish={add} requiredMark={false}>
          <Typography.Title level={5} style={{ marginTop: 0 }}>
            登记新的可信发布者
          </Typography.Title>
          <Form.Item name="repository" label="GitHub 仓库" rules={[{ required: true, message: '请填写 owner/repo' }]} extra="格式 owner/repo。私有仓库需要商店配置了 GitHub 令牌才能登记。">
            <Input placeholder="VelaShellLabs/velashell-plugins" prefix={<GithubOutlined />} />
          </Form.Item>
          <Form.Item name="workflow" label="工作流文件" rules={[{ required: true, message: '请填写工作流文件名' }]} extra=".github/workflows/ 下的文件名,区分大小写。">
            <Input placeholder="release.yml" className="mono" />
          </Form.Item>
          <Form.Item name="environment" label="部署环境(可选,推荐)" extra="填了就只接受来自该 GitHub 环境的运行。">
            <Input placeholder="market" />
          </Form.Item>
          {otherPlugins.length > 0 && (
            <Form.Item name="alsoPluginIds" label="同时登记到" extra="同一个仓库发布多个插件时,一次登记完。">
              <Select mode="multiple" allowClear options={otherPlugins} placeholder="选择其他插件" />
            </Form.Item>
          )}
          <Button type="primary" htmlType="submit" loading={adding}>
            登记
          </Button>
        </Form>

        <div>
          <Typography.Title level={5}>工作流示例</Typography.Title>
          <Typography.Paragraph type="secondary">
            受众(audience)必须是 <span className="mono">{settings.audience}</span>。上传返回 202 表示已进隔离区;409 表示该版本已发布,重跑时可以当成功处理。
          </Typography.Paragraph>
          <div className="markdown-body">
            <Typography.Paragraph copyable={{ text: workflowSnippet(settings, publishers[0]?.environment) }} style={{ marginBottom: 0 }}>
              <pre>
                <code>{workflowSnippet(settings, publishers[0]?.environment)}</code>
              </pre>
            </Typography.Paragraph>
          </div>
        </div>
      </Space>
    </Drawer>
  );
}
