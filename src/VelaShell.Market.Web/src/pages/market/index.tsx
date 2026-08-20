import { PLUGIN_PAGE_SIZE, SORT_OPTIONS } from '@/configs';
import { listPlugins, listTags } from '@/services/market';
import { TagsOutlined } from '@ant-design/icons';
import { keepResult } from '@/utils/request';
import { useRequest } from '@umijs/max';
import { Card, Col, Empty, Input, Pagination, Row, Segmented, Skeleton, Space, Tag, Typography } from 'antd';
import { useState } from 'react';
import PluginCard from './components/PluginCard';

/** 浏览页:搜索 + 标签过滤 + 排序 + 分页。 */
export default function MarketPage() {
  const [q, setQ] = useState('');
  const [tag, setTag] = useState<string | undefined>();
  const [sort, setSort] = useState<string>(SORT_OPTIONS[0].value);
  const [page, setPage] = useState(1);

  // 标签只在首屏拉一次:它随插件增减而变,但没必要跟着每次翻页重来。
  const { data: tags } = useRequest(listTags, { formatResult: keepResult, onError: () => undefined });

  const { data, loading } = useRequest(() => listPlugins({ q: q || undefined, tag, sort, page, size: PLUGIN_PAGE_SIZE }), {
    formatResult: keepResult,
    refreshDeps: [q, tag, sort, page],
    onError: () => undefined,
  });

  /** 换搜索词/标签/排序都要回第一页 —— 停在第 5 页看空列表是最常见的"以为没数据"。 */
  const reset = (apply: () => void) => {
    setPage(1);
    apply();
  };

  const hasTags = !!tags?.length;

  return (
    <div className="market-page">
      <div className="market-hero">
        <h1>为 VelaShell 找一个插件</h1>
        <p>所有插件都经过容器校验、结构检查与病毒扫描后才会上架。</p>
        <Input.Search size="large" placeholder="搜索插件名称、id 或简介…" allowClear onSearch={(value) => reset(() => setQ(value))} style={{ maxWidth: 520 }} />
      </div>

      <Row gutter={24}>
        {/* 没有标签时整列都不渲染 —— 留一个只写着"暂无标签"的空盒子,
            白占掉近 300px 宽度,右边的卡片反而被挤窄。 */}
        {hasTags ? (
          <Col xs={24} md={6} xl={5}>
            <Card
              size="small"
              title={
                <Space>
                  <TagsOutlined />
                  标签
                </Space>
              }
              style={{ marginBottom: 16 }}
            >
              <Space size={[6, 8]} wrap>
                <Tag.CheckableTag checked={!tag} onChange={() => reset(() => setTag(undefined))}>
                  全部
                </Tag.CheckableTag>
                {tags!.map((item) => (
                  <Tag.CheckableTag key={item.tag} checked={tag === item.tag} onChange={(checked) => reset(() => setTag(checked ? item.tag : undefined))}>
                    {item.tag} · {item.count}
                  </Tag.CheckableTag>
                ))}
              </Space>
            </Card>
          </Col>
        ) : null}

        <Col xs={24} md={hasTags ? 18 : 24} xl={hasTags ? 19 : 24}>
          <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
            <Typography.Text type="secondary">{loading ? '加载中…' : `共 ${data?.total ?? 0} 个插件`}</Typography.Text>
            <Segmented value={sort} onChange={(value) => reset(() => setSort(value as string))} options={SORT_OPTIONS as unknown as { label: string; value: string }[]} />
          </Space>

          {loading ? (
            <div className="plugin-grid">
              {Array.from({ length: 8 }).map((_, index) => (
                <Card key={index}>
                  <Skeleton active avatar={{ shape: 'square', size: 48 }} paragraph={{ rows: 2 }} />
                </Card>
              ))}
            </div>
          ) : data && data.items.length > 0 ? (
            <>
              <div className="plugin-grid">
                {data.items.map((plugin) => (
                  <PluginCard key={plugin.id} plugin={plugin} />
                ))}
              </div>
              <Pagination style={{ marginTop: 24 }} align="center" current={data.page} pageSize={data.size} total={data.total} showSizeChanger={false} onChange={setPage} />
            </>
          ) : (
            <Empty style={{ padding: '64px 0' }} description={q || tag ? '没有匹配的插件' : '市场还没有插件,来发布第一个吧'} />
          )}
        </Col>
      </Row>
    </div>
  );
}
