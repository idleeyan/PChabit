# Tai Browser Extension

用于 [PChabit](https://github.com/idleeyan/PChabit) 个人习惯数据收集系统的浏览器扩展。

## 功能

- 📊 **页面追踪**: 记录浏览的页面、停留时间
- 🔍 **搜索检测**: 自动检测搜索引擎查询
- 🖱️ **交互追踪**: 点击、滚动、表单提交
- 🔌 **实时同步**: 通过 WebSocket 与 Tai 服务端实时通信

## 安装

### Chrome

1. 打开 `chrome://extensions/`
2. 启用 "开发者模式"
3. 点击 "加载已解压的扩展程序"
4. 选择 `tai-browser-extension` 文件夹

### Edge

1. 打开 `edge://extensions/`
2. 启用 "开发人员模式"
3. 点击 "加载解压缩的扩展"
4. 选择 `tai-browser-extension` 文件夹

### Firefox

1. 打开 `about:debugging#/runtime/this-firefox`
2. 点击 "临时载入附加组件"
3. 选择 `tai-browser-extension` 文件夹中的 `manifest.json`

> 注意: Firefox 需要使用 Manifest V2 版本，请使用 `manifest-firefox.json`

## 配置

默认连接到 `ws://localhost:8765`。如需修改：

1. 打开扩展设置
2. 修改 WebSocket 服务器地址
3. 保存并重新连接

## 隐私

- 所有数据仅在本地处理
- 不会收集密码字段内容
- 不会收集敏感表单数据
- 用户可随时暂停或卸载扩展

## 开发

```bash
# 文件结构
tai-browser-extension/
├── manifest.json      # 扩展配置
├── background.js      # 后台服务脚本
├── content.js         # 内容脚本
├── popup.html         # 弹出窗口
├── popup.js           # 弹出窗口脚本
└── icons/             # 图标文件
```

## 图标转换

SVG 图标需要转换为 PNG 格式：

```bash
# 使用 ImageMagick
convert icons/icon16.svg icons/icon16.png
convert icons/icon48.svg icons/icon48.png
convert icons/icon128.svg icons/icon128.png

# 或使用在线工具
# https://svgtopng.com/
```

## 许可证

MIT License
