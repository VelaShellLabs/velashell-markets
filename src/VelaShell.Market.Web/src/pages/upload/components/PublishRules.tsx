import { CloseOutlined, WarningOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';

const REJECTED = [
  '把 zip 改名成 .vpx —— 市场只认专属容器,读不出容器头就直接退回',
  '包内含 .exe / .msi 等可直接运行的文件',
  '路径逃逸、重名条目、解压炸弹',
  'plugin.json 里的 id 或版本与包内实际内容不符',
  '病毒库命中',
];

const NEEDS_REVIEW = [
  '包内含脚本(.ps1 / .sh …)或原生库',
  '带了本该由宿主提供的 Avalonia* / PluginSdk 程序集',
  '签名公钥与该插件既往版本不同 —— 换了签名密钥必须由人确认',
];

const AFTER = [
  '包落进隔离桶(vpx-quarantine),这个桶永远不对外可读',
  '静态检查与 ClamAV 依次跑完;引擎不可用时留在隔离区等重试,不会被当成干净放行',
  '全部通过就自动搬进正式桶并上架;可疑项转人工,有害则拒收并给出原因',
];

function Rule({ tone, icon, children }: { tone: 'danger' | 'warn' | 'accent'; icon: ReactNode; children: ReactNode }) {
  return (
    <div className="rule-row">
      <span className={`rule-mark chip-${tone}`}>{icon}</span>
      <span>{children}</span>
    </div>
  );
}

/**
 * 发布页右栏:会被拒收的、会转人工的、以及上传之后会发生什么。
 *
 * 这些话原先在页面上是两张 `<Card>` 里的 `<ul>`。改成图标 + 一行文字的清单,
 * 而不是继续套盒子 —— 右栏本来就是"读一眼就走"的东西,边框只会拖慢它。
 */
export default function PublishRules() {
  return (
    <aside className="upload-rail">
      <section className="rail-section">
        <h3 className="rail-title">会被直接拒收</h3>
        {REJECTED.map((text) => (
          <Rule key={text} tone="danger" icon={<CloseOutlined />}>
            {text}
          </Rule>
        ))}
      </section>

      <section className="rail-section">
        <h3 className="rail-title">不会被拒,但会转人工复核</h3>
        {NEEDS_REVIEW.map((text) => (
          <Rule key={text} tone="warn" icon={<WarningOutlined />}>
            {text}
          </Rule>
        ))}
      </section>

      <section className="rail-section">
        <h3 className="rail-title">上传之后</h3>
        {AFTER.map((text, index) => (
          <Rule key={text} tone="accent" icon={<b style={{ fontSize: 11 }}>{index + 1}</b>}>
            {text}
          </Rule>
        ))}
      </section>
    </aside>
  );
}
