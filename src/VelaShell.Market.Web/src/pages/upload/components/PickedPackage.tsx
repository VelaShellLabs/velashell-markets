import { Chip, SignatureTag } from '@/components';
import { formatSize } from '@/utils/format';
import { CheckOutlined, CloseOutlined, FileZipOutlined } from '@ant-design/icons';
import { Alert, Button } from 'antd';

/** 清单里那几个"决定这次认领的是谁"的字段。页面上一个都改不了。 */
function Field({ label, value }: { label: string; value?: string | number | null }) {
  return (
    <div className="upload-manifest-item">
      <span>{label}</span>
      <b>{value ?? '—'}</b>
    </div>
  );
}

/**
 * 选好文件之后、点「上传并送检」之前的那一屏。
 *
 * 它解决的是一个很具体的浪费:`plugin.json` 里的 id / 版本决定了这次上传认领的是
 * **哪个插件的哪个版本**,而这些字段页面上改不了。等真上传完再在「我的上传」里发现
 * "版本号忘了提"或者"id 打错前缀被判给了别人",代价是一次完整往返加一次隔离区占用。
 * 所以先读一遍清单摊开给人看,顺带把两种**必然失败**的情况直接说出来。
 */
export default function PickedPackage({ file, inspection, onRemove }: { file: File; inspection: UploadsAPI.Inspection; onRemove: () => void }) {
  const taken = inspection.ownership === 'taken';
  const published = inspection.versionState === 'published';

  return (
    <div className="upload-picked">
      <div className="upload-picked-head">
        <div className="upload-picked-file">
          <span className="upload-picked-icon">
            <FileZipOutlined />
          </span>
          <div style={{ minWidth: 0 }}>
            <div className="upload-picked-name">
              {file.name}
              <Chip tone="ok" icon={<CheckOutlined />}>
                容器可读
              </Chip>
            </div>
            <div className="upload-picked-sub">
              {formatSize(inspection.packageSize)} · 已在本地读出清单,尚未上传
            </div>
          </div>
        </div>
        <Button size="small" icon={<CloseOutlined />} onClick={onRemove}>
          移除
        </Button>
      </div>

      <div className="upload-manifest">
        <Field label="插件 id" value={inspection.pluginId} />
        <Field label="版本" value={inspection.version} />
        <Field label="apiLevel" value={inspection.apiLevel} />
        <Field label="最低宿主" value={inspection.minHostVersion ?? '不限'} />
        <div className="upload-manifest-item">
          <span>签名</span>
          <SignatureTag state={inspection.signature} icon={false} />
        </div>
      </div>

      <p className="rail-note" style={{ marginTop: 12 }}>
        以上字段读自包内 <code className="mono">plugin.json</code>,页面上改不了 —— 展示与实际安装的东西必须一致。
        {inspection.signatureFingerprint ? (
          <>
            <br />
            签名公钥指纹 <code className="mono">{inspection.signatureFingerprint}</code>
          </>
        ) : null}
      </p>

      {taken ? (
        <Alert
          type="error"
          showIcon
          style={{ marginTop: 12 }}
          message={`插件 id「${inspection.pluginId}」已由其他账号认领`}
          description="插件 id 是全局唯一的,而且发布后不可改(它同时是命令前缀与插件私有数据的命名空间)。请改用你自己的发布者前缀重新打包。现在传上去只会被拒。"
        />
      ) : null}

      {published ? (
        <Alert
          type="error"
          showIcon
          style={{ marginTop: 12 }}
          message={`${inspection.pluginId} 的 ${inspection.version} 已经发布过了`}
          description="改内容不改版本号会让已装用户永远拿不到修复。请提升版本号后重新打包。"
        />
      ) : null}

      {inspection.versionState === 'reupload' && !taken ? (
        <Alert
          type="info"
          showIcon
          style={{ marginTop: 12 }}
          message="这个版本在隔离区里已经有一份了"
          description="继续上传会覆盖那一份并重新送检。已发布的版本不受影响。"
        />
      ) : null}
    </div>
  );
}
