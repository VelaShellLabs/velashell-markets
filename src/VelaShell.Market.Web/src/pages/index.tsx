import { useEffect, useState } from 'react';
import {
  Card, Col, Empty, Input, Pagination, Rate, Row, Segmented, Skeleton, Space, Tag, Typography,
} from 'antd';
import { DownloadOutlined, TagsOutlined } from '@ant-design/icons';
import { history } from 'umi';
import { api } from '../auth';

type PluginSummary = {
  id: string;
  displayName: string;
  summary?: string;
  excerpt?: string;
  author?: string;
  tags: string[];
  latestVersion?: string;
  latestApiLevel?: number;
  downloads: number;
  ratingAverage: number;
  ratingCount: number;
};

type Page = { total: number; page: number; size: number; items: PluginSummary[] };

const SIZE = 12;

/** 浏览页:搜索 + 标签过滤 + 排序 + 分页。 */
export default function IndexPage() {
  const [data, setData] = useState<Page | null>(null);
  const [tags, setTags] = useState<{ tag: string; count: number }[]>([]);
  const [loading, setLoading] = useState(true);
  const [q, setQ] = useState('');
  const [tag, setTag] = useState<string | undefined>();
  const [sort, setSort] = useState('updated');
  const [page, setPage] = useState(1);

  useEffect(() => {
    api('/plugins/tags')
      .then((r) => r.json())
      .then(setTags)
      .catch(() => setTags([]));
  }, []);

  useEffect(() => {
    setLoading(true);
    const query = new URLSearchParams({ sort, page: String(page), size: String(SIZE) });
    if (q) query.set('q', q);
    if (tag) query.set('tag', tag);
    api(`/plugins?${query}`)
      .then((r) => r.json())
      .then(setData)
      .catch(() => setData({ total: 0, page: 1, size: SIZE, items: [] }))
      .finally(() => setLoading(false));
  }, [q, tag, sort, page]);

  const reset = (fn: () => void) => {
    // 换搜索词/标签/排序都要回第一页 —— 停在第 5 页看空列表是最常见的"以为没数据"。
    setPage(1);
    fn();
  };

  return (
    <div className="market-page">
      <div className="market-hero">
        <h1>为 VelaShell 找一个插件</h1>
        <p>所有插件都经过容器校验、结构检查与病毒扫描后才会上架。</p>
        <Input.Search
          size="large"
          placeholder="搜索插件名称、id 或简介…"
          allowClear
          onSearch={(value) => reset(() => setQ(value))}
          style={{ maxWidth: 520 }}
        />
      </div>

      <Row gutter={24}>
        <Col xs={24} md={6}>
          <Card size="small" title={<Space><TagsOutlined />标签</Space>} style={{ marginBottom: 16 }}>
            {tags.length === 0 ? (
              <Typography.Text type="secondary">暂无标签</Typography.Text>
            ) : (
              <Space size={[6, 8]} wrap>
                <Tag.CheckableTag checked={!tag} onChange={() => reset(() => setTag(undefined))}>
                  全部
                </Tag.CheckableTag>
                {tags.map((t) => (
                  <Tag.CheckableTag
                    key={t.tag}
                    checked={tag === t.tag}
                    onChange={(checked) => reset(() => setTag(checked ? t.tag : undefined))}
                  >
                    {t.tag} · {t.count}
                  </Tag.CheckableTag>
                ))}
              </Space>
            )}
          </Card>
        </Col>

        <Col xs={24} md={18}>
          <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
            <Typography.Text type="secondary">
              {loading ? '加载中…' : `共 ${data?.total ?? 0} 个插件`}
            </Typography.Text>
            <Segmented
              value={sort}
              onChange={(value) => reset(() => setSort(value as string))}
              options={[
                { label: '最近更新', value: 'updated' },
                { label: '下载最多', value: 'downloads' },
                { label: '评分最高', value: 'rating' },
                { label: '最新发布', value: 'created' },
              ]}
            />
          </Space>

          {loading ? (
            <Row gutter={[16, 16]}>
              {Array.from({ length: 6 }).map((_, i) => (
                <Col xs={24} sm={12} xl={8} key={i}>
                  <Card><Skeleton active paragraph={{ rows: 2 }} /></Card>
                </Col>
              ))}
            </Row>
          ) : data && data.items.length > 0 ? (
            <>
              <Row gutter={[16, 16]}>
                {data.items.map((p) => (
                  <Col xs={24} sm={12} xl={8} key={p.id}>
                    <Card
                      className="plugin-card"
                      hoverable
                      onClick={() => history.push(`/plugins/${p.id}`)}
                      styles={{ body: { padding: 16 } }}
                    >
                      <Space direction="vertical" size={8} style={{ width: '100%' }}>
                        <div className="plugin-card-title">
                          <span className="plugin-card-name">{p.displayName}</span>
                          <Tag color="blue" bordered={false}>{p.latestVersion}</Tag>
                        </div>
                        <div className="plugin-card-summary">{p.summary || p.excerpt || '作者未填写简介'}</div>
                        <Space size={4} wrap>
                          {p.tags?.slice(0, 3).map((t) => (
                            <Tag key={t} bordered={false}>{t}</Tag>
                          ))}
                        </Space>
                        <Space split={<span className="plugin-card-meta">·</span>} size={8}>
                          <Space size={4}>
                            <Rate disabled allowHalf value={p.ratingAverage} style={{ fontSize: 12 }} />
                            <span className="plugin-card-meta">{p.ratingCount}</span>
                          </Space>
                          <span className="plugin-card-meta">
                            <DownloadOutlined /> {p.downloads}
                          </span>
                          {p.author ? <span className="plugin-card-meta">{p.author}</span> : null}
                        </Space>
                      </Space>
                    </Card>
                  </Col>
                ))}
              </Row>
              <Pagination
                style={{ marginTop: 24, textAlign: 'center' }}
                align="center"
                current={data.page}
                pageSize={data.size}
                total={data.total}
                showSizeChanger={false}
                onChange={setPage}
              />
            </>
          ) : (
            <Empty
              style={{ padding: '64px 0' }}
              description={q || tag ? '没有匹配的插件' : '市场还没有插件,来发布第一个吧'}
            />
          )}
        </Col>
      </Row>
    </div>
  );
}
