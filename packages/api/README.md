# @pzd/api

orval 生成产物所在包（架构 §14.1）。

- 现状（票 02）：包骨架 + 导出面，无生成物。
- 计划：`Pzd.Api` 构建期导出 `openapi.json` 后接入 orval；Turbo 任务依赖
  `api#generate → web#build`，自定义 mutator 处理错误、CSRF 头、版本头。
