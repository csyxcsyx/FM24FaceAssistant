# FM24 头像助手

FM24 头像助手为球员、职员和特殊角色匹配头像，并将映射应用到 Football Manager 2024。程序适用于 Windows x64；不读取或修改游戏存档，读取游戏进程内存时只读。

## 安装

1. 从 GitHub Releases 下载 `FM24FaceAssistant-v1.0.0-win-x64.zip`。
2. 解压到可写入的文件夹，运行 `FM24头像助手.exe`。
3. 首次运行时可安装随包提供的 `FLAYUS skin 24 YUXIANde`。也可跳过；头像管理仍可使用，但游戏内 UID 显示和工具栏入口需要配套皮肤。

头像分配和设置保存在 `%LOCALAPPDATA%\FM24FaceAssistant`。本地图库单独存放，不包含在程序下载包或公开仓库中。没有图库时，可在程序中下载 NG_Regens 素材；此功能需要已安装 Python 和 Pillow，且 `python` 命令可用。下载过程可取消或接续。

## 使用

- **手动换图**：搜索姓名或 ID，选人物，再随机重选或点击候选头像预览；点击“应用并刷新游戏”保存并刷新皮肤。
- **自动补全**：扫描缺失头像，检查会合并更新名单；预览后应用结果。素材匹配优先使用本地区未用头像，再扩展至兼容地区，最后才按使用次数复用。可在界面中手动选择其他素材。
- **特殊角色**：为名单中找不到的人物创建角色，填写姓名、UID 和职位后选图并应用。隐藏角色 UID 由当前存档决定，助手不会仅凭姓名推算。
- **主教练头像**：FM 使用单独的个人头像设置。按程序提示导出图片后，在游戏“偏好选择 → 概况 → 改变轮廓贴图”中载入。
- **恢复原头像包**：在“设置与恢复”中恢复助手改动前的外部头像映射。不要手动删除程序生成的恢复备份。

刷新默认快捷键为 `Shift+R`。发送刷新按键成功后，游戏仍需时间重载大型头像包。

## 开发

需要 .NET 10 SDK 和 Windows 桌面开发组件。运行检查：

```powershell
dotnet run --project Tests/Tests.csproj -c Release
python -m unittest discover -s Tests -p test_download_ngregens.py
```

构建 Windows x64 自包含版本：

```powershell
dotnet publish FM24FaceAssistant.csproj -c Release -r win-x64 --self-contained true
```

应用代码采用 MIT 许可证，详见 [LICENSE](LICENSE)。姓名数据及其许可证见 `Data/Names`。随附 FLAYUS 皮肤是第三方作品，版权和许可条件不因本项目的 MIT 许可而改变；相关说明见 [第三方许可](THIRD-PARTY-NOTICES.md)。

## 资源与隐私

姓名和国籍信息仅在本机用于头像推荐，不会上传。NG_Regens 图库从其官方来源下载，版权归原作者；本项目不随包分发该图库。头像分配、设置和恢复备份保存在本机，不提交到版本库。
