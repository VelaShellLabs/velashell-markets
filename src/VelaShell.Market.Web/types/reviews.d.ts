/** 评价域。对应 services/reviews。 */
declare namespace ReviewsAPI {
  type Review = {
    /** 评价 id。作者要回复某一条时得能指名道姓。 */
    id: string;
    displayName?: string;
    rating: number;
    bodyHtml: string;
    pluginVersion?: string;
    createdAt: string;
    updatedAt: string;
    /** 作者回复(渲染结果)。作者能解释,但删不掉别人说过的话。 */
    authorReplyHtml?: string | null;
    /** 作者回复原文,点「编辑回复」时用来预填输入框。 */
    authorReply?: string | null;
    authorReplyAt?: string | null;
  };

  type ReviewPage = {
    total: number;
    page: number;
    size: number;
    /** 各星级(1–5)的条数,画"应用商店式"分布条用。 */
    distribution?: Record<string, number>;
    items: Review[];
  };

  type MyReview = {
    rating: number;
    body?: string;
    updatedAt: string;
  };

  type ReviewDraft = {
    rating: number;
    body?: string | null;
  };
}
