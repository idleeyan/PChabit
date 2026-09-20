# 版权与许可声明

本模块（`PChabit.HardwareMonitor`）的硬件采集代码移植自开源项目 **LiteMonitor**：

- 项目主页：https://github.com/Diorser/LiteMonitor
- 作者：Diorser
- 协议：MIT License

LiteMonitor 原文 MIT License 声明如下（依据上游仓库 LICENSE 文件保留）：

```
MIT License

Copyright (c) Diorser

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

移植过程中做了以下裁剪与适配（与上游代码的差异说明）：

- 移除 UI 渲染层（WinForms/GDI+）、插件系统、主题/多语言、流量历史、硬件历史、网页服务、更新器、内存清理（SystemOptimizer）、FPS 计数器与内核驱动安装（DriverInstaller）等外围功能；
- 采集逻辑（`HardwareMonitor` / `SensorMap` / `HardwareValueProvider` / `NetworkManager` / `DiskManager` / `PerformanceCounterManager` 等）保留核心实现，仅做命名空间与配置类（`HardwareMonitorOptions` 替代上游 `Settings`）适配；
- `TrafficLogger` 相关调用已移除（v1 不采集每日流量历史）。

任何对本模块的再分发须保留本声明。
