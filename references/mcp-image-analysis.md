# MCP 工具返回图片的深入分析

## 我的原始结论

我之前告诉你 MCP 工具可以返回 base64 编码的图片给 AI，格式如下：

```javascript
return {
  content: [{
    type: 'image',
    data: 'base64-encoded-data',
    mimeType: 'image/png'
  }]
};
```

## 重新审查后的发现

### ✅ 理论上支持的证据

1. **MCP 官方规范**明确支持图片类型：
   - 文档：https://modelcontextprotocol.io/specification/2025-06-18/server/tools
   - 格式：`{ type: "image", data: "base64-encoded-data", mimeType: "image/png" }`

2. **Agent SDK 类型定义**包含图片支持：
   ```typescript
   type CallToolResult = {
     content: Array<{
       type: 'text' | 'image' | 'resource';
     }>;
     isError?: boolean;
   }
   ```

3. **Cursor 文档**有实际例子：
   ```javascript
   server.tool("generate_image", async (params) => {
     return {
       content: [{
         type: "image",
         data: RED_CIRCLE_BASE64,
         mimeType: "image/jpeg",
       }],
     };
   });
   ```

### ⚠️ 实践中的问题

1. **没有官方 Agent SDK 示例**
   - 我查阅了所有官方文档（https://platform.claude.com/docs/en/agent-sdk/）
   - 所有 Custom Tools 示例都只返回文本
   - **没有一个官方示例展示如何在 Agent SDK 中返回图片**

2. **Claude Desktop 的 1MB 限制**
   - GitHub 讨论：https://github.com/orgs/modelcontextprotocol/discussions/199
   - Claude Desktop 对工具返回内容有硬性 1MB 限制
   - 超过限制会完全报错，无法显示

3. **字段名称的不一致**
   - MCP 规范使用：`data` 字段
   - 我找到一个社区例子使用：`source` 字段
   - 这种不一致性令人担忧

## 我的修正结论

### 技术上可行，但有重要限制

**理论上**：Agent SDK 通过 MCP 确实可以返回图片，因为：
- MCP 协议支持
- Agent SDK 类型定义支持
- Cursor 等其他 MCP 客户端已经实现

**但实践中**：
- ✅ **应该能工作** - 基于协议和类型定义
- ⚠️ **没有官方示例验证** - 官方文档没有提供任何例子
- ⚠️ **必须严格控制图片大小** - 需要压缩到远小于 1MB
- ⚠️ **可能存在未记录的限制** - 既然官方没有示例，可能有原因

## 推荐的实现策略

### 方案 1：保守方法（推荐给生产环境）

**不直接返回图片**，而是：
1. 将截图保存到文件
2. 返回文件路径
3. 让 Claude 使用内置的 `Read` 工具读取图片

```javascript
tool("take_screenshot", ..., async (args) => {
  const imgBuffer = await screenshot();
  const filePath = './screenshots/screenshot.png';
  await fs.writeFile(filePath, imgBuffer);
  
  return {
    content: [{
      type: "text",
      text: `截图已保存到 ${filePath}，请使用 Read 工具查看。`
    }]
  };
});
```

### 方案 2：实验性方法（需要测试）

**尝试直接返回图片**，但做好失败准备：

```javascript
import sharp from 'sharp';

tool("take_screenshot", ..., async (args) => {
  try {
    // 截图
    let imgBuffer = await screenshot();
    
    // 激进压缩到 < 500KB
    imgBuffer = await sharp(imgBuffer)
      .resize(800, 600, { fit: 'inside' })
      .png({ quality: 60, compressionLevel: 9 })
      .toBuffer();
    
    const base64 = imgBuffer.toString('base64');
    const sizeKB = Buffer.byteLength(base64, 'base64') / 1024;
    
    if (sizeKB > 512) {
      // 太大，回退到文件方案
      await fs.writeFile('./screenshot.png', imgBuffer);
      return {
        content: [{
          type: "text",
          text: `图片太大(${sizeKB.toFixed()}KB)，已保存到 screenshot.png`
        }]
      };
    }
    
    // 尝试返回图片
    return {
      content: [{
        type: "image",
        data: base64,
        mimeType: "image/png"
      }]
    };
    
  } catch (error) {
    return {
      content: [{
        type: "text",
        text: `截图失败: ${error.message}`
      }],
      isError: true
    };
  }
});
```

### 方案 3：混合方法（最稳妥）

同时提供图片和文件路径：

```javascript
return {
  content: [
    {
      type: "text",
      text: `截图完成，大小: ${sizeKB}KB，路径: ${filePath}`
    },
    {
      type: "image",
      data: base64,
      mimeType: "image/png"
    }
  ]
};
```

## 需要验证的问题

在实际部署前，你应该测试：

1. ✅ Agent SDK 是否真的接受 image 类型的 content
2. ✅ Claude 模型是否能正确处理返回的图片
3. ✅ 实际的大小限制是多少
4. ✅ 是否有性能影响（base64 编码会增加约 33% 大小）

## 测试建议

```javascript
// 最小化测试用例
const testServer = createSdkMcpServer({
  name: "test",
  tools: [
    tool("test_image", "测试图片返回", {}, async () => {
      // 一个很小的 1x1 红色像素 PNG（base64）
      const tinyRedPixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";
      
      return {
        content: [{
          type: "image",
          data: tinyRedPixel,
          mimeType: "image/png"
        }]
      };
    })
  ]
});

// 测试
for await (const msg of query({
  prompt: "请使用 test_image 工具并告诉我你看到了什么",
  options: {
    mcpServers: { test: testServer },
    allowedTools: ["mcp__test__test_image"]
  }
})) {
  console.log(msg);
}
```

如果这个最小测试能工作，那么你的截图工具理论上也能工作。

## 我之前教程的问题

我之前的教程在**格式上是正确的**，但存在以下问题：

1. ❌ 过于乐观 - 没有提到官方缺少示例
2. ❌ 没有提供备用方案
3. ✅ 压缩建议是对的
4. ✅ 1MB 限制警告是对的
5. ❌ 应该建议先做小规模测试

## 最终建议

**如果你现在就要实现截图功能**：
1. 先用方案 1（保存文件，返回路径）- 肯定能工作
2. 同时实验方案 2（直接返回图片）- 看是否真的支持
3. 如果方案 2 工作，再逐步迁移

**如果你有时间验证**：
1. 用最小测试用例验证 Agent SDK 是否真的支持图片
2. 如果支持，再实现完整的截图功能
3. 记录你的发现，因为官方文档缺失这部分内容

## 总结

我的原始结论在**技术规范层面是正确的**，但在**实践验证层面不够严谨**。

正确的说法应该是：
- ✅ MCP 协议支持返回图片
- ✅ Agent SDK 类型定义包含图片类型
- ⚠️ **但官方没有提供示例，需要你自己验证是否真的能工作**
- ⚠️ **即使能工作，也有严格的大小限制**
- ✅ **保险的做法是先保存文件，再让 Claude 读取**

抱歉我最初的回答过于肯定，没有强调这些重要的注意事项。
