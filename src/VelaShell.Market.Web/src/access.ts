/**
 * 权限由 initialState.currentUser 推导。
 * 是不是审核员由服务端(/me)说了算,这里只消费结论。
 * @see https://umijs.org/docs/max/access#access
 */
export default function access(initialState: { currentUser?: MarketAPI.Profile | null } | undefined) {
  const { currentUser } = initialState ?? {};
  return {
    signedIn: !!currentUser,
    canModerate: !!currentUser?.isModerator,
  };
}
