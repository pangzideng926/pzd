import { PassThrough } from "node:stream";

import type { AppLoadContext, EntryContext } from "react-router";
import { createReadableStreamFromReadable } from "@react-router/node";
import { ServerRouter } from "react-router";
import { isbot } from "isbot";
import type { PipeableStream, RenderToPipeableStreamOptions } from "react-dom/server";
import { renderToPipeableStream } from "react-dom/server";

/**
 * F-03-A5：自定义 server entry，只做两件事，都是为了让 aspire 下 web 起不来的原因可见。
 *
 * 1) **不让 abort 的 TDZ 掩盖真错误**。`@react-router/dev` 7.18.4 的默认 entry 形状是：
 *
 *    let timeoutId = setTimeout(() => abort(), streamTimeout + 1000);  // 先挂定时器
 *    const { pipe, abort } = renderToPipeableStream(...);              // abort 才声明
 *
 *    一旦 `renderToPipeableStream` **同步抛出**，`abort` 永远停在暂时性死区（TDZ），
 *    真错误在 6 秒后被定时器里的 `ReferenceError: Cannot access 'abort' before initialization`
 *    覆盖，进程再以 exit 1 退出。表现就是「Running 几秒后退出、日志里只有 TDZ」。
 *    这里把 `abort` 提前声明、定时器挪到渲染之后，并在同步路径上 try/catch 打出真错误。
 *
 * 2) **dev 下 `/` 的应答由我们自己掌控**：SSR 关闭（`ssr: false`，SPA 模式）时
 *    React Router 仍用这份 entry 渲染文档外壳，因此这里的 `onAllReady` 决定 index.html
 *    何时可写；文档中间件不再插手 SSR 首帧。
 *
 * 不删 `setTimeout`：那会把 6 秒的流超时保护一起删掉，卡住的渲染会一直挂到进程被杀。
 */

/** 流超时：超过这段时间仍未 flush 完就中止渲染，避免请求永久挂起。 */
export const streamTimeout = 5_000;

export default function handleRequest(
  request: Request,
  responseStatusCode: number,
  responseHeaders: Headers,
  routerContext: EntryContext,
  _loadContext: AppLoadContext,
): Response | Promise<Response> {
  // https://httpwg.org/specs/rfc9110.html#HEAD
  if (request.method.toUpperCase() === "HEAD") {
    return new Response(null, {
      status: responseStatusCode,
      headers: responseHeaders,
    });
  }

  return new Promise<Response>((resolve, reject) => {
    let shellRendered = false;
    const userAgent = request.headers.get("user-agent");

    // 爬虫与 SPA 模式要等全部内容就绪再回包，否则拿到的 index.html 不完整。
    // https://react.dev/reference/react-dom/server/renderToPipeableStream#waiting-for-all-content-to-load-for-crawlers-and-static-generation
    const readyOption: keyof RenderToPipeableStreamOptions =
      (userAgent && isbot(userAgent)) || routerContext.isSpaMode ? "onAllReady" : "onShellReady";

    let timeoutId: ReturnType<typeof setTimeout> | undefined;
    let abort: (() => void) | undefined;

    let stream: PipeableStream;

    try {
      stream = renderToPipeableStream(<ServerRouter context={routerContext} url={request.url} />, {
        [readyOption]() {
          shellRendered = true;

          const body = new PassThrough({
            final(callback) {
              // 流写完就清掉定时器，避免闭包被留住。
              clearTimeout(timeoutId);
              timeoutId = undefined;
              callback();
            },
          });

          responseHeaders.set("Content-Type", "text/html");

          stream.pipe(body);

          resolve(
            new Response(createReadableStreamFromReadable(body), {
              headers: responseHeaders,
              status: responseStatusCode,
            }),
          );
        },
        onShellError(error: unknown) {
          // 失败路径也要清定时器（默认 entry 只在成功 final 里清）：否则 6 秒后
          // 定时器还会对着已经失败的流调 abort，制造一条与本因无关的二次错误。
          clearTimeout(timeoutId);
          timeoutId = undefined;

          console.error("[pzd/entry.server] 文档外壳渲染失败（这是真错误，不是 abort TDZ）：", error);
          reject(error);
        },
        onError(error: unknown) {
          responseStatusCode = 500;

          // 外壳已发出后的流式错误只能记日志（响应头已定），首帧错误由 onShellError 负责。
          if (shellRendered) {
            console.error(error);
          }
        },
      });
    } catch (error) {
      // 同步抛出就是真错误：默认 entry 会把它吞到 6 秒后的 TDZ 后面，这里立刻暴露。
      // 这条路径上定时器还没挂（它在渲染成功之后才挂），clearTimeout 只是显式兜底。
      clearTimeout(timeoutId);
      timeoutId = undefined;

      console.error(
        "[pzd/entry.server] renderToPipeableStream 同步抛出（默认 entry 会被 abort TDZ 掩盖 6 秒）：",
        error,
      );
      reject(error);
      return;
    }

    abort = stream.abort;

    // 定时器必须在渲染成功之后挂：此时 abort 一定已初始化，不会再踩 TDZ。
    // 到点仍未决定响应时显式失败——超时保护要保住，但不能再变成「请求永久挂起」。
    timeoutId = setTimeout(() => {
      abort?.();

      if (!shellRendered) {
        reject(new Error(`SSR 流在 ${streamTimeout + 1_000}ms 内未就绪，已中止渲染（流超时保护）。`));
      }
    }, streamTimeout + 1_000);
  });
}
