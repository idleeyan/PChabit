# v2.22.5 修复概览

## 问题
用户反馈 v2.22.4 修复后键鼠统计依旧失效。

## 调查方法
直接查询数据库确认：
- **PChabit 正在运行**（AppSessions 持续写入到 18:25）
- **键鼠数据最近 10 分钟 0 条**（完全没新数据）
- **上次键鼠数据是 15:00**（3.5 小时前）

说明问题在**采集层**（钩子未真正工作），不是查询层。

## 发现的根因

### 根因 1：KeyboardMonitor.Stop() 错误 Dispose Timer
```csharp
// 错误代码（v2.22.4 之前）
public void Stop() {
    _idleCheckTimer.Stop();
    _idleCheckTimer.Dispose();  // ❌ 销毁后无法再 Start()
    // ...
}
```
健康检查 Stop+Start 重启钩子时，`_idleCheckTimer.Start()` 抛 ObjectDisposedException，被 try-catch 吞掉，导致钩子"假启动"。

### 根因 2：SetCurrentProcess 从未被调用
KeyboardMonitor/MouseMonitor 需要知道当前激活进程才能附加到事件，但全代码库**没有任何地方调用 `SetCurrentProcess`**——所以 `_currentProcess` 永远是 null。

## 修复内容

| 修复 | 影响 |
|------|------|
| KeyboardMonitor.Stop() 移除 Dispose | 钩子能真正重启 |
| AppMonitor.GetCurrentProcess() 公开方法 | 提供当前进程名 |
| MouseMonitor.SetCurrentProcess() 接口 | 占位实现 |
| MonitorManager._processSyncTimer 每秒同步 | 键鼠 Monitor 拿到当前进程 |
| 钩子回调诊断日志 [KB-Start]/[MS-Start]/[KB-Hook] | 下次可定位 |

## 版本
v2.22.4 → v2.22.5 | Release|x64 | 0 错误, 0 警告

## 经验教训
每次失败都涉及"日志不完整，无法定位问题"。应该第一时间添加最基础的诊断日志。
