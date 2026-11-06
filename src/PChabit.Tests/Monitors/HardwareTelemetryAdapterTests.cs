// E2-a 修订版（3.72.0）：适配器与持久化服务位于 App 层（平台 TFM），
// Tests（net10.0）无法引用 App/HardwareMonitor，相关桥接测试不可达。
// Core 接口契约测试见同目录 HardwareTelemetryContractTests.cs。
// 装配正确性由运行期验证覆盖（启动日志 + 样本表新记录 + 冒烟）。
