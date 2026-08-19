import { SafetyCertificateOutlined, WarningOutlined } from '@ant-design/icons';
import { Tag, Tooltip } from 'antd';

/** 签名状态的展示。未签名不是错误,但值得让人看见 —— 它决定了升级时能不能验身份连续性。 */
export default function SignatureTag({ state }: { state: string }) {
  if (state === 'Trusted') {
    return (
      <Tooltip title="包带有效签名,升级时可校验发布者身份连续性">
        <Tag color="success" bordered={false} icon={<SafetyCertificateOutlined />}>
          已签名
        </Tag>
      </Tooltip>
    );
  }
  if (state === 'Untrusted') {
    return (
      <Tag color="warning" bordered={false} icon={<WarningOutlined />}>
        自签名
      </Tag>
    );
  }
  return (
    <Tooltip title="作者未对该包签名。市场当前允许未签名的包上架">
      <Tag bordered={false}>未签名</Tag>
    </Tooltip>
  );
}
