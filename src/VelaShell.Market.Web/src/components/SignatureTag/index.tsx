import { SIGNATURE_TAG } from '@/configs';
import { SafetyCertificateOutlined, WarningOutlined } from '@ant-design/icons';
import { Tag, Tooltip } from 'antd';

const ICONS: Record<string, React.ReactNode> = {
  Trusted: <SafetyCertificateOutlined />,
  Untrusted: <WarningOutlined />,
};

/** 签名状态的展示。未签名不是错误,但值得让人看见 —— 它决定了升级时能不能验身份连续性。 */
export default function SignatureTag({ state }: { state?: MarketAPI.SignatureState }) {
  const key = state || 'None';
  const preset = SIGNATURE_TAG[key] ?? { text: key };
  const tag = (
    <Tag color={preset.color} bordered={false} icon={ICONS[key]}>
      {preset.text}
    </Tag>
  );
  return preset.tip ? <Tooltip title={preset.tip}>{tag}</Tooltip> : tag;
}
