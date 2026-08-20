/** 评价域。对应 services/reviews。 */
declare namespace ReviewsAPI {
  type Review = {
    displayName?: string;
    rating: number;
    bodyHtml: string;
    pluginVersion?: string;
    createdAt: string;
    updatedAt: string;
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
