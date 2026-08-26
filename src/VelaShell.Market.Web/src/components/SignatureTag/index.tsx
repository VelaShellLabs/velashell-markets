import Chip from '@/components/Chip';
import { SIGNATURE_TAG } from '@/configs';
import { SafetyCertificateOutlined, StopOutlined, WarningOutlined } from '@ant-design/icons';
import { Tooltip } from 'antd';
import type { ReactNode } from 'react';

const ICONS: Record<string, ReactNode> = {
  Trusted: <SafetyCertificateOutlined />,
  Untrusted: <WarningOutlined />,
  None: <StopOutlined />,
};

/** 签名状态的展示。未签名不是错误,但值得让人看见 —— 它决定了升级时能不能验身份连续性。 */
export default function SignatureTag({ state, icon = true }: { state?: MarketAPI.SignatureState; icon?: boolean }) {
  const key = state || 'None';
  const preset = SIGNATURE_TAG[key] ?? { text: key };
  const chip = (
    <Chip tone={preset.tone} icon={icon ? ICONS[key] : undefined}>
      {preset.text}
    </Chip>
  );
  return preset.tip ? <Tooltip title={preset.tip}>{chip}</Tooltip> : chip;
}
