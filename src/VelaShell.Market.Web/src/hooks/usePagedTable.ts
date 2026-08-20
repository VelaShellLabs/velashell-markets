import { MODERATION_PAGE_SIZE } from '@/configs';
import { keepResult } from '@/utils/request';
import { useRequest } from '@umijs/max';
import type { TablePaginationConfig } from 'antd';
import { useCallback, useState } from 'react';

/** 后端分页列表的统一形状:审核台三个列表接口都是这个结构。 */
type PagedResult<T> = { total: number; items: T[] };

/**
 * 分页 + 筛选表格的状态机。
 *
 * 审核台的插件治理与评价治理原本各自抄了一份 rows / total / page / size / q / scope /
 * loading + load + useEffect,连"改筛选忘了回第一页"这种 bug 都要各修一次。
 * 收进这里之后两个面板只剩各自的列定义与处置动作。
 *
 * 取数走 umi 的 useRequest:refreshDeps 变了自动重拉,组件卸载后的响应会被丢弃,
 * 不会再出现切走页签又切回来时旧请求把新数据盖掉的竞态。
 */
export function usePagedTable<TItem, TFilters extends object>(
  fetcher: (params: TFilters & { page: number; size: number }) => Promise<PagedResult<TItem>>,
  initialFilters: TFilters,
  initialSize: number = MODERATION_PAGE_SIZE,
) {
  const [filters, setFiltersState] = useState<TFilters>(initialFilters);
  const [page, setPage] = useState(1);
  const [size, setSize] = useState(initialSize);

  const { data, loading, refresh } = useRequest(() => fetcher({ ...filters, page, size }), {
    formatResult: keepResult,
    refreshDeps: [filters, page, size],
    // 审核端点带 skipErrorHandler,失败会抛到这里。列表页要的是"空表 + 不炸",
    // 具体错误由页面级的权限探测负责说明。
    onError: () => undefined,
  });

  /** 改任何一个筛选条件都回到第一页 —— 停在第 5 页看空列表是最常见的"以为没数据"。 */
  const setFilters = useCallback((patch: Partial<TFilters>) => {
    setPage(1);
    setFiltersState((prev) => ({ ...prev, ...patch }));
  }, []);

  const pagination: TablePaginationConfig = {
    current: page,
    pageSize: size,
    total: data?.total ?? 0,
    showSizeChanger: true,
    onChange: (nextPage, nextSize) => {
      setPage(nextPage);
      setSize(nextSize);
    },
  };

  return {
    rows: data?.items ?? [],
    total: data?.total ?? 0,
    loading,
    filters,
    setFilters,
    reload: refresh,
    pagination,
  };
}
