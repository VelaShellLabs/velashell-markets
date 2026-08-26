import { PageShell } from '@/components';
import { inspectPackage, previewMarkdown, uploadPackage } from '@/services/uploads';
import { BookOutlined, CloudUploadOutlined, InboxOutlined, ProfileOutlined, SafetyOutlined } from '@ant-design/icons';
import { history } from '@umijs/max';
import { Alert, App, Button, Input, Result, Select, Skeleton, Steps, Tabs, Upload } from 'antd';
import { useState } from 'react';
import PickedPackage from './components/PickedPackage';
import PublishRules from './components/PublishRules';

const STEPS = [
  { title: '上传 .vpx', description: '选包并补上说明与标签' },
  { title: '隔离检测', description: '容器 / 结构 / 清单 / 病毒' },
  { title: '人工复核', description: '仅命中可疑项时才需要' },
  { title: '发布上架', description: '搬进正式桶,立即可下载' },
];

/** 发布页。上传后包进隔离区,检测结论在「我的上传」里看。 */
export default function UploadPage() {
  const { message } = App.useApp();

  const [file, setFile] = useState<File | null>(null);
  const [inspection, setInspection] = useState<UploadsAPI.Inspection | null>(null);
  const [inspecting, setInspecting] = useState(false);
  const [inspectError, setInspectError] = useState<string | null>(null);

  const [description, setDescription] = useState('');
  const [releaseNotes, setReleaseNotes] = useState('');
  const [tags, setTags] = useState<string[]>([]);
  const [previewHtml, setPreviewHtml] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState<UploadsAPI.UploadResult | null>(null);

  /**
   * 选好文件立刻预检:读出包内清单摊给人看,顺带把"id 已被别人认领""这个版本已发布"
   * 这两种**必然失败**的情况提前说清楚。预检不落盘、不入库、不排队。
   */
  const pick = async (picked: File) => {
    setFile(picked);
    setInspection(null);
    setInspectError(null);
    setInspecting(true);
    try {
      const result = await inspectPackage(picked);
      setInspection(result);
      // 作者没在页面上写过标签时,用清单里的信息给个起点比让他对着空框强。
      if (tags.length === 0 && result.contributes?.protocols?.length) {
        setTags(result.contributes.protocols.map((protocol) => protocol.displayName.toLowerCase()));
      }
    } catch (error: any) {
      const data = error?.response?.data;
      setInspectError(data?.error ?? data?.detail ?? '这个文件读不出来 —— 它可能不是一个 .vpx 容器。');
    } finally {
      setInspecting(false);
    }
  };

  const clear = () => {
    setFile(null);
    setInspection(null);
    setInspectError(null);
  };

  const preview = async () => {
    try {
      const { html } = await previewMarkdown(description);
      setPreviewHtml(html);
    } catch {
      setPreviewHtml('<p>预览失败。</p>');
    }
  };

  const submit = async () => {
    if (!file) {
      message.warning('请选择 .vpx 文件');
      return;
    }
    setBusy(true);
    try {
      const body = new FormData();
      body.append('file', file);
      body.append('description', description);
      body.append('releaseNotes', releaseNotes);
      body.append('tags', tags.join(','));
      setDone(await uploadPackage(body));
    } catch (error: any) {
      // 上传失败的原因(结构检查、清单不符…)对作者最有用,所以这条路径不走统一提示,
      // 直接把服务端那句话原样端出来。
      const data = error?.response?.data;
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
                clear();
                setDescription('');
                setReleaseNotes('');
                setTags([]);
              }}
            >
              继续上传
            </Button>,
          ]}
        />
      </div>
    );
  }

  // 预检已经断定会失败的两种情况,直接把提交按钮关掉:让人点一下再收一个 409 毫无意义。
  const blocked = inspection?.ownership === 'taken' || inspection?.versionState === 'published';

  return (
    <PageShell
      title="发布插件"
      description="包会先落进不对外开放的隔离桶,通过全部检测后才搬进正式桶并可下载。"
      extra={[
        <Button key="mine" icon={<ProfileOutlined />} onClick={() => history.push('/mine')}>
          查看我的上传
        </Button>,
        <Button key="doc" icon={<BookOutlined />} href="https://github.com/joesdu/velashell-plugin-toolchain/blob/main/docs/publishing.md" target="_blank">
          打包规范
        </Button>,
      ]}
    >
      <Steps size="small" current={0} labelPlacement="vertical" items={STEPS} style={{ margin: '4px 0 30px' }} />

      <div className="upload-body">
        <div className="upload-main">
          {file && inspection ? (
            <PickedPackage file={file} inspection={inspection} onRemove={clear} />
          ) : (
            <Upload.Dragger
              beforeUpload={(picked) => {
                // 返回 false:文件只留在页面里,由 submit 一次性连同文案发出去。
                void pick(picked as unknown as File);
                return false;
              }}
              maxCount={1}
              accept=".vpx"
              showUploadList={false}
              disabled={inspecting}
            >
              <p className="ant-upload-drag-icon">
                <InboxOutlined />
              </p>
              <p className="ant-upload-text">把 .vpx 拖到这里,或点击选择</p>
              <p className="ant-upload-hint">
                用 <code className="mono">dotnet build -t:PackVpx</code> 或 <code className="mono">vela-plugin pack</code> 生成
              </p>
            </Upload.Dragger>
          )}

          {inspecting ? <Skeleton active paragraph={{ rows: 2 }} /> : null}
          {inspectError ? <Alert type="error" showIcon message="读不出这个包" description={inspectError} action={<Button size="small" onClick={clear}>重新选择</Button>} /> : null}

          <div>
            <div className="upload-field-head">
              <label>插件说明(Markdown)</label>
              <span>支持标题、列表、代码块;HTML 会被清洗</span>
            </div>
            {/* 预览走服务端:插件页上的 HTML 就是同一个渲染器出的,
                前端换一个 Markdown 库就意味着预览和实际发布的东西可能不一样。 */}
            <Tabs
              size="small"
              onChange={(key) => key === 'preview' && preview()}
              items={[
                {
                  key: 'edit',
                  label: '编辑',
                  children: (
                    <Input.TextArea
                      rows={12}
                      value={description}
                      maxLength={20000}
                      showCount
                      onChange={(event) => setDescription(event.target.value)}
                      placeholder={'## 它能做什么\n\n…\n\n## 怎么用\n\n…'}
                    />
                  ),
                },
                {
                  key: 'preview',
                  label: '预览',
                  children: previewHtml ? (
                    <div className="markdown-body" style={{ minHeight: 200 }} dangerouslySetInnerHTML={{ __html: previewHtml }} />
                  ) : (
                    <div style={{ minHeight: 200, color: 'var(--ink-3)' }}>还没有内容。</div>
                  ),
                },
              ]}
            />
          </div>

          <div>
            <div className="upload-field-head">
              <label>本版本更新说明(Markdown)</label>
              <span>会显示在版本列表里</span>
            </div>
            <Input.TextArea rows={4} value={releaseNotes} maxLength={5000} onChange={(event) => setReleaseNotes(event.target.value)} placeholder={'- 修了什么\n- 加了什么'} />
          </div>

          <div>
            <div className="upload-field-head">
              <label>标签</label>
              <span>最多 10 个,会统一转小写</span>
            </div>
            {/* tags 模式:输入后回车即成为一个标签,逗号(中英文)也算分隔符。
                后端会统一转小写并去重,所以这里不重复做一遍归一。 */}
            <Select mode="tags" value={tags} onChange={(value) => setTags(value.slice(0, 10))} style={{ width: '100%' }} placeholder="输入后回车添加,例如 ssh、运维、数据库" tokenSeparators={[',', ',']} />
          </div>

          <div className="upload-actions">
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 7, fontSize: 12.5, color: 'var(--ink-3)' }}>
              <SafetyOutlined />
              送检结论与完整报告都能在「我的上传」里看到
            </span>
            <div style={{ display: 'flex', gap: 10 }}>
              <Button
                onClick={() => {
                  clear();
                  setDescription('');
                  setReleaseNotes('');
                  setTags([]);
                  setPreviewHtml(null);
                }}
              >
                重填
              </Button>
              <Button type="primary" size="large" icon={<CloudUploadOutlined />} loading={busy} disabled={!file || !inspection || blocked} onClick={submit}>
                上传并送检
              </Button>
            </div>
          </div>
        </div>

        <PublishRules />
      </div>
    </PageShell>
  );
}
