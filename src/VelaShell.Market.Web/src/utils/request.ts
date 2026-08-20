/**
 * umi 的 `useRequest` 有一个必须知道的默认行为:它会给 ahooks 传
 * `formatResult: (result) => result?.data`,也就是**再往里取一层 `data`**。
 *
 * 那是给"响应体包一层 { data }"的后端准备的。本项目的接口直接返回负载
 * (统一错误处理里已经把 axios 的 response 拆开了),再取一层就成了 `undefined` ——
 * 页面上表现为"接口明明有数据,列表却永远是空的",而且不报任何错。
 *
 * 所以每个 `useRequest` 都要显式写 `formatResult: keepResult` 把它顶掉。
 * 顺带一个好处:带 formatResult 的那条重载能正确推出 `data` 的类型,
 * 不写的话 `data` 会退化成 `{}`,后面每处访问字段都要 as 一下。
 */
export const keepResult = <T>(result: T): T => result;
