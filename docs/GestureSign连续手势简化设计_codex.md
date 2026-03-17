# GestureSign 连续手势简化设计

## 1. 设计结论

连续手势不再作为一个通用的“多指连续命令系统”对外暴露，而收敛为两种明确能力：

- 两指滑动
- 两指缩放

其中：

- 两指滑动对应滚动能力（含惯性滚动等增强）
- 两指缩放对应缩放能力

3/4/5 指连续手势支持从产品设计与代码结构中一并移除，不再保留旧的过度抽象实现。

---

## 2. 用户交互

连续手势仍然保留独立栏目，但交互简化为三态：

- 继承
- 启用
- 禁用

默认选择：

- 继承

当用户选择“启用”时，可展开对应高级设置：

### 两指滑动高级设置

- 惯性滚动开关
- 滚动方向
- 反向
- 灵敏度
- WinUI 兼容补偿

### 两指缩放高级设置

- 缩放速度
- 缩放灵敏度

主界面不再展示：

- 手指数选择
- 自定义连续方向命令
- 3/4 指连续配置

---

## 3. 配置模型方向

建议将原有面向 2/3/4 指的 `ContinuousGestureSettings/ContinuousGestureConfig`，逐步收敛为更明确的两指模型。

目标模型：

```csharp
public enum InheritSwitch
{
    Inherit = 0,
    Enabled = 1,
    Disabled = 2,
}

public class TwoFingerGestureSettings
{
    public InheritSwitch Scroll { get; set; } = InheritSwitch.Inherit;
    public InheritSwitch Zoom { get; set; } = InheritSwitch.Inherit;

    public InertialScrollSettings ScrollSettings { get; set; } = new InertialScrollSettings();
    public TwoFingerZoomSettings ZoomSettings { get; set; } = new TwoFingerZoomSettings();
}
```

说明：

- `Scroll` / `Zoom` 各自独立支持：继承 / 启用 / 禁用
- 配置范围严格限定为两指
- 不再暴露 `ContactCount`
- 不再暴露连续方向命令绑定

---

## 4. 运行时逻辑方向

运行时只保留两条连续手势主线：

- 两指滑动 -> 滚动执行器
- 两指缩放 -> 缩放执行器

不再支持：

- 3/4 指连续滚动
- 3/4 指自定义连续方向命令
- 任意手指数连续命令分支

这意味着 `ContinuousGestureTrigger` 也要同步收敛：

- 输入不是 2 指时，直接退出连续手势主逻辑
- 只保留滚动/缩放判定与执行

---

## 5. 迁移原则

- UI 先简化
- 后端保留最小兼容读取能力即可
- 旧的 3/4 指连续手势配置不再在 UI 中显示或编辑
- 如果要一次到位，则可以直接删除相关实现与配置分支

当前方向按“一次到位”处理：

- 删除 3/4 指连续手势实现
- 删除连续方向命令模式
- 保留两指滚动/缩放

---

## 6. 与整体架构的关系

在新的整体手势架构中：

- 连续手势 = 两指滑动 / 两指缩放
- 非连续手势 = 录制创建 + 自动分类
- Tap / TipTap / 轨迹手势不再与连续手势共享抽象模型

这会让 GestureSign 的产品结构更加清晰：

- 连续：两指系统交互增强
- 非连续：录制型手势

---

## 7. 下一步实施

1. 简化连续手势文档和 UI
2. 简化连续手势配置模型
3. 删除 `ContinuousGestureTrigger` 中 3/4 指与方向命令相关逻辑
4. 构建并回归验证两指滚动/缩放

