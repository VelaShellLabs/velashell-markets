import { Alert, Button, Card, Form, Input, Upload, message } from 'antd';
import { api } from '../auth';

/** 上传页(骨架)。上传后包进隔离区,检测结论在「我的上传」里看。 */
export default function UploadPage() {
  const [form] = Form.useForm();

  const submit = async (values: any) => {
    const file = values.file?.[0]?.originFileObj;
    if (!file) {
      message.warning('请选择 .vpx 文件');
      return;
    }
    const body = new FormData();
    body.append('file', file);
    body.append('description', values.description ?? '');
    body.append('releaseNotes', values.releaseNotes ?? '');
    body.append('tags', values.tags ?? '');
    const response = await api('/uploads', { method: 'POST', body });
    const payload = await response.json();
    if (response.ok) {
      message.success(payload.message);
      form.resetFields();
    } else {
      message.error(payload.error ?? payload.detail ?? '上传失败');
    }
  };

  return (
    <div style={{ maxWidth: 720, margin: '32px auto', padding: '0 16px' }}>
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="上传的包会先进入隔离区"
        description="通过容器校验、结构检查与病毒扫描后才会自动发布;命中可疑项的包转人工复核。插件 id、版本号与兼容信息一律取自包内的 plugin.json,不接受手填。"
      />
      <Card>
        <Form form={form} layout="vertical" onFinish={submit}>
          <Form.Item
            name="file"
            label=".vpx 包"
            valuePropName="fileList"
            getValueFromEvent={(e) => (Array.isArray(e) ? e : e?.fileList)}
          >
            <Upload beforeUpload={() => false} maxCount={1} accept=".vpx">
              <Button>选择文件</Button>
            </Upload>
          </Form.Item>
          <Form.Item name="description" label="插件说明(Markdown)">
            <Input.TextArea rows={8} />
          </Form.Item>
          <Form.Item name="releaseNotes" label="本版本更新说明(Markdown)">
            <Input.TextArea rows={4} />
          </Form.Item>
          <Form.Item name="tags" label="标签(逗号分隔)">
            <Input placeholder="ssh, 运维, 数据库" />
          </Form.Item>
          <Button type="primary" htmlType="submit">
            上传
          </Button>
        </Form>
      </Card>
    </div>
  );
}
