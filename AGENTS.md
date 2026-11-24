# 便单 Windows 版本更新

本项目的 GitHub 仓库是 `ShiShiBits1/biandan-windows`，日常开发和发布使用 `main`。

每次完成用户要求的版本更新，都要同步源码并发布 GitHub Release。除非用户要求仅在本地修改，否则将以下步骤作为同一次任务完成。

1. 在 `src/Version.cs` 同时更新 `AssemblyVersion` 和 `AssemblyFileVersion`。公开版本号使用前三段，例如 `1.6.0.0` 对应 `v1.6.0`。每个新版本使用独立版本号。
2. 运行 `build.ps1 -Package`，用构建出的 `Biandan.exe --smoke` 执行冒烟测试，确认进程成功退出并生成本次测试的 `smoke/passed.txt`。
3. 将本次源码、文档和版本号一起提交到 GitHub 的 `main`。若本地目录缺少 Git 历史，使用 GitHub 连接核对远端差异，以当前 `main` 为父提交同步，保留远端历史。
4. 等待“发布便单 Windows”工作流完成。核对 `v<版本号>` 指向本次源码提交，公开 Release 中的 `Biandan-Windows-x64-<版本号>.zip` 可以下载。向用户提供 Release 页面和 ZIP 链接。

如发布中断，可在 GitHub Actions 重新运行失败的任务。工作流会继续处理该版本的草稿，并核验已经公开的版本。
