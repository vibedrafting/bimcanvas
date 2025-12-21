# 调试报告：场景中顽固的黄色边界框 (Yellow Bounds Artifact)

## 1. 问题描述
在切换到 "AI Vision" 视图或加载新的 Proposal 数据后，场景坐标原点 (0,0,0) 处始终显示一个黄色的 `BoxHelper` 边界框。即使调用了 `clearScene()` 清理场景，该对象似乎仍然存在或被重新创建。

## 2. 现象观察
- **视觉表现**：在 AI 模式下（背景深色），原点处清晰可见一个黄色的线框立方体。
- **截图证据**：见 `ai_vision_after_load_*.png`，左下角显示了该异常对象。
- **预期行为**：`clearScene()` 应移除所有几何体及其关联的辅助对象（包括 `BoxHelper`），只保留网格和坐标轴。

## 3. 技术背景
- **代码位置**：`src/services/builders/SceneBuilder.ts`
- **创建逻辑**：`setLayers()` 方法会为每个传入的 `THREE.Object3D` 创建一个 `THREE.BoxHelper`，并将其分配给 `LAYER_BOUNDS` 层。
  ```typescript
  private setLayers(object: THREE.Object3D) {
      // ...
      const boxHelper = new THREE.BoxHelper(object, 0xffff00);
      boxHelper.layers.set(LayerManager.LAYER_BOUNDS);
      this.scene.add(boxHelper);
  }
  ```
- **清理逻辑**：`clearScene()` 遍历 `this.scene.children`，尝试移除 `Mesh`, `Line`, `LineSegments` (BoxHelper 的基类) 等对象。

## 4. 已尝试的修复与调试
1.  **增强清理逻辑**：
    - 修改 `clearScene()`，显式检查 `child.type === 'BoxHelper'` 和 `instanceof THREE.LineSegments`。
    - 添加了对 `geometry` 和 `material` 的 `dispose()` 调用以防内存泄漏。
2.  **添加调试日志**：
    - 在 `clearScene()` 中打印 `Marking for removal` 和 `Skipping removal of`，试图追踪对象生命周期。
3.  **代码重构**：
    - 修复了 `SceneBuilder.ts` 中因编辑导致的语法错误和结构损坏，确保代码逻辑正确。

## 5. 疑似原因
尽管清理逻辑看起来是正确的，但对象依然存在，可能的原因包括：
- **GhostManager 干扰**：`GhostManager` 可能维护了自己的对象引用或在场景清理后重新添加了 Ghost 对象（及其 BoxHelper）。
- **对象未被正确识别**：某些 `BoxHelper` 可能挂载在 `Group` 内部而非场景根节点，而当前的 `clearScene` 遍历可能未覆盖深层级或逻辑有误。
- **异步/时序问题**：Vue 的响应式更新或 Three.js 的渲染循环可能在清理后又触发了某些创建逻辑。
- **Demo 场景残留**：`buildDemoScene` 创建的对象可能未被完全清除。

## 6. 下一步建议
- **检查 GhostManager**：审查 `src/services/interaction/GhostManager.ts`，确认其是否在场景清理时保留了对象。
- **深度遍历清理**：确保 `clearScene` 递归清理所有层级的子对象，而不仅仅是根节点的直接子对象。
- **唯一标识追踪**：通过日志记录该顽固对象的 UUID，在整个生命周期中追踪它的创建者。
