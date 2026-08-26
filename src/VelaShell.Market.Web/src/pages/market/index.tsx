import { PipelineStrip } from '@/components';
import { PLUGIN_PAGE_SIZE, SORT_OPTIONS } from '@/configs';
import { getStats, listFeatured, listPlugins, listTags } from '@/services/market';
import { keepResult } from '@/utils/request';
import { DownOutlined } from '@ant-design/icons';
import { useRequest } from '@umijs/max';
import { Empty, Pagination, Segmented, Skeleton } from 'antd';
import { useState } from 'react';
import FeaturedCard from './components/FeaturedCard';
import MarketHero from './components/MarketHero';
import PluginCard from './components/PluginCard';

/** 标签条默认只展开这么多个,其余收在「更多」后面。一整屏的标签本身就是一种噪音。 */
const VISIBLE_TAGS = 6;

/** 浏览页:深色首屏 + 安全流水线细带 + 顶部标签栏 + 卡片网格。 */
export default function MarketPage() {
  const [q, setQ] = useState('');
  const [tag, setTag] = useState<string | undefined>();
  const [sort, setSort] = useState<string>(SORT_OPTIONS[0].value);
  const [page, setPage] = useState(1);
  const [allTags, setAllTags] = useState(false);

  // 标签与站点数字只在首屏拉一次:它们随插件增减而变,但没必要跟着每次翻页重来。
  const { data: tags } = useRequest(listTags, { formatResult: keepResult, onError: () => undefined });
  const { data: stats } = useRequest(getStats, { formatResult: keepResult, onError: () => undefined });

  /**
   * 编辑推荐单独取一次,不从列表里挑 isFeatured —— 推荐位与排序、分页、筛选都无关,
   * 用户切到第 3 页或者按标签过滤之后,首屏那张卡片不该跟着消失。
   * 只在"没有筛选条件的第一页"上出现:带着搜索词还硬塞一张不相干的卡片是干扰。
   */
  const { data: featured } = useRequest(listFeatured, { formatResult: keepResult, onError: () => undefined });

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

  const tagList = tags ?? [];
  const shownTags = allTags ? tagList : tagList.slice(0, VISIBLE_TAGS);
  const pinned = featured?.items?.[0];
  // 推荐的那个插件已经在本页列表里时不再重复渲染一遍。
  const showFeatured = !!pinned && page === 1 && !q && !tag;
  const items = (data?.items ?? []).filter((item) => !showFeatured || item.id !== pinned!.id);

  return (
    <div className="market-shell">
      <MarketHero stats={stats} defaultValue={q} onSearch={(value) => reset(() => setQ(value))} />
      <PipelineStrip />

      <div className="market-body">
        <div className="market-filters">
          <div className="market-tagbar">
            <button type="button" className={`tag-chip${tag ? '' : ' tag-chip-active'}`} onClick={() => reset(() => setTag(undefined))}>
              全部
              {data ? <em>{data.total}</em> : null}
            </button>
            {shownTags.map((item) => (
              <button key={item.tag} type="button" className={`tag-chip${tag === item.tag ? ' tag-chip-active' : ''}`} onClick={() => reset(() => setTag(tag === item.tag ? undefined : item.tag))}>
                {item.tag}
                <em>{item.count}</em>
              </button>
            ))}
            {tagList.length > VISIBLE_TAGS ? (
              <button type="button" className="tag-chip tag-chip-ghost" onClick={() => setAllTags((value) => !value)}>
                {allTags ? '收起' : `更多 ${tagList.length - VISIBLE_TAGS} 个`}
                <DownOutlined style={{ fontSize: 11, transform: allTags ? 'rotate(180deg)' : undefined }} />
              </button>
            ) : null}
          </div>

          <div className="market-sort">
            <span className="market-count">{loading ? '加载中…' : `共 ${data?.total ?? 0} 个插件`}</span>
            <Segmented value={sort} onChange={(value) => reset(() => setSort(value as string))} options={SORT_OPTIONS as unknown as { label: string; value: string }[]} />
          </div>
        </div>

        {loading && !data ? (
          <div className="plugin-grid">
            {Array.from({ length: 8 }).map((_, index) => (
              <div className="plugin-card" key={index}>
                <Skeleton active avatar={{ shape: 'square', size: 44 }} paragraph={{ rows: 2 }} />
              </div>
            ))}
          </div>
        ) : items.length > 0 || showFeatured ? (
          <>
            <div className="plugin-grid">
              {showFeatured ? <FeaturedCard plugin={pinned!} /> : null}
              {items.map((plugin) => (
                <PluginCard key={plugin.id} plugin={plugin} />
              ))}
            </div>
            <Pagination style={{ marginTop: 28 }} align="center" current={data?.page ?? 1} pageSize={data?.size ?? PLUGIN_PAGE_SIZE} total={data?.total ?? 0} showSizeChanger={false} onChange={setPage} />
          </>
        ) : (
          <Empty style={{ padding: '72px 0' }} description={q || tag ? '没有匹配的插件' : '市场还没有插件,来发布第一个吧'} />
        )}
      </div>
    </div>
  );
}
