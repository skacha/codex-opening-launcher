# Command OS 实现与验证

## 原因与参考证据

用户要求按指定视频制作 Codex 开机动画，并切换新分支。已在浏览器实际查看 [BV1dppy6yEC9](https://www.bilibili.com/pt/video/BV1dppy6yEC9/) 中约 10 至 21 秒的开场：浅灰白底、左上工业排版、身份资料与授权标题、黑色旋转圆弧、核验通过以及欢迎页。该方向与 `main` 的 EVA 驾驶舱不同。

本版独立绘制相同类型的界面节奏，品牌替换为 Codex，橙色改为青蓝色，文案全部为英文。没有打包参考视频、鲸鱼标识、原作画面或参考中的商业歌曲。英文语音与此前的音量设置保持一致。

分支：`feat/bilibili-codex-boot`。本机任务栏入口仍指向 F 盘同一程序，构建后可直接使用。

## 最小实现

- `EvaBootScene.OnRender`：固定纸白背景与细网格；五阶段正文共用连续动画时钟，保留圆弧的旋转和半径变化。短程淡入与位置缓动衔接文字，避免整屏翻页。
- `DrawInterface`：使用身份资料排版，将下层圆弧遮到正文之后，避免穿过资料影响阅读。
- `DrawSync` / `DrawVerify`：黑色断环、青蓝卫星点和百分比，中央浅色文字横带覆盖圆弧；系统核验以三个状态点结束。
- `DrawWelcome`：欢迎标题、黑色 Codex 品牌条、终端符号和等待确认说明。原生按钮仍在统一的 1600×900 等比坐标内，保留鼠标和 Enter。
- `SplashWindow`：配套调整确认按钮、键盘焦点边框、页脚与设置主题名称。原有显示器选择与窗口接入逻辑保持不变。

阶段与核验编号为演出信息，界面注明 `DEMONSTRATION TELEMETRY`，不作为账号认证或真实工具检测。

## 稳健性修正与导出

导出完整有声视频时发现旧 `BootTimeline` 的元数据读取使用 `(object[])root["Metrics"]`。`JavaScriptSerializer.Deserialize<Dictionary<string, object>>` 在此产生的集合为 `ArrayList`，类型转换会触发 `InvalidCastException`，被原有 `catch` 吞掉后静默使用估算时长。

现在使用 `(System.Collections.IList)root["Metrics"]`，沿用已有数值校验和异常回退。行为变更点：有效元数据按真实 WAV 时长分配阶段，而非估算值；默认总长仍为 24 秒，确认控制不变。

当前阶段起点为 0、4.479846、9.241596、14.247791、19.541379 秒，同步阶段约 5.006195 秒。独立编译的边界校验已验证起点、各起点对应的视觉阶段和最终进度，不再落入原来的估算分配。

`MotionPreview` 离屏复用完整 `SplashWindow`，包含原生确认按钮与页脚。`render-motion.ps1` 新增可选导出时长、30/60 帧和音轨参数，默认六秒无声行为保留。音轨使用同一时间轴延迟五句语音，按运行时规则压低播报期间的 BGM。

## 验证结果

- .NET Framework 编译成功。
- 七项现有播放回归通过：内置动画、主题动画、有声开关、图片、素材缺失、无效视频和正常视频；所有预览均未启动 Codex。
- 最终版完整 24 秒实时预览：五段语音全部播完，随后进入等待确认，Enter 确认后退出；`userConfirmed=true`、`awaitedConfirmation=true`、`launchRequested=false`。
- 同版按钮样式此前也完成了鼠标点击确认；只有时间轴元数据读取与身份页遮罩随后调整。
- 最终实时预览绘制 3129 帧，平均 CPU 绘制约 0.78 毫秒，95 分位帧间隔约 17.23 毫秒；这是本机测试，不是其他电脑的帧率保证。
- 五段英文语音、基础电平提升和无削波验证通过。
- 26 秒 MP4 包含 1560 帧与有声音轨，全片解码成功；混音峰值约 -1.0 dBFS。
- 五个阶段的 PNG 已逐一检查；身份页环线避让、英文标题、原生按钮和页脚之间没有文字重叠。

实时报告位于 `artifacts\access-os-final.report.json`，鼠标确认报告位于 `artifacts\access-os-manual.report.json`。报告未上传。

本次未强制关闭 Codex 验证冷启动，也未修改 `DisplayHandoff.cs`。多屏接入继续使用此前验证过的实现，不能用预览报告中的 `sameMonitor=false` 断言正式接入失败，预览不会执行实际接入。

## 修改与新增文件的绝对路径

- `F:\Codex\Projects\codex-opening-launcher\EvaBootScene.cs`
- `F:\Codex\Projects\codex-opening-launcher\Launcher.cs`
- `F:\Codex\Projects\codex-opening-launcher\BootTimeline.cs`
- `F:\Codex\Projects\codex-opening-launcher\MotionPreview.cs`
- `F:\Codex\Projects\codex-opening-launcher\render-motion.ps1`
- `F:\Codex\Projects\codex-opening-launcher\README.md`
- `F:\Codex\Projects\codex-opening-launcher\docs\Command-OS实现与验证.md`
- `F:\Codex\Projects\codex-opening-launcher\docs\previews\access-os-ready.png`
- `F:\Codex\Projects\codex-opening-launcher\docs\previews\access-os-sync.png`
- `F:\Codex\Projects\codex-opening-launcher\docs\previews\access-os-preview.mp4`

本机另已重新构建 `F:\Codex\Projects\codex-opening-launcher\CodexOpening.exe`，不纳入 Git。

## 风险与回滚

新视觉在原主题模式中替换绘制器，沿用 `Mode=eva` 和 `EvaSeconds` 以兼容本机设置；其他分支的旧界面仍可从 `main` 构建。窗口、任务栏入口、Codex 数据路径和账号配置没有改变。

本地构建前备份为 `F:\Codex\Projects\codex-opening-launcher\artifacts\before-access-os`。如要回到原驾驶舱版，在干净工作区切换 `main` 后重新运行 `build.ps1`。如果只临时恢复本机程序，也可使用该备份中的 `CodexOpening.exe`；保留新分支源码便于继续调整。
