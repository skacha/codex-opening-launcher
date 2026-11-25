# 第三方来源与许可

## DSH 原版开场界面

- 项目：[6shenhonghong9/dsh-startup-screen](https://github.com/6shenhonghong9/dsh-startup-screen)
- 原作者：沈宏宏。
- 移植版本：`4a2cfe6`，该版本位于参考视频发表前的历史中。
- 许可：MIT，全文保存在 `assets/reference-boot/LICENSE`。
- 使用文件：原版 `lib/splash.css` 与 `lib/splash.js`。

原版动画保留；CSS 基础重置选择器改用 `:where()`，避免覆盖布局间距，其他排版修正在本项目 `codex-layout.css` 实现。脚本新增 Codex 宿主接入钩子。品牌替换、标识替换、实际语音播放、原生窗口衔接和龙娘配色在本项目的适配层实现。上游原有的鲸鱼路径仍属于带许可的来源文件，实际展示容器在适配时替换为 Codex 终端符号。

上游第三方人声与音效文件未复制；当前六段语音由本机英文系统声线和本项目机械处理生成，BGM 由本项目合成。原版无素材界面音效使用其代码合成路径。

## Microsoft WebView2 SDK

- 官方包：`Microsoft.Web.WebView2`，版本 `1.0.3650.58`。
- 下载源：微软官方 NuGet 包源。
- 包 SHA-256：`911A472128C82AC8BAA0C486C23342CC9DD6E7DC50D754E676726642CA065C60`。
- 许可全文保存在 `assets/reference-boot/WEBVIEW2-LICENSE.txt`。

SDK DLL 在构建时还原，不直接提交仓库。运行需要电脑已有 WebView2 Runtime，不会修改 Codex 商店包。

## 角色配色与背景参考

黑、淡紫、银白来自用户在本聊天提供的龙娘三视图的颜色区域取样。根据用户后续加入角色背景的要求，使用内置 imagegen 将三视图中的正面角色整理为透明 PNG，保存为 `assets/dragon/dragon-front-v1.png`，用于线稿到上色的背景过渡。原始三视图不上传。完整编辑提示词与素材说明见 `assets/dragon/生成说明.md`；上游 DSH 的 MIT 许可不代表这张用户提供的角色参考图的许可。
