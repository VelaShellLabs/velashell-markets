import { CheckOutlined, InfoCircleOutlined, RightOutlined } from '@ant-design/icons';

/** 流水线的五步。文案与 README 里那张图、发布页的步骤条保持一致。 */
const STEPS = ['上传 .vpx', '隔离桶', '结构与清单检查', 'ClamAV 病毒扫描', '发布可下载'];

/**
 * 首屏下方那条「安全流水线」细带。
 *
 * 它不是装饰:隔离区 → 静态检查 → 病毒扫描 → 发布这条链子是这个市场唯一别人抄不走的东西,
 * 而旧版把它只写在 README 里,页面上一个字都没有。放在首屏第一屏之内,访客在看到
 * 任何一张插件卡片之前就知道"这里的包是怎么来的"。
 */
export default function PipelineStrip() {
  return (
    <div className="pipeline-strip">
      <div className="pipeline-steps">
        {STEPS.map((step, index) => (
          <span className="pipeline-step" key={step}>
            {index > 0 ? <RightOutlined className="pipeline-arrow" /> : null}
            <span className="pipeline-dot">
              <CheckOutlined />
            </span>
            {step}
          </span>
        ))}
      </div>
      <span className="pipeline-note">
        <InfoCircleOutlined />
        引擎不可用时,包留在隔离区等重试,绝不当作通过
      </span>
    </div>
  );
}
