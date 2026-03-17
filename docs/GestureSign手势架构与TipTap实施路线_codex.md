# GestureSign 手势架构与 TipTap 实施路线

## 1. 总体目标

本路线按当前已确认的最终方向推进：

- 连续手势收敛为两指滑动 / 两指缩放
- 非连续手势统一通过录制创建并自动分类
- 所有手势定义进入全局手势库
- 应用层只保存手势绑定
- 特征手指只作为轨迹识别内部策略，不污染原始数据

---

## 2. 优先级 TODO

## P0：核心架构落地（必须先完成）

- [x] 会话层保留完整多指数据
- [x] 增加 `GestureAnalysis` / `GestureSessionSnapshot`
- [x] 建立 `Tap` / `TipTap` 后端配置模型
- [x] 打通 `MultiFingerTap` 后端执行链
- [x] 打通 TipTap 后端基础识别骨架
- [x] 连续手势运行时收敛为两指
- [x] 连续手势 UI 收敛为：继承 / 启用 / 禁用
- [x] 建立全局手势定义库模型（基础版：轨迹/录制结果/Contact 定义已具备统一 Id/Name 入口）
- [x] 建立应用级绑定层模型（基础版：Action 已支持 GestureId + GestureName 双链）
- [x] 让非连续手势录制结果进入分类器而不是直接落旧轨迹模型（训练模式 IPC 已支持分类结果对象）

## P1：创建与展示链路

- [x] 录制完成后生成 `RecordedGestureSample`
- [x] 实现 `GestureClassifier`
- [x] 录制完成后自动判断：轨迹 / Tap / TipTap
- [x] 动作列表展示手势类型标签（基础版）
- [x] 轨迹手势改为显示全部手指轨迹（基础版：DisplayPointPatterns 已接入显示链）
- [x] Tap / TipTap 显示语义化示意图（基础版）
- [x] 当命中已有全局手势时支持基础复用（自动匹配复用 + 录制结果提示已接入，完整三选一交互待增强）

## P2：全局复用与编辑能力

- [x] 所有手势定义支持稳定 `Id`（轨迹 / Tap / TipTap 已完成基础支持）
- [x] 所有手势定义支持可编辑 `Name`（轨迹 / Tap / TipTap 已完成基础支持）
- [x] 应用绑定全部改为按 `GestureId` 优先引用（兼容旧 GestureName）
- [x] 修改已有轨迹手势时，所有引用自动生效（按 GestureId 重绑）
- [x] UI 对“修改已有手势会影响所有引用”做明确提示（轨迹手势编辑已加提示）

## P3：清理旧实现与性能优化

- [x] 清理旧的 3/4/5 指连续手势配置与 UI（主界面与运行时已切换为两指模型）
- [x] 清理连续方向命令旧实现（运行时与配置对话框已移除）
- [x] 给轨迹手势建立候选索引缓存
- [x] 缓存轨迹模板预处理结果
- [x] 将特征手指正式收敛为内部匹配策略（原始 session 保持完整，匹配仅作局部策略）

---

## 3. 当前执行顺序

从现在开始按以下顺序推进：

1. P0 中剩余项
2. P1
3. P2
4. P3

除非遇到真正阻塞性问题，否则不中断推进。













## Review 收敛状态（2026-03-12）

- [x] C1/C2：ResetSessionTracking 清理 pending 状态并停止 delay timer
- [x] R1：Training 模式优先进入录制分支，避免 Tap/TipTap 短路录制结果
- [x] R2/R3：移除旧 GotNewPattern 主链与失效 stack-up 依赖
- [x] R4：TipTap 方向检测收敛为更稳的相对位置判定
- [x] C3：TipTap active session 成功后调用 OnAfterPointsCaptured
- [x] E2：TipTap session 正确初始化并维护 _allPointsCaptured
- [x] S1：SaveObject 改为临时文件写入后替换目标文件
- [x] S2/D1：GestureManager / ApplicationManager 增加轻量并发保护
- [x] S3：加载时检测 GestureId 冲突并输出 warning
- [x] R5：单指 tap-like 归类行为已加注释说明
- [x] E1：明确为设计约束，不作为 bug 修复
- [x] E3：明确为产品决策点，不作为当前阻塞问题
