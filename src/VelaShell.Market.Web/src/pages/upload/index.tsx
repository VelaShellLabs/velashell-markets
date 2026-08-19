import { uploadPackage } from '@/services/uploads';
import { InboxOutlined } from '@ant-design/icons';
import { history } from '@umijs/max';
import { Alert, App, Button, Card, Col, Form, Input, Result, Row, Space, Steps, Typography, Upload } from 'antd';
import { useState } from 'react';

/** 发布页。上传后包进隔离区,检测结论在「我的上传」里看。 */
export default function UploadPage() {
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<MarketAPI.UploadResult | null>(null);

  const submit = async (values: any) => {
    const file = values.file?.[0]?.originFileObj;
    if (!file) {
      message.warning('请选择 .vpx 文件');
      return;
    }
    setBusy(true);
    try {
      const body = new FormData();
      body.append('file', file);
      body.append('description', values.description ?? '');
      body.append('releaseNotes', values.releaseNotes ?? '');
      body.append('tags', values.tags ?? '');
      const payload = await uploadPackage(body);
      setDone(payload);
    } catch (e: any) {
      const data = e?.response?.data;
      message.error(data?.error ?? data?.detail ?? '上传失败');
    } finally {
      setBusy(false);
    }
  };

  if (done) {
    return (
      <div className="market-page">
        <Result
          status="success"
          title={`${done.pluginId} ${done.version} 已进入隔离区`}
          subTitle="检测通过后会自动发布。命中可疑项的包会转人工复核,结论与原因都能在「我的上传」里看到。"
          extra={[
            <Button type="primary" key="mine" onClick={() => history.push('/mine')}>
              查看检测进度
            </Button>,
            <Button
              key="again"
              onClick={() => {
                setDone(null);
                form.resetFields();
              }}
            >
              继续上传
            </Button>,
          ]}
        />
      </div>
    );
  }

  return (
    <div className="market-page">
      <Typography.Title level={3}>发布插件</Typography.Title>

      <Steps
        size="small"
        current={0}
        style={{ margin: '20px 0 28px' }}
        items={[
          { title: '上传 .vpx' },
          { title: '隔离检测', description: '容器 / 结构 / 清单 / 病毒' },
          { title: '发布', description: '通过后自动上架' },
        ]}
      />

      <Row gutter={24}>
        <Col xs={24} lg={15}>
          <Card>
            <Form form={form} layout="vertical" onFinish={submit}>
              <Form.Item
                name="file"
                valuePropName="fileList"
                getValueFromEvent={(e) => (Array.isArray(e) ? e : e?.fileList)}
                rules={[{ required: true, message: '请选择 .vpx 包' }]}
              >
                <Upload.Dragger beforeUpload={() => false} maxCount={1} accept=".vpx">
                  <p className="ant-upload-drag-icon">
                    <InboxOutlined />
                  </p>
                  <p className="ant-upload-text">把 .vpx 拖到这里,或点击选择</p>
                  <p className="ant-upload-hint">
                    用 <code>dotnet build -t:PackVpx</code> 或 <code>vela-plugin pack</code> 生成
                  </p>
                </Upload.Dragger>
              </Form.Item>

              <Form.Item name="description" label="插件说明(Markdown)">
                <Input.TextArea rows={10} placeholder={'## 它能做什么\n\n…\n\n## 怎么用\n\n…'} />
              </Form.Item>
              <Form.Item name="releaseNotes" label="本版本更新说明(Markdown)">
                <Input.TextArea rows={4} />
              </Form.Item>
              <Form.Item name="tags" label="标签" extra="逗号分隔,最多 10 个,会统一转小写">
                <Input placeholder="ssh, 运维, 数据库" />
              </Form.Item>

              <Space>
                <Button type="primary" htmlType="submit" loading={busy} size="large">
                  上传并送检
                </Button>
                <Button onClick={() => form.resetFields()}>重填</Button>
              </Space>
            </Form>
          </Card>
        </Col>

        <Col xs={24} lg={9}>
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message="包会先进隔离区"
            description="上传的包落在不对外开放的隔离桶里,只有通过容器校验、结构检查与病毒扫描后才会搬进正式桶并可下载。"
          />
          <Card size="small" title="几条会被直接拒收的情况">
            <ul style={{ paddingLeft: 18, margin: 0, color: '#475467', lineHeight: 2 }}>
              <li>
                把 zip 改成 <code>.vpx</code>(市场只认专属容器)
              </li>
              <li>
                包内含 <code>.exe</code> / <code>.msi</code> 等可直接运行的文件
              </li>
              <li>路径逃逸、重名条目、解压炸弹</li>
              <li>清单里的 id / 版本与包不符</li>
              <li>病毒库命中</li>
            </ul>
          </Card>
          <Card size="small" title="不会被拒、但会转人工的情况" style={{ marginTop: 16 }}>
            <ul style={{ paddingLeft: 18, margin: 0, color: '#475467', lineHeight: 2 }}>
              <li>
                包内含脚本(<code>.ps1</code> / <code>.sh</code> …)或原生库
              </li>
              <li>
                带了本该由宿主提供的 <code>Avalonia*</code> / <code>PluginSdk</code>
              </li>
              <li>签名公钥与该插件既往版本不同</li>
            </ul>
          </Card>
        </Col>
      </Row>
    </div>
  );
}
