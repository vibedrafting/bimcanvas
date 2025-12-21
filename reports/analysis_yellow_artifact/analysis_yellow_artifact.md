# 调试报告：黄色边界框残留原因分析

## 1. 核心结论
经过代码深度分析，确认 **`SceneBuilder.ts` 中的 `clearScene` 方法存在严重逻辑缺陷**，导致嵌套对象（非场景根节点直接子对象）无法被正确移除。

这是导致“黄色边界框”以及其他对象（如门窗组件）在场景重置后依然残留的根本原因。

## 2. 问题详解

### 2.1 `clearScene` 的移除逻辑错误
在 `src/services/builders/SceneBuilder.ts` 中，`clearScene` 方法使用 `traverse` 遍历所有后代节点，但移除时统一使用了 `this.scene.remove(child)`。

```typescript
// SceneBuilder.ts
this.scene.traverse((child) => {
    // ...
    if (child !== this.scene) {
        if (shouldRemove(child)) {
             toRemove.push(child);
        }
    }
});

toRemove.forEach(child => {
    this.scene.remove(child); // <--- 错误！
    // ...
});
```

**错误原因**：
- `this.scene.remove(child)` 仅能移除 `this.scene` 的**直接子对象**。
- 对于嵌套在 `THREE.Group` 中的子对象（例如门窗的门框、面板，或者 `GhostManager` 中的对象），`child.parent` 不是 `this.scene`。
- 因此，调用 `this.scene.remove(child)` **无效**，对象依然保留在其父容器（Group）中。

### 2.2 与“黄色边界框”的关联
虽然 `SceneBuilder` 创建的 `BoxHelper` 通常直接添加到 `scene`（因此能被移除），但如果：
1.  **GhostManager**（或 `DragManager`）创建了 Ghost 对象，并将其放入 `ghostGroup`（一个 `THREE.Group`）。
2.  如果 Ghost 对象内部包含 `BoxHelper`（黄色线框），或者 `BoxHelper` 被添加到了 `ghostGroup` 中。
3.  `clearScene` 遍历时会找到这个 `BoxHelper`，并试图用 `this.scene.remove()` 移除它。
4.  由于 `BoxHelper` 的父级是 `ghostGroup` 而不是 `scene`，**移除失败**。
5.  `ghostGroup` 本身在 `clearScene` 中被显式跳过（`if (child !== this.scene)` 逻辑虽然排除了 root，但代码中并未显式移除 Group，且 `GhostManager` 的 Group 通常设计为常驻）。

因此，任何位于 Group 内部的 `BoxHelper` 都会成为“顽固分子”，无法被清理。

### 2.3 潜在的内存泄漏（门窗组件）
此 Bug 不仅影响黄色框，还导致所有 **门（Door）** 和 **窗（Window）** 的几何体无法被清除。
- `createDoor`/`createWindow` 创建了一个 `root` Group。
- 具体的 Mesh（门框、玻璃）是 `root` 的子节点。
- `clearScene` 无法移除 `root`（因为它不是 Mesh/Line/BoxHelper）。
- `clearScene` 试图移除 Mesh 子节点但失败（因为父级是 `root`）。
- 结果：每次加载新数据，旧的门窗对象都会保留在场景中，导致严重的性能下降和渲染重叠。

## 3. 其他发现
- **GhostManager 代码缺失**：读取到的 `GhostManager.ts` 似乎缺失了 `createGhost` 和 `removeGhost` 方法（尽管 `MoveTool` 和 `DragManager` 调用了它们）。这可能是文件读取问题或版本不同步，但如果 `GhostManager` 确实在运行时创建了包含 `BoxHelper` 的结构，上述 `clearScene` Bug 就会导致其残留。

## 4. 修复建议
修改 `SceneBuilder.ts` 中的移除逻辑，使用 `removeFromParent()`（Three.js r129+ 支持）或手动检查父级。

**建议修改代码：**

```typescript
toRemove.forEach(child => {
    // 正确的移除方式
    if (child.parent) {
        child.parent.remove(child);
    }
    
    // 资源释放逻辑保持不变
    if ((child as any).geometry) (child as any).geometry.dispose();
    // ...
});
```

此外，建议在 `clearScene` 中也考虑移除临时的 `THREE.Group` 容器，或者确保 `GhostManager` 在重置时彻底清理其 `ghostGroup`。
